using Microsoft.AspNetCore.Http;
using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Decks.Data;

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
internal sealed record NewDeck(string Name, string Format, string Visibility, Guid? FolderId, string Description, Guid? SourceDeckId);

/// <summary>
/// The changes of a deck. A <see langword="null"/> field does not change.
/// </summary>
internal sealed record DeckChanges(string? Name, string? Format, string? Visibility, Guid? FolderId, bool ClearFolder, string? Description, bool? Pinned, bool? Archived, int? ExpectedVersion);

/// <summary>
/// The wanted content of a deck line.
/// </summary>
internal sealed record LineInput(Guid OracleId, Guid? PrintingId, string? Finish, int Quantity, string Zone, string Note);

/// <summary>
/// The changes of a deck line. A <see langword="null"/> field does not change.
/// </summary>
internal sealed record LineChanges(int? Quantity, string? Zone, Guid? PrintingId, bool ClearPrinting, string? Finish, bool ClearFinish, string? Note);

/// <summary>
/// A deck and its lines, as a user can see them.
/// </summary>
internal sealed record DeckView(DeckRow Deck, IReadOnlyList<DeckLineRow> Lines, bool IsOwner);

/// <summary>
/// The operations on the decks.
/// </summary>
internal sealed class DeckService(DeckStore store, ICardCatalog catalog, IDomainEventWriter events, TimeProvider time)
{
    public async Task<DeckRow> CreateAsync(Guid ownerId, NewDeck input, IReadOnlyList<LineInput> lines, CancellationToken cancellationToken)
    {
        var lineRows = new List<DeckLineRow>();
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
        };
        await ValidateLinesAsync(lines, cancellationToken);
        Merge(lineRows, deck.Id, lines, now);

        // The directory comes first: a directory entry with no deck has no effect, but a deck with no entry cannot be shared.
        await store.AddToDirectoryAsync(deck.Id, ownerId, cancellationToken);
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (deck.FolderId is { } folderId && !await DeckStore.FolderExistsAsync(connection, ownerId, folderId, cancellationToken, transaction))
        {
            throw DeckException.Validation(new FieldError("folderId", FieldErrorCodes.NotAllowed, "The folder does not exist."));
        }

        await DeckStore.InsertDeckAsync(transaction, deck, cancellationToken);
        await DeckStore.InsertLinesAsync(transaction, ownerId, lineRows, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckCreated(deck.Id, deck.Format, deck.Visibility, deck.SourceDeckId), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deck;
    }

    /// <summary>
    /// Gets a deck, when the user can see it: the owner sees each deck, and the other users see the unlisted and public decks.
    /// </summary>
    public async Task<DeckView> GetAsync(Guid? viewerId, Guid deckId, CancellationToken cancellationToken)
    {
        var ownerId = await store.FindOwnerAsync(deckId, cancellationToken) ?? throw DeckException.NotFound();
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        var deck = await DeckStore.FindDeckAsync(connection, ownerId, deckId, cancellationToken) ?? throw DeckException.NotFound();
        var isOwner = viewerId == ownerId;
        if (!isOwner && deck.Visibility == "private")
        {
            throw DeckException.NotFound();
        }

        return new DeckView(deck, await DeckStore.ListLinesAsync(connection, deckId, cancellationToken), isOwner);
    }

    public async Task<IReadOnlyList<DeckSummaryRow>> ListAsync(Guid ownerId, (DateTime UpdatedAt, Guid Id)? after, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await DeckStore.ListDecksAsync(connection, ownerId, after, limit, cancellationToken);
    }

