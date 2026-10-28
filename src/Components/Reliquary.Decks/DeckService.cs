using Microsoft.AspNetCore.Http;
using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Decks.Data;
using Reliquary.Decks.Moderation;

namespace Reliquary.Decks;

/// <summary>
/// An error of a deck operation, with the status and the stable code of the API response.
/// </summary>
internal sealed class DeckException(int status, string code, string message, IReadOnlyList<FieldError>? errors = null) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    public IReadOnlyList<FieldError> Errors { get; } = errors ?? [];

    public static DeckException NotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The deck does not exist, or you cannot see it.");

    public static DeckException Validation(params FieldError[] errors) =>
        new(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "One or more fields of the request are not valid. Correct the fields in the errors list.", errors);
}

/// <summary>
/// The fields of a new deck.
/// </summary>
internal sealed record NewDeck(string Name, string Format, string Visibility, Guid? FolderId, string Description, Guid? SourceDeckId, IReadOnlyList<string>? Tags = null, Guid? VariantOf = null, Guid? BaseVersionId = null);

/// <summary>
/// The changes of a deck. A <see langword="null"/> field does not change.
/// </summary>
internal sealed record DeckChanges(string? Name, string? Format, string? Visibility, Guid? FolderId, bool ClearFolder, string? Description, bool? Pinned, bool? Archived, int? ExpectedVersion, IReadOnlyList<string>? Tags = null);

/// <summary>
/// A group action on several decks of a user.
/// </summary>
/// <param name="Action">The action: <c>move</c>, <c>tag</c>, <c>untag</c>, <c>pin</c>, <c>unpin</c>, <c>archive</c> or <c>unarchive</c>.</param>
/// <param name="FolderId">The folder of the action <c>move</c>. <see langword="null"/> removes the decks from their folder.</param>
/// <param name="Tag">The deck tag of the actions <c>tag</c> and <c>untag</c>.</param>
internal sealed record DeckGroupAction(string Action, Guid? FolderId, string? Tag);

/// <summary>
/// The wanted content of a deck line.
/// </summary>
internal sealed record LineInput(
    Guid OracleId,
    Guid? PrintingId,
    string? Finish,
    int Quantity,
    string Zone,
    string Note,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> GlobalTags,
    IReadOnlyList<string> Categories,
    string? PurchasePreferences = null)
{
    public LineInput(Guid oracleId, Guid? printingId, string? finish, int quantity, string zone, string note)
        : this(oracleId, printingId, finish, quantity, zone, note, [], [], [])
    {
    }
}

/// <summary>
/// The changes of a deck line. A <see langword="null"/> field does not change. A list replaces the current list.
/// </summary>
internal sealed record LineChanges(
    int? Quantity,
    string? Zone,
    Guid? PrintingId,
    bool ClearPrinting,
    string? Finish,
    bool ClearFinish,
    string? Note,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? GlobalTags = null,
    IReadOnlyList<string>? Categories = null,
    Purchases.PurchasePreferences? PurchasePreferences = null,
    bool ClearPurchasePreferences = false);

/// <summary>
/// A group action on several lines of one deck.
/// </summary>
/// <param name="Action">The action: <c>move</c>, <c>tag</c>, <c>untag</c>, <c>printing</c>, <c>quantity</c> or <c>remove</c>.</param>
/// <param name="Zone">The zone of the action <c>move</c>.</param>
/// <param name="Tags">The local tags of the actions <c>tag</c> and <c>untag</c>.</param>
/// <param name="GlobalTags">The global tags of the actions <c>tag</c> and <c>untag</c>.</param>
/// <param name="Categories">The categories of the actions <c>tag</c> and <c>untag</c>.</param>
/// <param name="QuantityDelta">The number of cards to add to each line in the action <c>quantity</c>. A negative number removes cards.</param>
/// <param name="Finish">The finish of the action <c>printing</c>, or <see langword="null"/> to keep the finish.</param>
/// <param name="ClearPrinting">For the action <c>printing</c>: <see langword="true"/> removes the printing, so the default printing applies.</param>
internal sealed record LineGroupAction(
    string Action,
    string? Zone,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> GlobalTags,
    IReadOnlyList<string> Categories,
    int QuantityDelta,
    string? Finish,
    bool ClearPrinting);

/// <summary>
/// A deck and its lines, as a user can see them, with the tags of the lines.
/// <see cref="Hidden"/> tells whether an administrator hid the deck. Only the owner sees a hidden deck.
/// </summary>
internal sealed record DeckView(DeckRow Deck, IReadOnlyList<DeckLineRow> Lines, bool IsOwner, IReadOnlyList<TagRow> Tags, bool Hidden = false);

