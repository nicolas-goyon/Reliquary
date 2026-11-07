using Dapper;
using Microsoft.Extensions.Logging;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// Manages the locations of the assembled decks for the Decks component. See <see cref="IAssembledDeckLocations"/>.
/// </summary>
/// <remarks>
/// The location of a deck has the kind <c>deck</c> and is not pickable. Its cards are never available cards.
/// The tokens of the deck go to the table <c>content.deck_tokens</c>, not to the entries.
/// </remarks>
internal sealed class AssembledDeckLocations(
    CollectionStore store,
    ICardCatalog catalog,
    IDomainEventWriter events,
    TimeProvider time,
    ILogger<AssembledDeckLocations> logger) : IAssembledDeckLocations
{
    /// <inheritdoc />
    public async Task<Guid> EnsureLocationAsync(Guid ownerId, Guid deckId, string deckName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deckName);
        var name = deckName.Trim();
        name = name.Length > CollectionRules.MaxNameLength ? name[..CollectionRules.MaxNameLength] : name;
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var location = await CollectionStore.FindDeckLocationAsync(connection, ownerId, deckId, cancellationToken, transaction, forUpdate: true);
        if (location is null)
        {
            location = new LocationRow
            {
                Id = Ids.New(),
                OwnerId = ownerId,
                Name = name,
                Kind = LocationKinds.Deck,
                Pickable = false,
                DeckId = deckId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await CollectionStore.InsertLocationAsync(transaction, location, cancellationToken);
            await events.WriteAsync(DatabaseTarget.Content, transaction, new LocationCreated(location.Id, location.Kind, null, deckId), ownerId, cancellationToken);
        }
        else if (location.Name != name)
        {
            location.Name = name;
            location.UpdatedAt = now;
            await CollectionStore.UpdateLocationAsync(transaction, location, cancellationToken);
            await events.WriteAsync(DatabaseTarget.Content, transaction, new LocationUpdated(location.Id, location.Kind, location.Locked, location.Pickable), ownerId, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return location.Id;
    }

    /// <inheritdoc />
    public async Task SetContentAsync(Guid ownerId, Guid deckId, IReadOnlyList<AssembledDeckCard> cards, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var facts = await catalog.GetCardsAsync([.. cards.Select(card => card.OracleId).Distinct()], cancellationToken);
        var unknown = cards.Select(card => card.OracleId).Where(id => !facts.ContainsKey(id)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"The cards {string.Join(", ", unknown)} do not exist.", nameof(cards));
        }

        var printings = await catalog.GetPrintingsAsync([.. cards.Where(card => card.PrintingId is not null).Select(card => card.PrintingId!.Value).Distinct()], cancellationToken);
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var settings = await SettingsService.GetAsync(connection, ownerId, cancellationToken, transaction);
        var tokens = cards.Where(card => IsToken(facts[card.OracleId])).Select(card => card.OracleId).ToHashSet();
        var wanted = cards
            .Where(card => !tokens.Contains(card.OracleId) && card.Quantity > 0)
            .Select(card =>
            {
                var printing = card.PrintingId is { } printingId ? printings.GetValueOrDefault(printingId) : null;
                var fields = new CardFields(card.OracleId, printing?.PrintingId, printing?.Lang ?? card.Language, settings.Finish ? card.Finish : null, null, null, null, null, null);
                return (Line: new CardLine(fields, facts[card.OracleId].Name, printing?.SetCode), card.Quantity);
            })
            .GroupBy(card => card.Line.Fields)
            .ToDictionary(group => group.Key, group => (group.First().Line, Quantity: group.Sum(card => card.Quantity)));

        var location = await CollectionStore.FindDeckLocationAsync(connection, ownerId, deckId, cancellationToken, transaction, forUpdate: true)
            ?? throw new InvalidOperationException($"The deck {deckId} has no location. Call {nameof(EnsureLocationAsync)} first.");
        var change = new CollectionChange(transaction, ownerId, time.GetUtcNow().UtcDateTime);
        await LockAsync(change, location.Id, allowDeck: true, cancellationToken);
        var current = await CollectionStore.ListEntriesAsync(connection, ownerId, [location.Id], cancellationToken, transaction);
        foreach (var entry in current)
        {
            var keep = wanted.TryGetValue(entry.Fields, out var card) ? Math.Min(card.Quantity, entry.Quantity) : 0;
            if (entry.Quantity > keep)
            {
                await change.RemoveAsync(entry, entry.Quantity - keep, MovementEndpoint.Nowhere, cancellationToken);
            }

            if (wanted.ContainsKey(entry.Fields))
            {
                wanted[entry.Fields] = (card.Line, card.Quantity - keep);
            }
        }

        foreach (var (line, quantity) in wanted.Values.Where(card => card.Quantity > 0))
        {
            await change.AddAsync(location.Id, line, quantity, MovementEndpoint.Nowhere, cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            delete from content.deck_tokens where location_id = @locationId and not (oracle_id = any(@tokens));
            insert into content.deck_tokens (location_id, owner_id, oracle_id, owned, updated_at)
            select @locationId, @ownerId, token, false, @now from unnest(@tokens) as token
            on conflict (location_id, oracle_id) do nothing;
            """,
            new { locationId = location.Id, ownerId, tokens = tokens.ToArray(), now = change.Now },
            transaction,
            cancellationToken: cancellationToken));
        await change.CompleteAsync(events, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(Guid ownerId, Guid deckId, Guid? targetLocationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var location = await CollectionStore.FindDeckLocationAsync(connection, ownerId, deckId, cancellationToken, transaction, forUpdate: true);
        if (location is null)
        {
            return;
        }

        var change = new CollectionChange(transaction, ownerId, time.GetUtcNow().UtcDateTime);
        await LockAsync(change, location.Id, allowDeck: true, cancellationToken);
        if (targetLocationId is { } targetId)
        {
            if (targetId == location.Id)
            {
                throw new ArgumentException("The target location is the location of the deck.", nameof(targetLocationId));
            }

            await LockAsync(change, targetId, allowDeck: false, cancellationToken);
        }

        var entries = await CollectionStore.ListEntriesAsync(connection, ownerId, [location.Id], cancellationToken, transaction);
        foreach (var entry in entries)
        {
            if (targetLocationId.HasValue)
            {
                await change.MoveAsync(entry, targetLocationId.Value, entry.Quantity, cancellationToken);
            }
            else
            {
                await change.RemoveAsync(entry, entry.Quantity, MovementEndpoint.Nowhere, cancellationToken);
            }
        }

        await change.CompleteAsync(events, cancellationToken);
        await CollectionStore.DeleteLocationAsync(transaction, ownerId, location.Id, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Content, transaction, new LocationDeleted(location.Id), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var cards = entries.Sum(entry => entry.Quantity);
        var target = targetLocationId?.ToString() ?? CollectionRules.Endpoints.Nowhere;
        logger.DeckLocationReleased(location.Id, deckId, cards, target);
    }

    /// <summary>
    /// Gets a value that tells whether a card is a token: the token cards of the tokens zone.
    /// </summary>
    internal static bool IsToken(CardFacts card) =>
        card.IsExtra && card.TypeLine is { } typeLine && typeLine.Contains("Token", StringComparison.Ordinal);

    /// <summary>
    /// Locks a location for the change, with the errors of the building blocks for the Decks component.
    /// </summary>
    private static async Task LockAsync(CollectionChange change, Guid locationId, bool allowDeck, CancellationToken cancellationToken)
    {
        try
        {
            await change.LockAsync(locationId, allowDeck, cancellationToken);
        }
        catch (CollectionException exception) when (exception.Code == CollectionErrorCodes.LocationLocked)
        {
            throw new LocationLockedException(locationId);
        }
        catch (CollectionException exception)
        {
            throw new ArgumentException(exception.Message, nameof(locationId), exception);
        }
    }
}