    public Task<DeckView> UpdateAsync(Guid ownerId, Guid deckId, DeckChanges changes, CancellationToken cancellationToken) =>
        MutateAsync(ownerId, deckId, changes.ExpectedVersion, async (connection, transaction, deck, _) =>
        {
            if (changes.FolderId is { } folderId && !await DeckStore.FolderExistsAsync(connection, ownerId, folderId, cancellationToken, transaction))
            {
                throw DeckException.Validation(new FieldError("folderId", FieldErrorCodes.NotAllowed, "The folder does not exist."));
            }

            deck.Name = changes.Name?.Trim() ?? deck.Name;
            deck.Format = changes.Format ?? deck.Format;
            deck.Visibility = changes.Visibility ?? deck.Visibility;
            deck.FolderId = changes.ClearFolder ? null : changes.FolderId ?? deck.FolderId;
            deck.Description = changes.Description ?? deck.Description;
            deck.Pinned = changes.Pinned ?? deck.Pinned;
            if (changes.Archived is { } archived)
            {
                deck.ArchivedAt = archived ? deck.ArchivedAt ?? time.GetUtcNow().UtcDateTime : null;
            }
        }, cancellationToken);

    public async Task DeleteAsync(Guid ownerId, Guid deckId, CancellationToken cancellationToken)
    {
        await using (var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            _ = await DeckStore.FindDeckAsync(connection, ownerId, deckId, cancellationToken, transaction, forUpdate: true) ?? throw DeckException.NotFound();
            await DeckStore.DeleteDeckAsync(transaction, ownerId, deckId, cancellationToken);
            await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckDeleted(deckId), ownerId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await store.RemoveFromDirectoryAsync(deckId, cancellationToken);
    }

    /// <summary>
    /// Adds cards to a deck. A line with the same card, printing and finish in the same zone gets the new quantity added.
    /// </summary>
    public async Task<DeckView> AddLinesAsync(Guid ownerId, Guid deckId, IReadOnlyList<LineInput> lines, CancellationToken cancellationToken)
    {
        await ValidateLinesAsync(lines, cancellationToken);
        return await MutateAsync(ownerId, deckId, null, (_, _, deck, current) =>
        {
            Merge(current, deck.Id, lines, time.GetUtcNow().UtcDateTime);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>
    /// Replaces all the lines of a deck. The lines that do not change keep their IDs.
    /// </summary>
    public async Task<DeckView> ReplaceLinesAsync(Guid ownerId, Guid deckId, IReadOnlyList<LineInput> lines, int? expectedVersion, CancellationToken cancellationToken)
    {
        await ValidateLinesAsync(lines, cancellationToken);
        return await MutateAsync(ownerId, deckId, expectedVersion, (_, _, deck, current) =>
        {
            var wanted = new List<DeckLineRow>();
            var now = time.GetUtcNow().UtcDateTime;
            Merge(wanted, deck.Id, lines, now);
            var existing = current.ToDictionary(line => line.Key);
            current.Clear();
            foreach (var line in wanted)
            {
                if (existing.TryGetValue(line.Key, out var kept))
                {
                    kept.Quantity = line.Quantity;
                    kept.Note = line.Note;
                    current.Add(kept);
                }
                else
                {
                    current.Add(line);
                }
            }

            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task<DeckView> UpdateLineAsync(Guid ownerId, Guid deckId, Guid lineId, LineChanges changes, CancellationToken cancellationToken)
    {
        if (changes.PrintingId is { } printingId)
        {
            await ValidatePrintingsAsync([printingId], cancellationToken);
        }

        return await MutateAsync(ownerId, deckId, null, async (_, _, _, current) =>
        {
            var line = current.FirstOrDefault(candidate => candidate.Id == lineId)
                ?? throw new DeckException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The deck line does not exist.");
            if (changes.Quantity == 0)
            {
                current.Remove(line);
                return;
            }

            if (changes.PrintingId is { } newPrinting)
            {
                var printing = (await catalog.GetPrintingsAsync([newPrinting], cancellationToken))[newPrinting];
                if (printing.OracleId != line.OracleId)
                {
                    throw DeckException.Validation(new FieldError("printingId", FieldErrorCodes.NotAllowed, "The printing is not a printing of the card of the line."));
                }
            }

            line.Quantity = changes.Quantity ?? line.Quantity;
            line.Zone = changes.Zone ?? line.Zone;
            line.PrintingId = changes.ClearPrinting ? null : changes.PrintingId ?? line.PrintingId;
            line.Finish = changes.ClearFinish ? null : changes.Finish ?? line.Finish;
            line.Note = changes.Note ?? line.Note;
            line.UpdatedAt = time.GetUtcNow().UtcDateTime;

            // A change can give the key of another line, for example a move to another zone. The two lines become one line.
            var same = current.FirstOrDefault(other => other.Id != line.Id && other.Key == line.Key);
            if (same is not null)
            {
                same.Quantity = Math.Min(DeckRules.MaxQuantity, same.Quantity + line.Quantity);
                same.UpdatedAt = line.UpdatedAt;
                current.Remove(line);
            }
        }, cancellationToken);
    }

    public Task<DeckView> DeleteLineAsync(Guid ownerId, Guid deckId, Guid lineId, CancellationToken cancellationToken) =>
        MutateAsync(ownerId, deckId, null, (_, _, _, current) =>
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
    private async Task<DeckView> MutateAsync(
        Guid ownerId,
        Guid deckId,
        int? expectedVersion,
        Func<NpgsqlConnection, NpgsqlTransaction, DeckRow, List<DeckLineRow>, Task> change,
        CancellationToken cancellationToken)
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

        var before = (await DeckStore.ListLinesAsync(connection, deckId, cancellationToken, transaction))
            .ToDictionary(line => line.Id, line => (line.PrintingId, line.Finish, line.Quantity, line.Zone, line.Note));
        var lines = (await DeckStore.ListLinesAsync(connection, deckId, cancellationToken, transaction)).ToList();
        await change(connection, transaction, deck, lines);

        if (lines.Count > DeckRules.MaxLines)
        {
            throw DeckException.Validation(new FieldError("lines", FieldErrorCodes.TooLong, $"A deck can have {DeckRules.MaxLines} lines or fewer."));
        }

        var removed = before.Keys.Except(lines.Select(line => line.Id)).ToList();
        var added = lines.Where(line => !before.ContainsKey(line.Id)).ToList();
        var updated = lines.Where(line => before.TryGetValue(line.Id, out var old) && old != (line.PrintingId, line.Finish, line.Quantity, line.Zone, line.Note)).ToList();

        // The unique key of the lines applies at each statement. Two changed lines can exchange their keys,
        // so the changed lines go out with the removed lines, then come back with the new lines. They keep their IDs.
        await DeckStore.DeleteLinesAsync(transaction, deckId, removed.Concat(updated.Select(line => line.Id)), cancellationToken);
        await DeckStore.InsertLinesAsync(transaction, ownerId, updated.Concat(added), cancellationToken);

        deck.Version++;
        deck.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await DeckStore.UpdateDeckAsync(transaction, deck, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckUpdated(deck.Id, deck.Version, deck.Format, deck.Visibility), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DeckView(deck, lines, true);
    }

    /// <summary>
    /// Adds the wanted lines to a list. A line with the key of an existing line adds its quantity to that line.
    /// </summary>
    private static void Merge(List<DeckLineRow> lines, Guid deckId, IEnumerable<LineInput> inputs, DateTime now)
    {
        foreach (var input in inputs)
        {
            var key = (input.Zone, input.OracleId, input.PrintingId, input.Finish);
            var existing = lines.FirstOrDefault(line => line.Key == key);
            if (existing is not null)
            {
                existing.Quantity = Math.Min(DeckRules.MaxQuantity, existing.Quantity + input.Quantity);
                existing.Note = input.Note.Length > 0 ? input.Note : existing.Note;
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
                CreatedAt = now,
                UpdatedAt = now,
            });
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

    private async Task ValidatePrintingsAsync(IReadOnlyCollection<Guid> printingIds, CancellationToken cancellationToken)
    {
        var printings = await catalog.GetPrintingsAsync(printingIds, cancellationToken);
        if (printings.Count != printingIds.Count)
        {
            throw DeckException.Validation(new FieldError("printingId", FieldErrorCodes.NotAllowed, "The printing does not exist."));
        }
    }
}

/// <summary>
/// The stable error codes of the Decks endpoints.
/// </summary>
public static class DeckErrorCodes
{
    /// <summary>The deck changed since the client read it.</summary>
    public const string VersionConflict = "deck_version_conflict";
}
