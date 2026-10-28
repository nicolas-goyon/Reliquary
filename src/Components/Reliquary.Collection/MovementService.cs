using Dapper;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// Copies of an entry that a movement takes from its origin location.
/// </summary>
/// <param name="EntryId">The entry of the origin location.</param>
/// <param name="Quantity">The number of copies, or <see langword="null"/> for all the copies of the entry.</param>
internal sealed record EntryQuantity(Guid EntryId, int? Quantity);

/// <summary>
/// The filters and the page of the movement history.
/// </summary>
/// <param name="LocationIds">Only the movements from or to these locations, or <see langword="null"/>.</param>
/// <param name="BatchId">Only the movements of this batch, or <see langword="null"/>.</param>
/// <param name="Before">The date and the ID of the last movement of the previous page, or <see langword="null"/>.</param>
/// <param name="Limit">The maximum number of movements.</param>
internal sealed record MovementQuery(IReadOnlyList<Guid>? LocationIds, Guid? BatchId, (DateTime CreatedAt, Guid Id)? Before, int Limit);

/// <summary>
/// Adds, removes and moves cards in one change: all the movements of a request have the same batch ID.
/// </summary>
internal sealed class MovementService(CollectionStore store, IDomainEventWriter events, TimeProvider time)
{
    /// <summary>
    /// Adds cards to a location from a place outside the collection.
    /// </summary>
    public async Task<IReadOnlyList<MovementRow>> AddAsync(Guid ownerId, MovementEndpoint origin, Guid destinationId, IReadOnlyList<ResolvedCard> cards, CancellationToken cancellationToken)
    {
        return await ChangeAsync(ownerId, async change =>
        {
            await change.LockAsync(destinationId, allowDeck: false, cancellationToken);
            foreach (var card in cards)
            {
                await change.AddAsync(destinationId, card.Line, card.Quantity, origin, cancellationToken);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Takes copies of entries from their location, and moves them to another location or removes them from the collection.
    /// A move keeps the card fields of the copies.
    /// </summary>
    public async Task<IReadOnlyList<MovementRow>> TakeAsync(Guid ownerId, Guid originId, MovementEndpoint destination, IReadOnlyList<EntryQuantity> cards, CancellationToken cancellationToken)
    {
        if (destination.LocationId == originId)
        {
            throw CollectionException.Validation(new FieldError("destination.locationId", FieldErrorCodes.NotAllowed, "The destination must be another location than the origin."));
        }

        return await ChangeAsync(ownerId, async change =>
        {
            // The locks follow the order of the IDs, so two opposite moves cannot wait for each other.
            foreach (var id in new[] { originId, destination.LocationId }.OfType<Guid>().Order())
            {
                await change.LockAsync(id, allowDeck: false, cancellationToken);
            }

            for (var index = 0; index < cards.Count; index++)
            {
                var card = cards[index];
                var entry = await CollectionStore.FindEntryAsync(change.Connection, ownerId, card.EntryId, cancellationToken, change.Transaction);
                if (entry is null || entry.LocationId != originId)
                {
                    throw CollectionException.Validation(new FieldError($"cards[{index}].entryId", FieldErrorCodes.NotAllowed, "The entry is not in the origin location."));
                }

                if (destination.LocationId is { } destinationId)
                {
                    await change.MoveAsync(entry, destinationId, card.Quantity ?? entry.Quantity, cancellationToken);
                }
                else
                {
                    await change.RemoveAsync(entry, card.Quantity ?? entry.Quantity, destination, cancellationToken);
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Lists a page of the movement history, the most recent first.
    /// </summary>
    public async Task<IReadOnlyList<MovementRow>> ListAsync(Guid ownerId, MovementQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return [.. await connection.QueryAsync<MovementRow>(new CommandDefinition(
            """
            select m.id, m.owner_id as OwnerId, m.batch_id as BatchId, m.kind, m.origin_kind as OriginKind, m.origin_location_id as OriginLocationId,
                m.destination_kind as DestinationKind, m.destination_location_id as DestinationLocationId, m.oracle_id as OracleId,
                m.printing_id as PrintingId, m.card_name as CardName, m.language, m.finish, m.condition, m.purchase_price as PurchasePrice,
                m.purchase_currency as PurchaseCurrency, m.acquired_on::timestamp as AcquiredOn, m.note, m.quantity, m.inventory_id as InventoryId,
                m.created_at as CreatedAt,
                case when op.id is null then o.name else op.name || ' / ' || o.name end as OriginName,
                case when dp.id is null then d.name else dp.name || ' / ' || d.name end as DestinationName
            from content.movements m
            left join content.locations o on o.id = m.origin_location_id
            left join content.locations op on op.id = o.parent_id
            left join content.locations d on d.id = m.destination_location_id
            left join content.locations dp on dp.id = d.parent_id
            where m.owner_id = @ownerId
                and (@locationIds::uuid[] is null or m.origin_location_id = any(@locationIds) or m.destination_location_id = any(@locationIds))
                and (@batchId::uuid is null or m.batch_id = @batchId)
                and (@beforeAt::timestamptz is null or (m.created_at, m.id) < (@beforeAt, @beforeId))
            order by m.created_at desc, m.id desc
            limit @limit
            """,
            new
            {
                ownerId,
                locationIds = query.LocationIds?.ToArray(),
                batchId = query.BatchId,
                beforeAt = query.Before is { } before ? DateTime.SpecifyKind(before.CreatedAt, DateTimeKind.Utc) : (DateTime?)null,
                beforeId = query.Before?.Id ?? Guid.Empty,
                limit = query.Limit,
            },
            cancellationToken: cancellationToken))];
    }

    private async Task<IReadOnlyList<MovementRow>> ChangeAsync(Guid ownerId, Func<CollectionChange, Task> apply, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var change = new CollectionChange(transaction, ownerId, time.GetUtcNow().UtcDateTime);
        await apply(change);
        await change.CompleteAsync(events, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return change.Movements;
    }
}
