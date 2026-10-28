using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Decks.Data;
using Reliquary.Decks.Exploration;

namespace Reliquary.Decks.Moderation;

/// <summary>
/// The moderation of the public content: the reports of the users and the actions of the administrators.
/// See docs/decisions/0035-moderation-of-public-content.md.
/// </summary>
internal sealed class ModerationService(
    DeckStore store,
    PublicDeckIndexer indexer,
    IAccountDirectory accounts,
    IDomainEventWriter events,
    IOptions<ModerationOptions> options,
    TimeProvider time)
{
    /// <summary>
    /// Records the report of a user about a public deck or an author profile.
    /// </summary>
    /// <exception cref="DeckException">The item does not exist, the user owns it, the user has an open report about it, or the user sent too many reports.</exception>
    public async Task<ReportRow> ReportAsync(Guid reporterId, string itemType, Guid itemId, string reason, string comment, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenUsersAsync(cancellationToken);
        var (ownerId, name) = await FindPublicItemAsync(connection, itemType, itemId, cancellationToken);
        if (ownerId == reporterId)
        {
            throw DeckException.Validation(new FieldError("itemId", FieldErrorCodes.NotAllowed, "You cannot report your own content."));
        }

        var now = time.GetUtcNow().UtcDateTime;
        var limit = options.Value.ReportsPerDay;
        if (await ModerationStore.CountRecentReportsAsync(connection, reporterId, now.AddDays(-1), cancellationToken) >= limit)
        {
            throw new DeckException(
                StatusCodes.Status429TooManyRequests,
                ErrorCodes.RateLimited,
                $"You sent {limit} reports in the last 24 hours. Wait, then send the report again.");
        }

        var row = new ReportRow
        {
            Id = Ids.New(),
            ReporterId = reporterId,
            ItemType = itemType,
            ItemId = itemId,
            ItemOwnerId = ownerId,
            ItemName = name,
            Reason = reason,
            Comment = comment,
            Status = "open",
            CreatedAt = now,
        };
        try
        {
            await ModerationStore.InsertReportAsync(connection, row, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DeckException(
                StatusCodes.Status409Conflict,
                ModerationErrorCodes.ReportExists,
                "You have an open report about this item. An administrator reads it soon.");
        }

        return row;
    }

    /// <summary>
    /// Lists the reports with a status, with the names of the owners and of the reporters.
    /// </summary>
    public async Task<IReadOnlyList<ModerationReportResponse>> ListReportsAsync(string status, int offset, int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportRow> rows;
        await using (var connection = await store.OpenUsersAsync(cancellationToken))
        {
            rows = await ModerationStore.ListReportsAsync(connection, status, offset, limit, cancellationToken);
        }

        var profiles = await accounts.GetProfilesAsync([.. rows.SelectMany(row => new[] { row.ItemOwnerId, row.ReporterId }).Distinct()], cancellationToken);
        return [.. rows.Select(row => new ModerationReportResponse(
            row.Id,
            row.ItemType,
            row.ItemId,
            row.ItemType == ModerationValues.Author && profiles.TryGetValue(row.ItemId, out var author) ? author.DisplayName : row.ItemName,
            User(row.ItemOwnerId, profiles),
            User(row.ReporterId, profiles),
            row.Reason,
            row.Comment,
            row.Status,
            row.ItemHidden,
            Utc(row.CreatedAt),
            row.ClosedAt is { } closedAt ? Utc(closedAt) : null))];
    }

    /// <summary>
    /// Closes an open report: <c>resolved</c> or <c>dismissed</c>. The moderation log records the action.
    /// </summary>
    /// <exception cref="DeckException">The report does not exist, or it is not open.</exception>
    public async Task CloseReportAsync(Guid moderatorId, Guid reportId, string status, string reason, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenUsersAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var report = await ModerationStore.FindReportAsync(connection, reportId, cancellationToken, transaction)
            ?? throw new DeckException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The report does not exist.");
        var now = time.GetUtcNow().UtcDateTime;
        if (!await ModerationStore.CloseReportAsync(connection, transaction, reportId, status, moderatorId, now, cancellationToken))
        {
            throw new DeckException(
                StatusCodes.Status409Conflict,
                ModerationErrorCodes.ReportClosed,
                "An administrator closed this report before. Read the list of the reports again.");
        }

        await ModerationStore.InsertLogAsync(connection, transaction, new LogRow
        {
            Id = Ids.New(),
            CreatedAt = now,
            ModeratorId = moderatorId,
            Action = status == "resolved" ? "resolve" : "dismiss",
            ItemType = report.ItemType,
            ItemId = report.ItemId,
            ItemOwnerId = report.ItemOwnerId,
            ItemName = report.ItemName,
            ReportId = reportId,
            Reason = reason,
        }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Hides a deck or an author profile. The read model of the public decks loses the decks in the same transaction,
    /// so the search does not show them after the response. The open reports of the item become resolved.
    /// </summary>
    /// <exception cref="DeckException">The item does not exist.</exception>
    public async Task HideAsync(Guid moderatorId, string itemType, Guid itemId, string reason, CancellationToken cancellationToken)
    {
        var (ownerId, name) = await FindItemAsync(itemType, itemId, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        await using var connection = await store.OpenUsersAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var hidden = new HiddenRow
        {
            ItemType = itemType,
            ItemId = itemId,
            ItemOwnerId = ownerId,
            ItemName = name,
            HiddenAt = now,
            HiddenBy = moderatorId,
            Reason = reason,
        };
        if (!await ModerationStore.InsertHiddenAsync(connection, transaction, hidden, cancellationToken))
        {
            // The item is hidden. A second request changes nothing.
            return;
        }

        await ModerationStore.RemovePublicDecksAsync(connection, transaction, itemType, itemId, cancellationToken);
        await WriteLogAsync(connection, transaction, moderatorId, "hide", hidden, null, now, cancellationToken);
        foreach (var reportId in await ModerationStore.ResolveItemReportsAsync(connection, transaction, itemType, itemId, moderatorId, now, cancellationToken))
        {
            await WriteLogAsync(connection, transaction, moderatorId, "resolve", hidden, reportId, now, cancellationToken);
        }

        await events.WriteAsync(DatabaseTarget.Users, transaction, new ContentHidden(itemType, itemId), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Shows a hidden deck or author profile again. The read model of the public decks gets the public decks again in the same request.
    /// </summary>
    /// <exception cref="DeckException">The item is not hidden.</exception>
    public async Task ShowAsync(Guid moderatorId, string itemType, Guid itemId, string reason, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        HiddenRow removed;
        await using (var connection = await store.OpenUsersAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            removed = await ModerationStore.DeleteHiddenAsync(connection, transaction, itemType, itemId, cancellationToken)
                ?? throw new DeckException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The item is not hidden.");
            removed.Reason = reason;
            await WriteLogAsync(connection, transaction, moderatorId, "show", removed, null, now, cancellationToken);
            await events.WriteAsync(DatabaseTarget.Users, transaction, new ContentShown(itemType, itemId), removed.ItemOwnerId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        IReadOnlyList<Guid> deckIds = [itemId];
        if (itemType == ModerationValues.Author)
        {
            await using var connection = await store.OpenUsersAsync(cancellationToken);
            deckIds = await ModerationStore.ListDecksOfOwnerAsync(connection, itemId, cancellationToken);
        }

        foreach (var deckId in deckIds)
        {
            await indexer.IndexAsync(removed.ItemOwnerId, deckId, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<HiddenItemResponse>> ListHiddenAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<HiddenRow> rows;
        await using (var connection = await store.OpenUsersAsync(cancellationToken))
        {
            rows = await ModerationStore.ListHiddenAsync(connection, offset, limit, cancellationToken);
        }

        var profiles = await accounts.GetProfilesAsync([.. rows.Select(row => row.ItemOwnerId).Distinct()], cancellationToken);
        return [.. rows.Select(row => new HiddenItemResponse(row.ItemType, row.ItemId, row.ItemName, User(row.ItemOwnerId, profiles), row.Reason, Utc(row.HiddenAt)))];
    }

    public async Task<IReadOnlyList<ModerationLogEntryResponse>> ListLogAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<LogRow> rows;
        await using (var connection = await store.OpenUsersAsync(cancellationToken))
        {
            rows = await ModerationStore.ListLogAsync(connection, offset, limit, cancellationToken);
        }

        var profiles = await accounts.GetProfilesAsync([.. rows.Select(row => row.ModeratorId).OfType<Guid>().Distinct()], cancellationToken);
        return [.. rows.Select(row => new ModerationLogEntryResponse(
            row.Id,
            row.Action,
            row.ItemType,
            row.ItemId,
            row.ItemName,
            row.ModeratorId is { } moderatorId ? User(moderatorId, profiles) : null,
            row.ReportId,
            row.Reason,
            Utc(row.CreatedAt)))];
    }

    /// <summary>
    /// Finds a public deck in the read model, or an author profile that is not hidden.
    /// </summary>
    private async Task<(Guid OwnerId, string Name)> FindPublicItemAsync(NpgsqlConnection connection, string itemType, Guid itemId, CancellationToken cancellationToken)
    {
        if (itemType == ModerationValues.Deck)
        {
            // The read model contains only the public decks that are not hidden.
            var deck = await PublicDeckStore.FindAsync(connection, itemId, cancellationToken) ?? throw DeckException.NotFound();
            return (deck.OwnerId, deck.Name);
        }

        var profiles = await accounts.GetProfilesAsync([itemId], cancellationToken);
        if (!profiles.TryGetValue(itemId, out var profile) || await ModerationStore.IsHiddenAsync(connection, ModerationValues.Author, itemId, cancellationToken))
        {
            throw PublicDeckService.AuthorNotFound();
        }

        return (itemId, profile.DisplayName);
    }

    /// <summary>
    /// Finds any deck or account, with its owner and its name.
    /// </summary>
    private async Task<(Guid OwnerId, string Name)> FindItemAsync(string itemType, Guid itemId, CancellationToken cancellationToken)
    {
        if (itemType == ModerationValues.Author)
        {
            var profiles = await accounts.GetProfilesAsync([itemId], cancellationToken);
            return profiles.TryGetValue(itemId, out var profile) ? (itemId, profile.DisplayName) : throw PublicDeckService.AuthorNotFound();
        }

        var ownerId = await store.FindOwnerAsync(itemId, cancellationToken) ?? throw DeckException.NotFound();
        await using var content = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        var deck = await DeckStore.FindDeckAsync(content, ownerId, itemId, cancellationToken) ?? throw DeckException.NotFound();
        return (ownerId, deck.Name);
    }

    private static Task WriteLogAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid moderatorId, string action, HiddenRow item, Guid? reportId, DateTime now, CancellationToken cancellationToken) =>
        ModerationStore.InsertLogAsync(connection, transaction, new LogRow
        {
            Id = Ids.New(),
            CreatedAt = now,
            ModeratorId = moderatorId,
            Action = action,
            ItemType = item.ItemType,
            ItemId = item.ItemId,
            ItemOwnerId = item.ItemOwnerId,
            ItemName = item.ItemName,
            ReportId = reportId,
            Reason = item.Reason,
        }, cancellationToken);

    private static ModerationUserResponse User(Guid accountId, IReadOnlyDictionary<Guid, AccountProfile> profiles) =>
        new(accountId, profiles.TryGetValue(accountId, out var profile) ? profile.DisplayName : string.Empty);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
