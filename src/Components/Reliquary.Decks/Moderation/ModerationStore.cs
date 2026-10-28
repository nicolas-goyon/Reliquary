using Dapper;
using Npgsql;

namespace Reliquary.Decks.Moderation;

/// <summary>A report as the table <c>users.moderation_reports</c> stores it.</summary>
internal sealed class ReportRow
{
    public Guid Id { get; set; }

    public Guid ReporterId { get; set; }

    public string ItemType { get; set; } = string.Empty;

    public Guid ItemId { get; set; }

    public Guid ItemOwnerId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public string Status { get; set; } = "open";

    public DateTime CreatedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    /// <summary>Gets or sets whether the item is hidden now. Only the list of the reports reads it.</summary>
    public bool ItemHidden { get; set; }
}

/// <summary>A hidden item as the table <c>users.moderation_hidden</c> stores it.</summary>
internal sealed class HiddenRow
{
    public string ItemType { get; set; } = string.Empty;

    public Guid ItemId { get; set; }

    public Guid ItemOwnerId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public DateTime HiddenAt { get; set; }

    public Guid? HiddenBy { get; set; }

    public string Reason { get; set; } = string.Empty;
}

/// <summary>An entry of the table <c>users.moderation_log</c>.</summary>
internal sealed class LogRow
{
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? ModeratorId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string ItemType { get; set; } = string.Empty;

    public Guid? ItemId { get; set; }

    public Guid? ItemOwnerId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public Guid? ReportId { get; set; }

    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// The SQL of the moderation tables, in the users database.
/// </summary>
internal static class ModerationStore
{
    private const string ReportColumns = """
        r.id, r.reporter_id as ReporterId, r.item_type as ItemType, r.item_id as ItemId, r.item_owner_id as ItemOwnerId,
        r.item_name as ItemName, r.reason, r.comment, r.status, r.created_at as CreatedAt, r.closed_at as ClosedAt, r.closed_by as ClosedBy
        """;

