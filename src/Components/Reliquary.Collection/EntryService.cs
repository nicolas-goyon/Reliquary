using Dapper;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// The changes of an entry. A <see langword="null"/> field does not change. A clear flag removes the value.
/// </summary>
internal sealed record EntryChanges(
    int? Quantity,
    Guid? PrintingId,
    bool ClearPrinting,
    string? Language,
    string? Finish,
    string? Condition,
    decimal? PurchasePrice,
    string? PurchaseCurrency,
    bool ClearPurchasePrice,
    DateOnly? AcquiredOn,
    bool ClearAcquiredOn,
    string? Note);

/// <summary>
/// The result of a change of the entries: the batch of the movements and the entries that have copies after the change.
/// </summary>
internal sealed record EntriesChange(Guid BatchId, IReadOnlyList<EntryRow> Entries);

/// <summary>
/// A page of the entries of a location.
/// </summary>
/// <param name="LocationIds">The location and, when the page includes them, its sections.</param>
/// <param name="Search">A part of the card name, or <see langword="null"/>.</param>
/// <param name="AfterName">The card name of the last entry of the previous page, or <see langword="null"/>.</param>
/// <param name="AfterId">The ID of the last entry of the previous page, or <see langword="null"/>.</param>
/// <param name="Limit">The maximum number of entries.</param>
/// <param name="Language">Only the entries in this language, or <see langword="null"/>.</param>
/// <param name="SetCode">Only the entries of a printing of this set, or <see langword="null"/>.</param>
internal sealed record EntryPageQuery(IReadOnlyList<Guid> LocationIds, string? Search, string? AfterName, Guid? AfterId, int Limit, string? Language = null, string? SetCode = null);

/// <summary>
/// The operations on the entries of the locations. Each change writes movements in the same transaction.
/// </summary>
internal sealed class EntryService(CollectionStore store, ICardCatalog catalog, IDomainEventWriter events, TimeProvider time)
{
    /// <summary>
    /// Lists a page of the entries of some locations, by card name.
    /// </summary>
    public async Task<IReadOnlyList<EntryRow>> ListAsync(Guid ownerId, EntryPageQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return [.. await connection.QueryAsync<EntryRow>(new CommandDefinition(
            $"""
            select {CollectionStore.EntryColumns}
            from content.location_entries e
            where e.owner_id = @ownerId and e.location_id = any(@locationIds)
                and (@search::text is null or e.card_name ilike @search)
                and (@language::text is null or e.language = @language)
                and (@setCode::text is null or e.set_code = @setCode)
                and (@afterName::text is null or (e.card_name, e.id) > (@afterName, @afterId))
            order by e.card_name, e.id
            limit @limit
            """,
            new
            {
                ownerId,
                locationIds = query.LocationIds.ToArray(),
                search = Like(query.Search),
                language = query.Language,
                setCode = query.SetCode?.ToLowerInvariant(),
                afterName = query.AfterName,
                afterId = query.AfterId ?? Guid.Empty,
                limit = query.Limit,
            },
            cancellationToken: cancellationToken))];
    }

