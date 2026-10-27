using Dapper;
using Npgsql;

namespace Reliquary.Accounts.PersonalData;

/// <summary>
/// An export of the data of an account.
/// </summary>
internal sealed class DataExportRow
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string Status { get; set; } = "pending";

    public DateTime RequestedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public long? SizeBytes { get; set; }
}

/// <summary>
/// The SQL of the exports and of the deletions of accounts.
/// </summary>
internal static class PersonalDataStore
{
    private const string ExportColumns = """
        id, account_id as AccountId, status, requested_at as RequestedAt, completed_at as CompletedAt, expires_at as ExpiresAt, size_bytes as SizeBytes
        """;

    public static Task InsertExportAsync(NpgsqlConnection connection, DataExportRow row, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "insert into users.data_exports (id, account_id, status, requested_at) values (@Id, @AccountId, @Status, @RequestedAt)",
            row,
            cancellationToken: cancellationToken));

    public static async Task<IReadOnlyList<DataExportRow>> ListExportsAsync(NpgsqlConnection connection, Guid accountId, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<DataExportRow>(new CommandDefinition(
            $"select {ExportColumns} from users.data_exports where account_id = @accountId order by requested_at desc limit 20",
            new { accountId },
            cancellationToken: cancellationToken))).ToList();

    public static Task<bool> HasPendingExportAsync(NpgsqlConnection connection, Guid accountId, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from users.data_exports where account_id = @accountId and status = 'pending')",
            new { accountId },
            cancellationToken: cancellationToken));

    public static Task<byte[]?> GetArchiveAsync(NpgsqlConnection connection, Guid accountId, Guid exportId, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<byte[]?>(new CommandDefinition(
            "select archive from users.data_exports where id = @exportId and account_id = @accountId and status = 'ready' and expires_at > @now",
            new { exportId, accountId, now },
            cancellationToken: cancellationToken));

    /// <summary>
    /// Takes the oldest pending export. Another worker replica skips the locked row.
    /// </summary>
    public static Task<DataExportRow?> TakePendingExportAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<DataExportRow>(new CommandDefinition(
            $"select {ExportColumns} from users.data_exports where status = 'pending' order by requested_at limit 1 for update skip locked",
            transaction: transaction,
            cancellationToken: cancellationToken));

    public static Task CompleteExportAsync(NpgsqlTransaction transaction, Guid exportId, byte[]? archive, DateTime now, DateTime expiresAt, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            update users.data_exports
            set status = @status, archive = @archive, size_bytes = @size, completed_at = @now, expires_at = @expiresAt
            where id = @exportId
            """,
            new { exportId, archive, status = archive is null ? "failed" : "ready", size = archive?.LongLength, now, expiresAt },
            transaction,
            cancellationToken: cancellationToken));

    public static Task<int> DeleteExpiredExportsAsync(NpgsqlConnection connection, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "delete from users.data_exports where expires_at <= @now",
            new { now },
            cancellationToken: cancellationToken));

    public static Task<int> SetDeletionRequestAsync(NpgsqlConnection connection, Guid accountId, DateTime? requestedAt, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "update users.accounts set deletion_requested_at = @requestedAt where id = @accountId",
            new { accountId, requestedAt },
            cancellationToken: cancellationToken));

    public static Task<DateTime?> GetDeletionRequestAsync(NpgsqlConnection connection, Guid accountId, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "select deletion_requested_at from users.accounts where id = @accountId",
            new { accountId },
            cancellationToken: cancellationToken));

    public static async Task<IReadOnlyList<Guid>> ListDueDeletionsAsync(NpgsqlConnection connection, DateTime before, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<Guid>(new CommandDefinition(
            "select id from users.accounts where deletion_requested_at <= @before order by deletion_requested_at limit 100",
            new { before },
            cancellationToken: cancellationToken))).ToList();

    /// <summary>
    /// Deletes the account and the data of the Accounts component. The foreign keys delete the sessions, the tokens, the keys and the exports.
    /// </summary>
    public static Task DeleteAccountAsync(NpgsqlTransaction transaction, Guid accountId, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            delete from users.usage_counters
            where (subject_kind = 'account' and subject_id = @accountId)
                or (subject_kind = 'key' and subject_id in (select id from users.api_keys where account_id = @accountId));
            delete from users.accounts where id = @accountId;
            """,
            new { accountId },
            transaction,
            cancellationToken: cancellationToken));
}
