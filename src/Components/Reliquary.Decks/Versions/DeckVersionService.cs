using Microsoft.AspNetCore.Http;
using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Versions;

/// <summary>
/// The deck versions and the variants. See docs/decisions/0033-deck-versions-and-variants.md.
/// </summary>
internal sealed class DeckVersionService(DeckStore store, DeckService decks, IDomainEventWriter events, TimeProvider time)
{
    /// <summary>The maximum length of a version message.</summary>
    public const int MaxMessageLength = 500;

    /// <summary>
    /// Lists the versions of a deck, the most recent first.
    /// </summary>
    /// <returns>The versions, and whether the working list differs from the most recent version.</returns>
    public async Task<(IReadOnlyList<VersionRow> Versions, bool WorkingListChanged, DeckRow Deck)> ListAsync(Guid? viewerId, Guid deckId, CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(viewerId, deckId, cancellationToken);
        await using var connection = await store.OpenAsync(view.Deck.OwnerId, ContentAccess.Read, cancellationToken);
        var versions = await VersionStore.ListAsync(connection, deckId, cancellationToken);
        var head = view.Deck.HeadVersionId is { } headId ? await VersionStore.ListLinesAsync(connection, headId, cancellationToken) : [];
        var changed = view.Deck.HeadVersionId is null
            ? view.Lines.Count > 0
            : !Same(Freeze(view.Lines, view.Tags), head);
        return (versions, changed, view.Deck);
    }

    /// <summary>
    /// Gets a version and its frozen lines.
    /// </summary>
    public async Task<(VersionRow Version, IReadOnlyList<VersionLineRow> Lines, DeckRow Deck)> GetAsync(Guid? viewerId, Guid deckId, Guid versionId, CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(viewerId, deckId, cancellationToken);
        await using var connection = await store.OpenAsync(view.Deck.OwnerId, ContentAccess.Read, cancellationToken);
        var version = await VersionStore.FindAsync(connection, versionId, cancellationToken);
        if (version is null || version.DeckId != deckId)
        {
            throw VersionNotFound();
        }

        return (version, await VersionStore.ListLinesAsync(connection, versionId, cancellationToken), view.Deck);
    }

    /// <summary>
    /// Compares a version with its first parent. The first version compares with an empty list.
    /// </summary>
    public async Task<ListComparison> ChangesAsync(Guid? viewerId, Guid deckId, Guid versionId, CancellationToken cancellationToken)
    {
        var (version, lines, deck) = await GetAsync(viewerId, deckId, versionId, cancellationToken);
        IReadOnlyList<VersionLineRow> parentLines = [];
        if (version.ParentIds.Length > 0)
        {
            await using var connection = await store.OpenAsync(deck.OwnerId, ContentAccess.Read, cancellationToken);
            parentLines = await VersionStore.ListLinesAsync(connection, version.ParentIds[0], cancellationToken);
        }

        return ListComparer.Compare(parentLines.Select(ToCompared), lines.Select(ToCompared));
    }