/// <summary>
/// The operations on the decks.
/// </summary>
internal sealed class DeckService(DeckStore store, ICardCatalog catalog, IDomainEventWriter events, TimeProvider time)
{
    public async Task<DeckRow> CreateAsync(Guid ownerId, NewDeck input, IReadOnlyList<LineInput> lines, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var deck = new DeckRow
        {
            Id = Ids.New(),
            OwnerId = ownerId,
            Name = input.Name.Trim(),
            Format = input.Format,
            Visibility = input.Visibility,
            FolderId = input.FolderId,
            Description = input.Description,
            SourceDeckId = input.SourceDeckId,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Tags = [.. input.Tags ?? []],
            VariantOf = input.VariantOf,
            BaseVersionId = input.BaseVersionId,
        };
        await ValidateLinesAsync(lines, cancellationToken);
        deck.ColorIdentity = await ColorIdentityAsync(lines.Select(line => (line.Zone, line.OracleId)), cancellationToken);

        // The directory comes first: a directory entry with no deck has no effect, but a deck with no entry cannot be shared.
        await store.AddToDirectoryAsync(deck.Id, ownerId, cancellationToken);
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (deck.FolderId is { } folderId && !await DeckStore.FolderExistsAsync(connection, ownerId, folderId, cancellationToken, transaction))
        {
            throw DeckException.Validation(new FieldError("folderId", FieldErrorCodes.NotAllowed, "The folder does not exist."));
        }

        await DeckStore.InsertDeckAsync(transaction, deck, cancellationToken);
        var tags = await ResolveTagsAsync(connection, transaction, ownerId, deck.Id, lines, cancellationToken);
        var rows = new List<DeckLineRow>();
        Merge(rows, deck.Id, lines, tags, now);
        await WriteLinesAsync(transaction, ownerId, deck.Id, [], rows, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckCreated(deck.Id, deck.Format, deck.Visibility, deck.SourceDeckId, deck.VariantOf), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deck;
    }

    /// <summary>
    /// Gets a deck, when the user can see it: the owner sees each deck, and the other users see the unlisted and public decks.
    /// The other users do not see a deck that an administrator hid.
    /// </summary>
    public async Task<DeckView> GetAsync(Guid? viewerId, Guid deckId, CancellationToken cancellationToken)
    {
        var ownerId = await store.FindOwnerAsync(deckId, cancellationToken) ?? throw DeckException.NotFound();
        var hidden = await IsHiddenAsync(deckId, cancellationToken);
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        var deck = await DeckStore.FindDeckAsync(connection, ownerId, deckId, cancellationToken) ?? throw DeckException.NotFound();
        var isOwner = viewerId == ownerId;
        if (!isOwner && (deck.Visibility == "private" || hidden))
        {
            throw DeckException.NotFound();
        }

        var lines = await DeckStore.ListLinesAsync(connection, deckId, cancellationToken);
        return new DeckView(deck, lines, isOwner, await TagsOfLinesAsync(connection, lines, cancellationToken), hidden);
    }

    /// <summary>
    /// Tells whether an administrator hid a deck.
    /// </summary>
    private async Task<bool> IsHiddenAsync(Guid deckId, CancellationToken cancellationToken)
    {
        await using var users = await store.OpenUsersAsync(cancellationToken);
        return await ModerationStore.IsHiddenAsync(users, ModerationValues.Deck, deckId, cancellationToken);
    }

    /// <summary>
    /// Copies a deck that the user can see into a new deck of the user.
    /// The copy of the deck of another user keeps a link to the original deck, and its tags become local tags.
    /// </summary>
    public async Task<DeckRow> CopyAsync(Guid userId, Guid deckId, string? name, string visibility, Guid? folderId, CancellationToken cancellationToken)
    {
        var source = await GetAsync(userId, deckId, cancellationToken);
        var tags = source.Tags.ToDictionary(tag => tag.Id);
        var lines = source.Lines.Select(line =>
        {
            var lineTags = line.TagIds.Where(tags.ContainsKey).Select(id => tags[id]).ToList();
            var local = source.IsOwner ? lineTags.Where(tag => !tag.IsGlobal) : lineTags;
            var global = source.IsOwner ? lineTags.Where(tag => tag.IsGlobal) : [];
            return new LineInput(
                line.OracleId,
                line.PrintingId,
                line.Finish,
                line.Quantity,
                line.Zone,
                line.Note,
                [.. local.Select(tag => tag.Name).Distinct(StringComparer.OrdinalIgnoreCase)],
                [.. global.Select(tag => tag.Name)],
                line.Categories,
                line.PurchasePreferences);
        }).ToList();
        var deck = source.Deck;
        return await CreateAsync(
            userId,
            new NewDeck(name ?? deck.Name, deck.Format, visibility, folderId, deck.Description, source.IsOwner ? null : deck.Id),
            lines,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DeckSummaryRow>> ListAsync(Guid ownerId, DeckListQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await DeckStore.ListDecksAsync(connection, ownerId, query, cancellationToken);
    }

    public async Task<IReadOnlyList<(string Name, int DeckCount)>> ListDeckTagsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await DeckStore.ListDeckTagsAsync(connection, ownerId, cancellationToken);
    }

    /// <summary>
    /// Applies one action to several decks of the user in one transaction. Each deck gets a new version.
    /// </summary>
    /// <returns>The number of changed decks.</returns>
    /// <exception cref="DeckException">A deck does not exist, or the folder does not exist.</exception>
    public async Task<int> ApplyGroupActionAsync(Guid ownerId, IReadOnlyCollection<Guid> deckIds, DeckGroupAction action, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var decks = await DeckStore.FindDecksForUpdateAsync(transaction, ownerId, deckIds, cancellationToken);
        if (decks.Count != deckIds.Count)
        {
            throw DeckException.NotFound();
        }

        if (action is { Action: "move", FolderId: { } folderId } && !await DeckStore.FolderExistsAsync(connection, ownerId, folderId, cancellationToken, transaction))
        {
            throw DeckException.Validation(new FieldError("folderId", FieldErrorCodes.NotAllowed, "The folder does not exist."));
        }

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var deck in decks)
        {
            switch (action.Action)
            {
                case "move":
                    deck.FolderId = action.FolderId;
                    break;
                case "tag":
                    deck.Tags = DeckRules.NormalizeDeckTags([.. deck.Tags, action.Tag!]);
                    break;
                case "untag":
                    deck.Tags = [.. deck.Tags.Where(tag => !string.Equals(tag, action.Tag, StringComparison.OrdinalIgnoreCase))];
                    break;
                case "pin" or "unpin":
                    deck.Pinned = action.Action == "pin";
                    break;
                case "archive":
                    deck.ArchivedAt ??= now;
                    break;
                case "unarchive":
                    deck.ArchivedAt = null;
                    break;
                default:
                    throw new ArgumentException($"The action {action.Action} is not known.", nameof(action));
            }

            if (action.Action == "tag" && deck.Tags.Length > DeckRules.MaxDeckTags)
            {
                throw DeckException.Validation(new FieldError("tag", FieldErrorCodes.TooLong, $"A deck can have {DeckRules.MaxDeckTags} deck tags or fewer."));
            }

            deck.Version++;
            deck.UpdatedAt = now;
            await DeckStore.UpdateDeckAsync(transaction, deck, cancellationToken);
            await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckUpdated(deck.Id, deck.Version, deck.Format, deck.Visibility, deck.VariantOf), ownerId, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return decks.Count;
    }

    public Task<DeckView> UpdateAsync(Guid ownerId, Guid deckId, DeckChanges changes, CancellationToken cancellationToken) =>
        MutateAsync(ownerId, deckId, changes.ExpectedVersion, [], async (connection, transaction, deck, _, _) =>
        {
            if (changes.FolderId is { } folderId && !await DeckStore.FolderExistsAsync(connection, ownerId, folderId, cancellationToken, transaction))
            {
                throw DeckException.Validation(new FieldError("folderId", FieldErrorCodes.NotAllowed, "The folder does not exist."));
            }

            if (deck.VariantOf is not null && (changes.Format is not null || changes.Visibility is not null || changes.FolderId is not null || changes.ClearFolder || changes.Pinned is not null || changes.Archived is not null))
            {
                throw DeckException.Validation(new FieldError("variantOf", FieldErrorCodes.NotAllowed, "A variant has the format, the visibility and the folder of its main deck. Change the main deck."));
            }

            if (deck.VariantOf is null && (changes.Format is not null || changes.Visibility is not null))
            {
                await DeckStore.UpdateVariantsAsync(transaction, ownerId, deck.Id, changes.Format ?? deck.Format, changes.Visibility ?? deck.Visibility, cancellationToken);
            }

            deck.Name = changes.Name?.Trim() ?? deck.Name;
            deck.Format = changes.Format ?? deck.Format;
            deck.Visibility = changes.Visibility ?? deck.Visibility;
            deck.FolderId = changes.ClearFolder ? null : changes.FolderId ?? deck.FolderId;
            deck.Description = changes.Description ?? deck.Description;
            deck.Pinned = changes.Pinned ?? deck.Pinned;
            if (changes.Tags is { } deckTags)
            {
                deck.Tags = DeckRules.NormalizeDeckTags(deckTags);
            }

            if (changes.Archived is { } archived)
            {
                deck.ArchivedAt = archived ? deck.ArchivedAt ?? time.GetUtcNow().UtcDateTime : null;
            }
        }, cancellationToken);

    public async Task DeleteAsync(Guid ownerId, Guid deckId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> variantIds;
        await using (var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var deck = await DeckStore.FindDeckAsync(connection, ownerId, deckId, cancellationToken, transaction, forUpdate: true) ?? throw DeckException.NotFound();
            if (deck.AssembledVersionId is not null)
            {
                throw Versions.AssembledDeckErrorCodes.DeletionRefused();
            }

            variantIds = await DeckStore.ListVariantIdsAsync(connection, ownerId, deckId, cancellationToken, transaction);
            await DeckStore.DeleteDeckAsync(transaction, ownerId, deckId, cancellationToken);
            foreach (var id in variantIds.Append(deckId))
            {
                await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckDeleted(id), ownerId, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var id in variantIds.Append(deckId))
        {
            await store.RemoveFromDirectoryAsync(id, cancellationToken);
        }
    }

    /// <summary>
    /// Adds cards to a deck. A line with the same card, printing and finish in the same zone gets the new quantity added.
    /// </summary>
    public async Task<DeckView> AddLinesAsync(Guid ownerId, Guid deckId, IReadOnlyList<LineInput> lines, CancellationToken cancellationToken)
    {
        await ValidateLinesAsync(lines, cancellationToken);
        return await MutateAsync(ownerId, deckId, null, lines, (_, _, deck, current, tags) =>
        {
            Merge(current, deck.Id, lines, tags, time.GetUtcNow().UtcDateTime);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>
    /// Replaces all the lines of a deck. The lines that do not change keep their IDs.
    /// </summary>
    public async Task<DeckView> ReplaceLinesAsync(
        Guid ownerId,
        Guid deckId,
        IReadOnlyList<LineInput> lines,
        int? expectedVersion,
        CancellationToken cancellationToken,
        Func<NpgsqlTransaction, DeckRow, IReadOnlyList<DeckLineRow>, Task>? afterWrite = null)
    {
        await ValidateLinesAsync(lines, cancellationToken);
        return await MutateAsync(ownerId, deckId, expectedVersion, lines, (_, _, deck, current, tags) =>
        {
            var wanted = new List<DeckLineRow>();
            var now = time.GetUtcNow().UtcDateTime;
            Merge(wanted, deck.Id, lines, tags, now);
            var existing = current.ToDictionary(line => line.Key);
            current.Clear();
            foreach (var line in wanted)
            {
                if (existing.TryGetValue(line.Key, out var kept))
                {
                    kept.Quantity = line.Quantity;
                    kept.Note = line.Note;
                    kept.TagIds = line.TagIds;
                    kept.Categories = line.Categories;
                    kept.PurchasePreferences = line.PurchasePreferences;
                    current.Add(kept);
                }
                else
                {
                    current.Add(line);
                }
            }

            return Task.CompletedTask;
        }, cancellationToken, afterWrite);
    }

    public async Task<DeckView> UpdateLineAsync(Guid ownerId, Guid deckId, Guid lineId, LineChanges changes, CancellationToken cancellationToken)
    {
        Guid? newPrintingOracle = null;
        if (changes.PrintingId is { } printingId)
        {
            var printings = await catalog.GetPrintingsAsync([printingId], cancellationToken);
            if (!printings.TryGetValue(printingId, out var printing))
            {
                throw DeckException.Validation(new FieldError("printingId", FieldErrorCodes.NotAllowed, "The printing does not exist."));
            }

            newPrintingOracle = printing.OracleId;
        }

        var tagInput = new LineInput(Guid.Empty, null, null, 1, "main", string.Empty, changes.Tags ?? [], changes.GlobalTags ?? [], []);
        return await MutateAsync(ownerId, deckId, null, [tagInput], async (_, transaction, _, current, tags) =>
        {
            var line = current.FirstOrDefault(candidate => candidate.Id == lineId)
                ?? throw new DeckException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The deck line does not exist.");
            if (changes.Quantity == 0)
            {
                current.Remove(line);
                return;
            }

            if (newPrintingOracle is { } oracle && oracle != line.OracleId)
            {
                throw DeckException.Validation(new FieldError("printingId", FieldErrorCodes.NotAllowed, "The printing is not a printing of the card of the line."));
            }

            line.Quantity = changes.Quantity ?? line.Quantity;
            line.Zone = changes.Zone ?? line.Zone;
            line.PrintingId = changes.ClearPrinting ? null : changes.PrintingId ?? line.PrintingId;
            line.Finish = changes.ClearFinish ? null : changes.Finish ?? line.Finish;
            line.Note = changes.Note ?? line.Note;
            if (changes.ClearPurchasePreferences)
            {
                line.PurchasePreferences = null;
            }
            else if (changes.PurchasePreferences is { } preferences)
            {
                line.PurchasePreferences = Purchases.PurchasePreferences.ToJson(preferences);
            }

            if (changes.Tags is not null || changes.GlobalTags is not null)
            {
                var kept = line.TagIds.Where(id => changes.Tags is null ? tags.IsLocal(id) : changes.GlobalTags is null && !tags.IsLocal(id)).ToList();
                line.TagIds = [.. kept.Concat(tags.IdsOf(tagInput)).Distinct()];
            }

            if (changes.Categories is { } categories && !categories.ToHashSet().SetEquals(line.Categories))
            {
                // A change of the categories becomes a rule of the user for the next suggestions of auto-tag. See ADR 0031.
                line.Categories = [.. categories.Distinct()];
                await CategoryRuleStore.UpsertAsync(transaction, ownerId, line.OracleId, line.Categories, time.GetUtcNow().UtcDateTime, cancellationToken);
            }

            line.UpdatedAt = time.GetUtcNow().UtcDateTime;

            // A change can give the key of another line, for example a move to another zone. The two lines become one line.
            var same = current.FirstOrDefault(other => other.Id != line.Id && other.Key == line.Key);
            if (same is not null)
            {
                same.Quantity = Math.Min(DeckRules.MaxQuantity, same.Quantity + line.Quantity);
                same.TagIds = [.. same.TagIds.Union(line.TagIds)];
                same.Categories = [.. same.Categories.Union(line.Categories)];
                same.UpdatedAt = line.UpdatedAt;
                current.Remove(line);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Applies one action to several lines of a deck in one transaction. The deck gets one new version and one event.
    /// Lines that get the same key become one line, with the sum of their quantities.
    /// </summary>
    /// <exception cref="DeckException">The deck or a line does not exist, or a line gets too many tags.</exception>
    public Task<DeckView> ApplyLineGroupActionAsync(Guid ownerId, Guid deckId, IReadOnlyCollection<Guid> lineIds, LineGroupAction action, int? expectedVersion, CancellationToken cancellationToken)
    {
        var tagInput = new LineInput(Guid.Empty, null, null, 1, "main", string.Empty, action.Tags, action.GlobalTags, action.Categories);
        IReadOnlyList<LineInput> inputs = action.Action == "tag" ? [tagInput] : [];
        return MutateAsync(ownerId, deckId, expectedVersion, inputs, (_, _, _, current, tags) =>
        {
            var selected = current.Where(line => lineIds.Contains(line.Id)).ToList();
            if (selected.Count != lineIds.Count)
            {
                throw new DeckException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "One or more deck lines do not exist.");
            }

            var now = time.GetUtcNow().UtcDateTime;
            HashSet<Guid> removedTagIds = action.Action == "untag"
                ? action.Tags.Select(name => tags.Find(name, false)?.Id).Concat(action.GlobalTags.Select(name => tags.Find(name, true)?.Id)).OfType<Guid>().ToHashSet()
                : [];
            foreach (var line in selected)
            {
                switch (action.Action)
                {
                    case "move":
                        line.Zone = action.Zone!;
                        break;
                    case "tag":
                        line.TagIds = [.. line.TagIds.Union(tags.IdsOf(tagInput))];
                        line.Categories = [.. line.Categories.Union(action.Categories)];
                        break;
                    case "untag":
                        line.TagIds = [.. line.TagIds.Where(id => !removedTagIds.Contains(id))];
                        line.Categories = [.. line.Categories.Except(action.Categories)];
                        break;
                    case "printing":
                        line.PrintingId = action.ClearPrinting ? null : line.PrintingId;
                        line.Finish = action.Finish ?? line.Finish;
                        break;
                    case "quantity":
                        line.Quantity = Math.Min(DeckRules.MaxQuantity, line.Quantity + action.QuantityDelta);
                        break;
                    case "remove":
                        line.Quantity = 0;
                        break;
                    default:
                        throw new ArgumentException($"The action {action.Action} is not known.", nameof(action));
                }

                line.UpdatedAt = now;
            }

            current.RemoveAll(line => line.Quantity <= 0);
            MergeSameKeys(current, lineIds, now);
            if (current.Any(line => line.TagIds.Length > DeckRules.MaxTagsPerLine || line.Categories.Length > DeckRules.MaxTagsPerLine))
            {
                throw DeckException.Validation(new FieldError("tags", FieldErrorCodes.TooLong, $"A line can have {DeckRules.MaxTagsPerLine} tags and {DeckRules.MaxTagsPerLine} categories or fewer."));
            }

            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task<DeckView> DeleteLineAsync(Guid ownerId, Guid deckId, Guid lineId, CancellationToken cancellationToken) =>
        MutateAsync(ownerId, deckId, null, [], (_, _, _, current, _) =>
        {
            if (current.RemoveAll(line => line.Id == lineId) == 0)
            {
                throw new DeckException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The deck line does not exist.");
            }

            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>
    /// Loads a deck of the owner with a lock, applies a change to the deck and to its lines, and writes the differences,
    /// the new version and the event in one transaction.
    /// </summary>
    /// <param name="ownerId">The owner of the deck.</param>
    /// <param name="deckId">The deck.</param>
    /// <param name="expectedVersion">The version that the client read, or <see langword="null"/>.</param>
    /// <param name="inputs">The lines of the request. The method finds or creates their tags before the change.</param>
    /// <param name="change">The change.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    internal async Task<DeckView> MutateAsync(
        Guid ownerId,
        Guid deckId,
        int? expectedVersion,
        IReadOnlyList<LineInput> inputs,
        Func<NpgsqlConnection, NpgsqlTransaction, DeckRow, List<DeckLineRow>, TagMap, Task> change,
        CancellationToken cancellationToken,
        Func<NpgsqlTransaction, DeckRow, IReadOnlyList<DeckLineRow>, Task>? afterWrite = null)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var deck = await DeckStore.FindDeckAsync(connection, ownerId, deckId, cancellationToken, transaction, forUpdate: true) ?? throw DeckException.NotFound();
        if (expectedVersion is { } expected && expected != deck.Version)
        {
            throw new DeckException(
                StatusCodes.Status409Conflict,
                DeckErrorCodes.VersionConflict,
                $"The deck changed since you read it. Its version is {deck.Version}, not {expected}. Read the deck again, then repeat the change.");
        }

        var before = await DeckStore.ListLinesAsync(connection, deckId, cancellationToken, transaction);
        var lines = (await DeckStore.ListLinesAsync(connection, deckId, cancellationToken, transaction)).ToList();
        var tags = await ResolveTagsAsync(connection, transaction, ownerId, deckId, inputs, cancellationToken);
        await change(connection, transaction, deck, lines, tags);

        if (lines.Count > DeckRules.MaxLines)
        {
            throw DeckException.Validation(new FieldError("lines", FieldErrorCodes.TooLong, $"A deck can have {DeckRules.MaxLines} lines or fewer."));
        }

        await WriteLinesAsync(transaction, ownerId, deckId, before, lines, cancellationToken);
        deck.ColorIdentity = await ColorIdentityAsync(lines.Select(line => (line.Zone, line.OracleId)), cancellationToken);
        deck.Version++;
        deck.UpdatedAt = time.GetUtcNow().UtcDateTime;
        if (afterWrite is not null)
        {
            await afterWrite(transaction, deck, lines);
        }

        await DeckStore.UpdateDeckAsync(transaction, deck, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckUpdated(deck.Id, deck.Version, deck.Format, deck.Visibility, deck.VariantOf), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DeckView(deck, lines, true, await TagsOfLinesAsync(connection, lines, cancellationToken), await IsHiddenAsync(deckId, cancellationToken));
    }

    /// <summary>
    /// Gets the color identity of a deck: the identity of the commanders, or of the main deck and the companions when the deck has no commander.
    /// </summary>
    private async Task<string> ColorIdentityAsync(IEnumerable<(string Zone, Guid OracleId)> lines, CancellationToken cancellationToken)
    {
        var list = lines.ToList();
        var commanders = list.Where(line => line.Zone == "commander").Select(line => line.OracleId).ToList();
        var oracleIds = (commanders.Count > 0 ? commanders : list.Where(line => line.Zone is "main" or "companion").Select(line => line.OracleId)).Distinct().ToList();
        if (oracleIds.Count == 0)
        {
            return string.Empty;
        }

        var cards = await catalog.GetCardsAsync(oracleIds, cancellationToken);
        var colors = cards.Values.SelectMany(card => card.ColorIdentity).ToHashSet(StringComparer.Ordinal);
        return string.Concat(DeckRules.ColorOrder.Where(color => colors.Contains(color.ToString())));
    }

    /// <summary>
    /// Writes the differences between two lists of lines, with the tags of the lines.
    /// </summary>
    private static async Task WriteLinesAsync(NpgsqlTransaction transaction, Guid ownerId, Guid deckId, IReadOnlyList<DeckLineRow> before, IReadOnlyList<DeckLineRow> after, CancellationToken cancellationToken)
    {
        var old = before.ToDictionary(line => line.Id, line => line.Content);
        var removed = old.Keys.Except(after.Select(line => line.Id)).ToList();
        var added = after.Where(line => !old.ContainsKey(line.Id)).ToList();
        var updated = after.Where(line => old.TryGetValue(line.Id, out var content) && content != line.Content).ToList();

        // The unique key of the lines applies at each statement. Two changed lines can exchange their keys,
        // so the changed lines go out with the removed lines, then come back with the new lines. They keep their IDs.
        await DeckStore.DeleteLinesAsync(transaction, deckId, removed.Concat(updated.Select(line => line.Id)), cancellationToken);
        await DeckStore.InsertLinesAsync(transaction, ownerId, updated.Concat(added), cancellationToken);
        await DeckStore.InsertLineTagsAsync(transaction, updated.Concat(added), cancellationToken);
    }

    /// <summary>
    /// Finds the tags of the inputs by name, and creates the tags that do not exist.
    /// </summary>
    private async Task<TagMap> ResolveTagsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid ownerId, Guid deckId, IReadOnlyList<LineInput> inputs, CancellationToken cancellationToken)
    {
        var existing = await DeckStore.ListTagsAsync(connection, ownerId, deckId, cancellationToken, transaction);
        var map = new TagMap(existing);
        var missing = new List<TagRow>();
        foreach (var (name, global) in inputs.SelectMany(input => input.Tags.Select(tag => (tag, false)).Concat(input.GlobalTags.Select(tag => (tag, true)))))
        {
            if (map.Find(name, global) is null)
            {
                var tag = new TagRow { Id = Ids.New(), DeckId = global ? null : deckId, Name = name.Trim() };
                missing.Add(tag);
                map.Add(tag);
            }
        }

        await DeckStore.InsertTagsAsync(transaction, ownerId, missing, time.GetUtcNow().UtcDateTime, cancellationToken);
        return map;
    }

    private static async Task<IReadOnlyList<TagRow>> TagsOfLinesAsync(NpgsqlConnection connection, IReadOnlyList<DeckLineRow> lines, CancellationToken cancellationToken)
    {
        var ids = lines.SelectMany(line => line.TagIds).Distinct().ToList();
        return ids.Count == 0 ? [] : await DeckStore.FindTagsAsync(connection, ids, cancellationToken);
    }

    /// <summary>
    /// Adds the wanted lines to a list. A line with the key of an existing line adds its quantity, its tags and its categories to that line.
    /// </summary>
    private static void Merge(List<DeckLineRow> lines, Guid deckId, IEnumerable<LineInput> inputs, TagMap tags, DateTime now)
    {
        foreach (var input in inputs)
        {
            var key = (input.Zone, input.OracleId, input.PrintingId, input.Finish);
            var tagIds = tags.IdsOf(input).ToArray();
            var existing = lines.FirstOrDefault(line => line.Key == key);
            if (existing is not null)
            {
                existing.Quantity = Math.Min(DeckRules.MaxQuantity, existing.Quantity + input.Quantity);
                existing.Note = input.Note.Length > 0 ? input.Note : existing.Note;
                existing.TagIds = [.. existing.TagIds.Union(tagIds)];
                existing.Categories = [.. existing.Categories.Union(input.Categories)];
                existing.PurchasePreferences = input.PurchasePreferences ?? existing.PurchasePreferences;
                existing.UpdatedAt = now;
                continue;
            }

            lines.Add(new DeckLineRow
            {
                Id = Ids.New(),
                DeckId = deckId,
                OracleId = input.OracleId,
                PrintingId = input.PrintingId,
                Finish = input.Finish,
                Quantity = input.Quantity,
                Zone = input.Zone,
                Note = input.Note,
                TagIds = tagIds,
                Categories = [.. input.Categories.Distinct()],
                PurchasePreferences = input.PurchasePreferences,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }

    /// <summary>
    /// Joins the lines that have the same key. The line that the action did not change keeps its ID.
    /// It gets the sum of the quantities, the tags, the categories and the first note that is not empty.
    /// </summary>
    private static void MergeSameKeys(List<DeckLineRow> lines, IReadOnlyCollection<Guid> changedIds, DateTime now)
    {
        var kept = new Dictionary<(string Zone, Guid OracleId, Guid? PrintingId, string? Finish), DeckLineRow>();
        foreach (var line in lines.OrderBy(line => changedIds.Contains(line.Id) ? 1 : 0).ToList())
        {
            if (!kept.TryGetValue(line.Key, out var same))
            {
                kept[line.Key] = line;
                continue;
            }

            same.Quantity = Math.Min(DeckRules.MaxQuantity, same.Quantity + line.Quantity);
            same.TagIds = [.. same.TagIds.Union(line.TagIds)];
            same.Categories = [.. same.Categories.Union(line.Categories)];
            same.Note = same.Note.Length > 0 ? same.Note : line.Note;
            same.UpdatedAt = now;
            lines.Remove(line);
        }
    }

    /// <summary>
    /// Checks that the cards and the printings of the lines exist, and that each printing is a printing of its card.
    /// </summary>
    private async Task ValidateLinesAsync(IReadOnlyList<LineInput> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var cards = await catalog.GetCardsAsync([.. lines.Select(line => line.OracleId)], cancellationToken);
        var printings = await catalog.GetPrintingsAsync([.. lines.Where(line => line.PrintingId is not null).Select(line => line.PrintingId!.Value)], cancellationToken);
        var errors = new List<FieldError>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!cards.ContainsKey(line.OracleId))
            {
                errors.Add(new FieldError($"lines[{index}].oracleId", FieldErrorCodes.NotAllowed, "The card does not exist."));
            }

            if (line.PrintingId is { } printingId && (!printings.TryGetValue(printingId, out var printing) || printing.OracleId != line.OracleId))
            {
                errors.Add(new FieldError($"lines[{index}].printingId", FieldErrorCodes.NotAllowed, "The printing does not exist, or it is not a printing of the card."));
            }
        }

        if (errors.Count > 0)
        {
            throw DeckException.Validation([.. errors]);
        }
    }
}

/// <summary>
/// The tags of an owner and of one deck, by name. The names are case-insensitive.
/// </summary>
internal sealed class TagMap
{
    private readonly Dictionary<string, TagRow> local = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TagRow> global = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> localIds = [];

    public TagMap(IEnumerable<TagRow> tags)
    {
        foreach (var tag in tags)
        {
            Add(tag);
        }
    }

    public void Add(TagRow tag)
    {
        (tag.IsGlobal ? global : local)[tag.Name] = tag;
        if (!tag.IsGlobal)
        {
            localIds.Add(tag.Id);
        }
    }

    public TagRow? Find(string name, bool isGlobal) =>
        (isGlobal ? global : local).GetValueOrDefault(name.Trim());

    public bool IsLocal(Guid tagId) => localIds.Contains(tagId);

    public IEnumerable<Guid> IdsOf(LineInput input) =>
        input.Tags.Select(name => Find(name, false)!.Id).Concat(input.GlobalTags.Select(name => Find(name, true)!.Id)).Distinct();
}

/// <summary>
/// The stable error codes of the Decks endpoints.
/// </summary>
public static class DeckErrorCodes
{
    /// <summary>The deck changed since the client read it.</summary>
    public const string VersionConflict = "deck_version_conflict";

    /// <summary>The deck text has lines that the API cannot use. The member <c>lineErrors</c> gives each line.</summary>
    public const string TextInvalid = "deck_text_invalid";

    /// <summary>The working list did not change since the last version.</summary>
    public const string NoChanges = "deck_no_changes";
}
