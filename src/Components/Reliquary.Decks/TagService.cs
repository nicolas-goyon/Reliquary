using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Decks.Data;

namespace Reliquary.Decks;

/// <summary>
/// The operations on the tags of a user: list, rename, merge and delete.
/// </summary>
internal sealed class TagService(DeckStore store, IDomainEventWriter events, TimeProvider time)
{
    public async Task<IReadOnlyList<(Guid Id, string Name, int LineCount)>> ListGlobalAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await DeckStore.ListGlobalTagsAsync(connection, ownerId, cancellationToken);
    }

    /// <summary>
    /// Renames a tag. When another tag of the same kind has the new name, the deck lines of the tag go to that tag.
    /// </summary>
    /// <returns>The ID of the tag that has the name after the change.</returns>
    public async Task<Guid> RenameAsync(Guid ownerId, Guid tagId, string name, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var tag = await DeckStore.FindOwnedTagAsync(connection, transaction, ownerId, tagId, cancellationToken) ?? throw TagNotFound();
        var decks = await DeckStore.TouchDecksOfTagAsync(transaction, tagId, now, cancellationToken);
        var same = await DeckStore.FindTagByNameAsync(connection, transaction, ownerId, tag.DeckId, name, cancellationToken);
        var result = tagId;
        if (same is not null && same.Id != tagId)
        {
            await DeckStore.MergeTagAsync(transaction, tagId, same.Id, cancellationToken);
            result = same.Id;
        }
        else
        {
            await DeckStore.RenameTagAsync(transaction, tagId, name, now, cancellationToken);
        }

        await WriteEventsAsync(transaction, ownerId, decks, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Deletes a tag. The deck lines keep their cards and lose the tag.
    /// </summary>
    public async Task DeleteAsync(Guid ownerId, Guid tagId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        _ = await DeckStore.FindOwnedTagAsync(connection, transaction, ownerId, tagId, cancellationToken) ?? throw TagNotFound();
        var decks = await DeckStore.TouchDecksOfTagAsync(transaction, tagId, time.GetUtcNow().UtcDateTime, cancellationToken);
        await DeckStore.DeleteTagAsync(transaction, tagId, cancellationToken);
        await WriteEventsAsync(transaction, ownerId, decks, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task WriteEventsAsync(Npgsql.NpgsqlTransaction transaction, Guid ownerId, IReadOnlyList<DeckRow> decks, CancellationToken cancellationToken)
    {
        foreach (var deck in decks)
        {
            await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckUpdated(deck.Id, deck.Version, deck.Format, deck.Visibility), ownerId, cancellationToken);
        }
    }

    private static DeckException TagNotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The tag does not exist.");
}