    /// <summary>
    /// Commits the working list of a deck as a new version.
    /// </summary>
    /// <exception cref="DeckException">The deck does not exist, or the working list did not change since the last version.</exception>
    public async Task<VersionRow> CommitAsync(Guid ownerId, Guid deckId, string message, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var deck = await DeckStore.FindDeckAsync(connection, ownerId, deckId, cancellationToken, transaction, forUpdate: true) ?? throw DeckException.NotFound();
        var lines = await DeckStore.ListLinesAsync(connection, deckId, cancellationToken, transaction);
        var frozen = await FreezeAsync(connection, lines, cancellationToken);
        if (deck.HeadVersionId is { } headId && Same(frozen, await VersionStore.ListLinesAsync(connection, headId, cancellationToken, transaction)))
        {
            throw new DeckException(StatusCodes.Status409Conflict, DeckErrorCodes.NoChanges, "The working list did not change since the last version. Change the deck, then commit.");
        }

        var version = await InsertVersionAsync(transaction, deck, "commit", message, deck.HeadVersionId is { } parent ? [parent] : [], frozen, cancellationToken);
        await DeckStore.UpdateDeckAsync(transaction, deck, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return version;
    }

    /// <summary>
    /// Copies a version into the working list and commits the result as a version of the kind <c>restore</c>.
    /// </summary>
    public async Task<VersionRow> RestoreAsync(Guid ownerId, Guid deckId, Guid versionId, string? message, CancellationToken cancellationToken)
    {
        var (version, lines, _) = await GetAsync(ownerId, deckId, versionId, cancellationToken);
        return await ReplaceAndCommitAsync(ownerId, deckId, lines, "restore", message ?? $"Restore of version {version.Number}", null, cancellationToken);
    }

    /// <summary>
    /// Creates a variant of a deck from one of its versions. With no version, the variant starts from the most recent version.
    /// When the deck has no version, the method commits the working list first.
    /// </summary>
    public async Task<DeckRow> CreateVariantAsync(Guid ownerId, Guid deckId, string name, Guid? baseVersionId, CancellationToken cancellationToken)
    {
        var main = (await decks.GetAsync(ownerId, deckId, cancellationToken)).Deck;
        if (main.OwnerId != ownerId)
        {
            throw DeckException.NotFound();
        }

        if (main.VariantOf is not null)
        {
            throw DeckException.Validation(new FieldError("deckId", FieldErrorCodes.NotAllowed, "A variant cannot have variants. Make the variant from the main deck."));
        }

        var baseId = baseVersionId ?? main.HeadVersionId ?? (await CommitAsync(ownerId, deckId, "First version", cancellationToken)).Id;
        var (baseVersion, lines, _) = await GetAsync(ownerId, deckId, baseId, cancellationToken);
        var variant = await decks.CreateAsync(
            ownerId,
            new NewDeck(name, main.Format, main.Visibility, null, string.Empty, null, null, main.Id, baseId),
            [.. lines.Select(ToInput)],
            cancellationToken);
        await CommitAsync(ownerId, variant.Id, $"Variant of {main.Name}, from version {baseVersion.Number}", cancellationToken);
        return (await decks.GetAsync(ownerId, variant.Id, cancellationToken)).Deck;
    }

    public async Task<IReadOnlyList<DeckRow>> ListVariantsAsync(Guid? viewerId, Guid deckId, CancellationToken cancellationToken)
    {
        var main = (await decks.GetAsync(viewerId, deckId, cancellationToken)).Deck;
        await using var connection = await store.OpenAsync(main.OwnerId, ContentAccess.Read, cancellationToken);
        return await DeckStore.ListVariantsAsync(connection, main.OwnerId, main.VariantOf ?? main.Id, cancellationToken);
    }

    /// <summary>
    /// Merges a variant into its main deck: the working list of the main deck becomes the list of the variant,
    /// and a version of the kind <c>merge</c> has two parents. The method commits the variant first when it changed.
    /// </summary>
    public async Task<VersionRow> MergeAsync(Guid ownerId, Guid variantId, string? message, CancellationToken cancellationToken)
    {
        var variant = (await decks.GetAsync(ownerId, variantId, cancellationToken)).Deck;
        if (variant.OwnerId != ownerId)
        {
            throw DeckException.NotFound();
        }

        if (variant.VariantOf is not { } mainId)
        {
            throw DeckException.Validation(new FieldError("deckId", FieldErrorCodes.NotAllowed, "Only a variant can merge into its main deck."));
        }

        var (_, changed, _) = await ListAsync(ownerId, variantId, cancellationToken);
        var variantHead = changed
            ? (await CommitAsync(ownerId, variantId, "Before the merge", cancellationToken)).Id
            : variant.HeadVersionId!.Value;
        var (_, lines, _) = await GetAsync(ownerId, variantId, variantHead, cancellationToken);
        return await ReplaceAndCommitAsync(ownerId, mainId, lines, "merge", message ?? $"Merge of the variant {variant.Name}", variantHead, cancellationToken);
    }

    /// <summary>
    /// Reverts a merge: the working list of the deck gets the content of the first parent of the merge, in a new version of the kind <c>revert</c>.
    /// </summary>
    public async Task<VersionRow> RevertAsync(Guid ownerId, Guid deckId, Guid versionId, string? message, CancellationToken cancellationToken)
    {
        var (version, _, _) = await GetAsync(ownerId, deckId, versionId, cancellationToken);
        if (version.Kind != "merge")
        {
            throw DeckException.Validation(new FieldError("versionId", FieldErrorCodes.NotAllowed, "Only a merge can be reverted. To go back to another version, restore it."));
        }

        IReadOnlyList<VersionLineRow> lines = [];
        if (version.ParentIds.Length > 0 && version.ParentIds[0] != version.ParentIds[^1])
        {
            (_, lines, _) = await GetAsync(ownerId, deckId, version.ParentIds[0], cancellationToken);
        }

        return await ReplaceAndCommitAsync(ownerId, deckId, lines, "revert", message ?? $"Revert of the merge of version {version.Number}", null, cancellationToken);
    }

    /// <summary>Converts a frozen line into the input of a deck line.</summary>
    internal static LineInput ToInput(VersionLineRow line) =>
        new(line.OracleId, line.PrintingId, line.Finish, line.Quantity, line.Zone, line.Note, line.Tags, line.GlobalTags, line.Categories);

    internal static ComparedLine ToCompared(VersionLineRow line) =>
        new(line.OracleId, line.Zone, line.PrintingId, line.Finish, line.Quantity, line.Note);

    /// <summary>
    /// Replaces the working list of a deck with frozen lines, and commits the result in the same transaction.
    /// </summary>
    private async Task<VersionRow> ReplaceAndCommitAsync(Guid ownerId, Guid deckId, IReadOnlyList<VersionLineRow> lines, string kind, string message, Guid? secondParent, CancellationToken cancellationToken)
    {
        VersionRow? version = null;
        await decks.ReplaceLinesAsync(ownerId, deckId, [.. lines.Select(ToInput)], null, cancellationToken, async (transaction, deck, written) =>
        {
            var frozen = await FreezeAsync(transaction.Connection!, written, cancellationToken);
            IReadOnlyList<Guid> parents = [.. new[] { deck.HeadVersionId, secondParent }.OfType<Guid>()];
            version = await InsertVersionAsync(transaction, deck, kind, message, parents, frozen, cancellationToken);
        });
        return version!;
    }

    private async Task<VersionRow> InsertVersionAsync(NpgsqlTransaction transaction, DeckRow deck, string kind, string message, IReadOnlyList<Guid> parents, IReadOnlyList<VersionLineRow> lines, CancellationToken cancellationToken)
    {
        var version = await VersionStore.InsertAsync(transaction, deck.OwnerId, deck.Id, kind, message.Trim(), parents, lines, Ids.New(), time.GetUtcNow().UtcDateTime, cancellationToken);
        deck.HeadVersionId = version.Id;
        await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckVersionCreated(deck.Id, version.Id, version.Number, kind), deck.OwnerId, cancellationToken);
        return version;
    }

