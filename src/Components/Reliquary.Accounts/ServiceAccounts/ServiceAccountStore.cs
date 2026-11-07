using Dapper;
using Npgsql;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Accounts.ServiceAccounts;

/// <summary>
/// A service account as the <c>users.service_accounts</c> table stores it.
/// </summary>
internal sealed class ServiceAccountRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public byte[] SecretHash { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// A delegation, with the name and the description of its service account.
/// </summary>
internal sealed class DelegationRow
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid ServiceAccountId { get; set; }

    public string ServiceAccountName { get; set; } = string.Empty;

    public string ServiceAccountDescription { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// A valid delegation token, with the fields of its delegation and of its account that the authentication needs.
/// </summary>
internal sealed class DelegationTokenRow
{
    public Guid Id { get; set; }

    public Guid DelegationId { get; set; }

    public Guid AccountId { get; set; }

    public Guid ServiceAccountId { get; set; }

    public byte[] SecretHash { get; set; } = [];

    public string[] Scopes { get; set; } = [];

    public DateTime? DelegationLastUsedAt { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Language { get; set; } = "en";
}

/// <summary>
/// Reads and writes the service accounts, the delegations and the delegation tokens in the <c>users</c> schema.
/// </summary>
/// <param name="database">The databases of the instance.</param>
internal sealed class ServiceAccountStore(IDatabase database)
{
    private const string ServiceAccountColumns = """
        id, name, description, prefix, secret_hash as SecretHash, created_at as CreatedAt, last_used_at as LastUsedAt, revoked_at as RevokedAt
        """;

    private const string DelegationColumns = """
        d.id, d.account_id as AccountId, d.service_account_id as ServiceAccountId, s.name as ServiceAccountName,
        s.description as ServiceAccountDescription, d.scopes, d.created_at as CreatedAt, d.expires_at as ExpiresAt,
        d.last_used_at as LastUsedAt, d.revoked_at as RevokedAt
        """;

    // A delegation is active when nobody revoked it or its service account, and when it did not expire.
    private const string ActiveDelegation = "d.revoked_at is null and s.revoked_at is null and d.expires_at > @now";

    public async Task<bool> InsertServiceAccountAsync(ServiceAccountRow row, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.service_accounts (id, name, description, prefix, secret_hash, created_at)
            values (@Id, @Name, @Description, @Prefix, @SecretHash, @CreatedAt)
            on conflict (name) do nothing
            """,
            row,
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task<IReadOnlyList<ServiceAccountRow>> ListServiceAccountsAsync(bool includeRevoked, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        var rows = await connection.QueryAsync<ServiceAccountRow>(new CommandDefinition(
            $"select {ServiceAccountColumns} from users.service_accounts where @includeRevoked or revoked_at is null order by name",
            new { includeRevoked },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<ServiceAccountRow?> FindServiceAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ServiceAccountRow>(new CommandDefinition(
            $"select {ServiceAccountColumns} from users.service_accounts where id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<ServiceAccountRow?> FindServiceAccountByNameAsync(string name, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ServiceAccountRow>(new CommandDefinition(
            $"select {ServiceAccountColumns} from users.service_accounts where name = @name",
            new { name },
            cancellationToken: cancellationToken));
    }

    public async Task<ServiceAccountRow?> FindActiveServiceAccountByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ServiceAccountRow>(new CommandDefinition(
            $"select {ServiceAccountColumns} from users.service_accounts where prefix = @prefix and revoked_at is null",
            new { prefix },
            cancellationToken: cancellationToken));
    }

    public async Task MarkServiceAccountUsedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "update users.service_accounts set last_used_at = @now where id = @id",
            new { id, now },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Revokes a service account and all its delegations in one transaction.
    /// </summary>
    /// <returns><see langword="true"/> when the service account was active.</returns>
    public async Task<bool> RevokeServiceAccountAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var revoked = await connection.ExecuteAsync(new CommandDefinition(
            "update users.service_accounts set revoked_at = @now where id = @id and revoked_at is null",
            new { id, now },
            transaction,
            cancellationToken: cancellationToken)) == 1;
        await connection.ExecuteAsync(new CommandDefinition(
            "update users.delegations set revoked_at = @now where service_account_id = @id and revoked_at is null",
            new { id, now },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return revoked;
    }

    public async Task<int> CountActiveDelegationsAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"""
            select count(*) from users.delegations d
            join users.service_accounts s on s.id = d.service_account_id
            where d.account_id = @accountId and {ActiveDelegation}
            """,
            new { accountId, now },
            cancellationToken: cancellationToken));
    }

    public async Task InsertDelegationAsync(DelegationRow row, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.delegations (id, account_id, service_account_id, scopes, created_at, expires_at)
            values (@Id, @AccountId, @ServiceAccountId, @Scopes, @CreatedAt, @ExpiresAt)
            """,
            row,
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Lists the active delegations of a user.
    /// </summary>
    public async Task<IReadOnlyList<DelegationRow>> ListActiveDelegationsOfAccountAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        var rows = await connection.QueryAsync<DelegationRow>(new CommandDefinition(
            $"""
            select {DelegationColumns} from users.delegations d
            join users.service_accounts s on s.id = d.service_account_id
            where d.account_id = @accountId and {ActiveDelegation}
            order by d.created_at desc, d.id desc
            """,
            new { accountId, now },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    /// <summary>
    /// Lists all the delegations of a user, also the revoked and the expired delegations, for the export of the personal data.
    /// </summary>
    public async Task<IReadOnlyList<DelegationRow>> ListAllDelegationsOfAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        var rows = await connection.QueryAsync<DelegationRow>(new CommandDefinition(
            $"""
            select {DelegationColumns} from users.delegations d
            join users.service_accounts s on s.id = d.service_account_id
            where d.account_id = @accountId
            order by d.created_at, d.id
            """,
            new { accountId },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<bool> RevokeDelegationAsync(Guid accountId, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "update users.delegations set revoked_at = @now where id = @id and account_id = @accountId and revoked_at is null",
            new { id, accountId, now },
            cancellationToken: cancellationToken)) == 1;
    }

    /// <summary>
    /// Lists the active delegations that users gave to a service account.
    /// </summary>
    public async Task<IReadOnlyList<DelegationRow>> ListActiveDelegationsOfServiceAccountAsync(Guid serviceAccountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        var rows = await connection.QueryAsync<DelegationRow>(new CommandDefinition(
            $"""
            select {DelegationColumns} from users.delegations d
            join users.service_accounts s on s.id = d.service_account_id
            where d.service_account_id = @serviceAccountId and {ActiveDelegation}
            order by d.created_at, d.id
            """,
            new { serviceAccountId, now },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<DelegationRow?> FindActiveDelegationAsync(Guid serviceAccountId, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<DelegationRow>(new CommandDefinition(
            $"""
            select {DelegationColumns} from users.delegations d
            join users.service_accounts s on s.id = d.service_account_id
            where d.id = @id and d.service_account_id = @serviceAccountId and {ActiveDelegation}
            """,
            new { id, serviceAccountId, now },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Adds a delegation token, and deletes the expired tokens of the same delegation.
    /// </summary>
    public async Task InsertDelegationTokenAsync(Guid id, Guid delegationId, string prefix, byte[] secretHash, string[] scopes, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            delete from users.delegation_tokens where delegation_id = @delegationId and expires_at <= @now;
            insert into users.delegation_tokens (id, delegation_id, prefix, secret_hash, scopes, created_at, expires_at)
            values (@id, @delegationId, @prefix, @secretHash, @scopes, @now, @expiresAt);
            """,
            new { id, delegationId, prefix, secretHash, scopes, now, expiresAt },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Finds a delegation token that is valid now: the token, its delegation and its service account are active.
    /// </summary>
    public async Task<DelegationTokenRow?> FindValidDelegationTokenAsync(string prefix, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<DelegationTokenRow>(new CommandDefinition(
            $"""
            select t.id, t.delegation_id as DelegationId, d.account_id as AccountId, d.service_account_id as ServiceAccountId,
                t.secret_hash as SecretHash, t.scopes, d.last_used_at as DelegationLastUsedAt,
                a.display_name as DisplayName, a.language
            from users.delegation_tokens t
            join users.delegations d on d.id = t.delegation_id
            join users.service_accounts s on s.id = d.service_account_id
            join users.accounts a on a.id = d.account_id
            where t.prefix = @prefix and t.expires_at > @now and {ActiveDelegation}
            """,
            new { prefix, now },
            cancellationToken: cancellationToken));
    }

    public async Task MarkDelegationUsedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "update users.delegations set last_used_at = @now where id = @id",
            new { id, now },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Deletes the delegations of a user and their tokens, in the transaction of the deletion of the account.
    /// </summary>
    public static Task DeleteDelegationsOfAccountAsync(NpgsqlTransaction transaction, Guid accountId, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "delete from users.delegations where account_id = @accountId",
            new { accountId },
            transaction,
            cancellationToken: cancellationToken));
}
