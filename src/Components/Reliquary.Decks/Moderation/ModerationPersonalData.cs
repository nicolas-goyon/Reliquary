using Dapper;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Moderation;

/// <summary>
/// Gives the moderation data of an account for the export, and deletes it for the deletion of an account.
/// </summary>
/// <remarks>
/// The deletion deletes the reports of the user, the reports about the content of the user and the hidden items of the user.
/// The moderation log keeps its entries, but the deletion removes the IDs and the names of the account from them.
/// See docs/decisions/0035-moderation-of-public-content.md.
/// </remarks>
internal sealed class ModerationPersonalData(DeckStore store) : IPersonalDataProvider
{
    public string Name => "moderation";

    public async Task ExportAsync(Guid ownerId, IPersonalDataArchive archive, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenUsersAsync(cancellationToken);
        var reports = await connection.QueryAsync<(Guid Id, string ItemType, Guid ItemId, string ItemName, string Reason, string Comment, string Status, DateTime CreatedAt, DateTime? ClosedAt)>(new CommandDefinition(
            """
            select id, item_type, item_id, item_name, reason, comment, status, created_at, closed_at
            from users.moderation_reports where reporter_id = @ownerId order by created_at, id
            """,
            new { ownerId },
            cancellationToken: cancellationToken));
        await archive.WriteJsonAsync(
            "reports.json",
            reports.Select(row => new { row.Id, row.ItemType, row.ItemId, row.ItemName, row.Reason, row.Comment, row.Status, row.CreatedAt, row.ClosedAt }),
            cancellationToken);

        var hidden = await connection.QueryAsync<(string ItemType, Guid ItemId, string ItemName, DateTime HiddenAt, string Reason)>(new CommandDefinition(
            "select item_type, item_id, item_name, hidden_at, reason from users.moderation_hidden where item_owner_id = @ownerId order by hidden_at",
            new { ownerId },
            cancellationToken: cancellationToken));
        await archive.WriteJsonAsync("hidden-content.json", hidden.Select(row => new { row.ItemType, row.ItemId, row.ItemName, row.HiddenAt, row.Reason }), cancellationToken);

        var actions = await connection.QueryAsync<(string Action, string ItemType, Guid? ItemId, string ItemName, Guid? ReportId, string Reason, DateTime CreatedAt)>(new CommandDefinition(
            """
            select action, item_type, item_id, item_name, report_id, reason, created_at
            from users.moderation_log where moderator_id = @ownerId order by created_at, id
            """,
            new { ownerId },
            cancellationToken: cancellationToken));
        await archive.WriteJsonAsync(
            "moderation-actions.json",
            actions.Select(row => new { row.Action, row.ItemType, row.ItemId, row.ItemName, row.ReportId, row.Reason, row.CreatedAt }),
            cancellationToken);
    }

    public async Task DeleteAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenUsersAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update users.moderation_log set report_id = null
            where report_id in (select id from users.moderation_reports where reporter_id = @ownerId or item_owner_id = @ownerId);
            update users.moderation_log set item_id = null, item_owner_id = null, item_name = '' where item_owner_id = @ownerId;
            update users.moderation_log set moderator_id = null where moderator_id = @ownerId;
            delete from users.moderation_reports where reporter_id = @ownerId or item_owner_id = @ownerId;
            update users.moderation_reports set closed_by = null where closed_by = @ownerId;
            delete from users.moderation_hidden where item_owner_id = @ownerId;
            update users.moderation_hidden set hidden_by = null where hidden_by = @ownerId;
            """,
            new { ownerId },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }
}