    private static async Task<IReadOnlyList<VersionLineRow>> FreezeAsync(NpgsqlConnection connection, IReadOnlyList<DeckLineRow> lines, CancellationToken cancellationToken)
    {
        var tagIds = lines.SelectMany(line => line.TagIds).Distinct().ToList();
        var tags = tagIds.Count == 0 ? [] : await DeckStore.FindTagsAsync(connection, tagIds, cancellationToken);
        return Freeze(lines, tags);
    }

    private static IReadOnlyList<VersionLineRow> Freeze(IReadOnlyList<DeckLineRow> lines, IReadOnlyList<TagRow> tags)
    {
        var byId = tags.ToDictionary(tag => tag.Id);
        return [.. lines.Select(line => new VersionLineRow
        {
            OracleId = line.OracleId,
            PrintingId = line.PrintingId,
            Finish = line.Finish,
            Quantity = line.Quantity,
            Zone = line.Zone,
            Note = line.Note,
            Categories = [.. line.Categories.Order(StringComparer.Ordinal)],
            Tags = [.. line.TagIds.Where(byId.ContainsKey).Select(id => byId[id]).Where(tag => !tag.IsGlobal).Select(tag => tag.Name).Order(StringComparer.OrdinalIgnoreCase)],
            GlobalTags = [.. line.TagIds.Where(byId.ContainsKey).Select(id => byId[id]).Where(tag => tag.IsGlobal).Select(tag => tag.Name).Order(StringComparer.OrdinalIgnoreCase)],
        })];
    }

    private static bool Same(IReadOnlyList<VersionLineRow> first, IReadOnlyList<VersionLineRow> second) =>
        first.Select(line => line.Content).Order(StringComparer.Ordinal).SequenceEqual(second.Select(line => line.Content).Order(StringComparer.Ordinal));

    private static DeckException VersionNotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The version does not exist in this deck.");
}