    public static Task<bool> IsHiddenAsync(NpgsqlConnection connection, string itemType, Guid itemId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from users.moderation_hidden where item_type = @itemType and item_id = @itemId)",
            new { itemType, itemId },
            transaction,
            cancellationToken: cancellationToken));

    public static Task<int> CountRecentReportsAsync(NpgsqlConnection connection, Guid reporterId, DateTime since, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*)::int from users.moderation_reports where reporter_id = @reporterId and created_at > @since",
            new { reporterId, since },
            cancellationToken: cancellationToken));

    public static Task InsertReportAsync(NpgsqlConnection connection, ReportRow row, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.moderation_reports (id, reporter_id, item_type, item_id, item_owner_id, item_name, reason, comment, status, created_at)
            values (@Id, @ReporterId, @ItemType, @ItemId, @ItemOwnerId, @ItemName, @Reason, @Comment, @Status, @CreatedAt)
            """,
            row,
            cancellationToken: cancellationToken));

    public static Task<ReportRow?> FindReportAsync(NpgsqlConnection connection, Guid reportId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        connection.QuerySingleOrDefaultAsync<ReportRow>(new CommandDefinition(
            $"select {ReportColumns} from users.moderation_reports r where r.id = @reportId",
            new { reportId },
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>
    /// Lists the reports with a status, the oldest first, so the administrators read the reports in their order of arrival.
    /// </summary>
    public static async Task<IReadOnlyList<ReportRow>> ListReportsAsync(NpgsqlConnection connection, string status, int offset, int limit, CancellationToken cancellationToken) =>
        [.. await connection.QueryAsync<ReportRow>(new CommandDefinition(
            $"""
            select {ReportColumns},
                exists (select 1 from users.moderation_hidden h where h.item_type = r.item_type and h.item_id = r.item_id) as ItemHidden
            from users.moderation_reports r
            where r.status = @status
            order by case when @status = 'open' then r.created_at end, r.closed_at desc, r.created_at desc, r.id
            offset @offset
            limit @limit
            """,
            new { status, offset, limit },
            cancellationToken: cancellationToken))];

    /// <summary>
    /// Closes an open report.
    /// </summary>
    /// <returns><see langword="true"/> when the report was open.</returns>
    public static async Task<bool> CloseReportAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid reportId, string status, Guid moderatorId, DateTime now, CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            "update users.moderation_reports set status = @status, closed_at = @now, closed_by = @moderatorId where id = @reportId and status = 'open'",
            new { reportId, status, moderatorId, now },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    /// <summary>
    /// Resolves all the open reports of an item.
    /// </summary>
    /// <returns>The IDs of the reports.</returns>
    public static async Task<IReadOnlyList<Guid>> ResolveItemReportsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string itemType, Guid itemId, Guid moderatorId, DateTime now, CancellationToken cancellationToken) =>
        [.. await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            update users.moderation_reports set status = 'resolved', closed_at = @now, closed_by = @moderatorId
            where item_type = @itemType and item_id = @itemId and status = 'open'
            returning id
            """,
            new { itemType, itemId, moderatorId, now },
            transaction,
            cancellationToken: cancellationToken))];

    /// <summary>
    /// Records a hidden item.
    /// </summary>
    /// <returns><see langword="true"/> when the item was not hidden before.</returns>
    public static async Task<bool> InsertHiddenAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, HiddenRow row, CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.moderation_hidden (item_type, item_id, item_owner_id, item_name, hidden_at, hidden_by, reason)
            values (@ItemType, @ItemId, @ItemOwnerId, @ItemName, @HiddenAt, @HiddenBy, @Reason)
            on conflict do nothing
            """,
            row,
            transaction,
            cancellationToken: cancellationToken)) == 1;

    /// <summary>
    /// Removes a hidden item.
    /// </summary>
    /// <returns>The removed row, or <see langword="null"/> when the item was not hidden.</returns>
    public static Task<HiddenRow?> DeleteHiddenAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string itemType, Guid itemId, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<HiddenRow>(new CommandDefinition(
            """
            delete from users.moderation_hidden where item_type = @itemType and item_id = @itemId
            returning item_type as ItemType, item_id as ItemId, item_owner_id as ItemOwnerId, item_name as ItemName, hidden_at as HiddenAt, hidden_by as HiddenBy, reason
            """,
            new { itemType, itemId },
            transaction,
            cancellationToken: cancellationToken));

    public static async Task<IReadOnlyList<HiddenRow>> ListHiddenAsync(NpgsqlConnection connection, int offset, int limit, CancellationToken cancellationToken) =>
        [.. await connection.QueryAsync<HiddenRow>(new CommandDefinition(
            """
            select item_type as ItemType, item_id as ItemId, item_owner_id as ItemOwnerId, item_name as ItemName, hidden_at as HiddenAt, hidden_by as HiddenBy, reason
            from users.moderation_hidden
            order by hidden_at desc, item_id
            offset @offset
            limit @limit
            """,
            new { offset, limit },
            cancellationToken: cancellationToken))];

    /// <summary>
    /// Removes the public deck rows of a hidden deck or of all the decks of a hidden author from the read model.
    /// </summary>
    public static Task RemovePublicDecksAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string itemType, Guid itemId, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            itemType == ModerationValues.Deck
                ? "delete from users.public_decks where deck_id = @itemId"
                : "delete from users.public_decks where owner_id = @itemId",
            new { itemId },
            transaction,
            cancellationToken: cancellationToken));

    public static Task InsertLogAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, LogRow row, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.moderation_log (id, created_at, moderator_id, action, item_type, item_id, item_owner_id, item_name, report_id, reason)
            values (@Id, @CreatedAt, @ModeratorId, @Action, @ItemType, @ItemId, @ItemOwnerId, @ItemName, @ReportId, @Reason)
            """,
            row,
            transaction,
            cancellationToken: cancellationToken));

    public static async Task<IReadOnlyList<LogRow>> ListLogAsync(NpgsqlConnection connection, int offset, int limit, CancellationToken cancellationToken) =>
        [.. await connection.QueryAsync<LogRow>(new CommandDefinition(
            """
            select id, created_at as CreatedAt, moderator_id as ModeratorId, action, item_type as ItemType, item_id as ItemId,
                item_owner_id as ItemOwnerId, item_name as ItemName, report_id as ReportId, reason
            from users.moderation_log
            order by created_at desc, id desc
            offset @offset
            limit @limit
            """,
            new { offset, limit },
            cancellationToken: cancellationToken))];

    /// <summary>
    /// Lists the decks of an owner in the deck directory.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> ListDecksOfOwnerAsync(NpgsqlConnection connection, Guid ownerId, CancellationToken cancellationToken) =>
        [.. await connection.QueryAsync<Guid>(new CommandDefinition(
            "select deck_id from users.deck_directory where owner_id = @ownerId",
            new { ownerId },
            cancellationToken: cancellationToken))];
}