    /// <summary>
    /// Adds cards to a location. A card with the same fields as an entry of the location adds to its quantity.
    /// </summary>
    public async Task<EntriesChange> AddAsync(Guid ownerId, Guid locationId, IReadOnlyList<ResolvedCard> cards, MovementEndpoint origin, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var change = new CollectionChange(transaction, ownerId, time.GetUtcNow().UtcDateTime);
        await change.LockAsync(locationId, allowDeck: false, cancellationToken);
        var entries = new Dictionary<Guid, EntryRow>();
        foreach (var card in cards)
        {
            var entry = await change.AddAsync(locationId, card.Line, card.Quantity, origin, cancellationToken);
            entries[entry.Id] = entry;
        }

        await change.CompleteAsync(events, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new EntriesChange(change.BatchId, [.. entries.Values]);
    }

    /// <summary>
    /// Changes the quantity or the fields of an entry. A change of a field moves the copies to the entry with the new fields.
    /// </summary>
    public async Task<EntriesChange> UpdateAsync(Guid ownerId, Guid entryId, EntryChanges changes, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var entry = await CollectionStore.FindEntryAsync(connection, ownerId, entryId, cancellationToken, transaction) ?? throw CollectionException.EntryNotFound();
        var change = new CollectionChange(transaction, ownerId, time.GetUtcNow().UtcDateTime);
        await change.LockAsync(entry.LocationId, allowDeck: false, cancellationToken);
        entry = await CollectionStore.FindEntryAsync(connection, ownerId, entryId, cancellationToken, transaction) ?? throw CollectionException.EntryNotFound();
        var settings = await SettingsService.GetAsync(connection, ownerId, cancellationToken, transaction);
        var line = await ApplyAsync(entry.Line, settings.Input(changes), cancellationToken);
        var quantity = changes.Quantity ?? entry.Quantity;
        EntryRow? result = null;
        if (line.Fields == entry.Fields)
        {
            if (quantity > entry.Quantity)
            {
                result = await change.AddAsync(entry.LocationId, line, quantity - entry.Quantity, MovementEndpoint.Nowhere, cancellationToken);
            }
            else
            {
                if (quantity < entry.Quantity)
                {
                    await change.RemoveAsync(entry, entry.Quantity - quantity, MovementEndpoint.Nowhere, cancellationToken);
                }

                result = quantity > 0 ? entry : null;
            }
        }
        else
        {
            await change.RemoveAsync(entry, entry.Quantity, MovementEndpoint.Nowhere, cancellationToken);
            if (quantity > 0)
            {
                result = await change.AddAsync(entry.LocationId, line, quantity, MovementEndpoint.Nowhere, cancellationToken);
            }
        }

        await change.CompleteAsync(events, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new EntriesChange(change.BatchId, result is null ? [] : [result]);
    }

    /// <summary>
    /// Removes all the copies of an entry from the collection.
    /// </summary>
    public async Task<Guid> DeleteAsync(Guid ownerId, Guid entryId, MovementEndpoint destination, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var entry = await CollectionStore.FindEntryAsync(connection, ownerId, entryId, cancellationToken, transaction) ?? throw CollectionException.EntryNotFound();
        var change = new CollectionChange(transaction, ownerId, time.GetUtcNow().UtcDateTime);
        await change.LockAsync(entry.LocationId, allowDeck: false, cancellationToken);
        entry = await CollectionStore.FindEntryAsync(connection, ownerId, entryId, cancellationToken, transaction) ?? throw CollectionException.EntryNotFound();
        await change.RemoveAsync(entry, entry.Quantity, destination, cancellationToken);
        await change.CompleteAsync(events, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return change.BatchId;
    }

    /// <summary>
    /// Gives the pattern of a search in the card names, with the special characters of <c>like</c> escaped.
    /// </summary>
    public static string? Like(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? null
            : $"%{search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";

    /// <summary>
    /// Applies the changes to the fields of an entry, and checks the new printing with the card catalog.
    /// </summary>
    private async Task<CardLine> ApplyAsync(CardLine line, EntryChanges changes, CancellationToken cancellationToken)
    {
        var fields = line.Fields;
        var errors = new List<FieldError>();
        CardResolver.CheckCardFields(
            errors,
            string.Empty,
            changes.Language,
            string.IsNullOrEmpty(changes.Finish) ? null : changes.Finish,
            string.IsNullOrEmpty(changes.Condition) ? null : changes.Condition,
            changes.PurchasePrice,
            changes.PurchasePrice is null ? null : changes.PurchaseCurrency,
            changes.Note);
        if (changes.Quantity is < 0 or > CollectionRules.MaxQuantity)
        {
            errors.Add(new FieldError("quantity", FieldErrorCodes.OutOfRange, $"The quantity must be from 0 to {CollectionRules.MaxQuantity}."));
        }

        if (errors.Count > 0)
        {
            throw CollectionException.Validation([.. errors]);
        }

        var printingId = changes.ClearPrinting ? null : changes.PrintingId ?? fields.PrintingId;
        var setCode = changes.ClearPrinting ? null : line.SetCode;
        var language = changes.Language ?? fields.Language;
        if (changes.PrintingId is { } newPrinting && newPrinting != fields.PrintingId)
        {
            var printings = await catalog.GetPrintingsAsync([newPrinting], cancellationToken);
            if (!printings.TryGetValue(newPrinting, out var printing) || printing.OracleId != fields.OracleId)
            {
                throw CollectionException.Validation(new FieldError("printingId", FieldErrorCodes.NotAllowed, "The printing does not exist, or it is not a printing of the card."));
            }

            setCode = printing.SetCode;
            language = changes.Language ?? printing.Lang;
            if (language != printing.Lang)
            {
                throw CollectionException.Validation(new FieldError("language", FieldErrorCodes.NotAllowed, $"The printing is in the language \"{printing.Lang}\". Give this language, or give no language."));
            }
        }

        var price = changes.ClearPurchasePrice ? null : changes.PurchasePrice ?? fields.PurchasePrice;
        var currency = changes.ClearPurchasePrice ? null : changes.PurchasePrice is null ? fields.PurchaseCurrency : changes.PurchaseCurrency!.ToUpperInvariant();
        var updated = fields with
        {
            PrintingId = printingId,
            Language = language,
            Finish = changes.Finish is null ? fields.Finish : changes.Finish.Length == 0 ? null : changes.Finish,
            Condition = changes.Condition is null ? fields.Condition : changes.Condition.Length == 0 ? null : changes.Condition,
            PurchasePrice = price,
            PurchaseCurrency = currency,
            AcquiredOn = changes.ClearAcquiredOn ? null : changes.AcquiredOn ?? fields.AcquiredOn,
            Note = changes.Note is null ? fields.Note : string.IsNullOrWhiteSpace(changes.Note) ? null : changes.Note.Trim(),
        };
        return new CardLine(updated, line.CardName, setCode);
    }
}
