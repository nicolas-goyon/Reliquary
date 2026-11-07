using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;

namespace Reliquary.Collection.Data;

/// <summary>
/// One change of the entries in one transaction: it adds and removes copies, and it records a movement for each change.
/// All the movements of the change have the same batch ID. Call <see cref="CompleteAsync"/> before the commit.
/// </summary>
/// <remarks>
/// The caller locks each location with <see cref="LockAsync"/> before it changes the entries of the location.
/// </remarks>
internal sealed class CollectionChange(NpgsqlTransaction transaction, Guid ownerId, DateTime now, Guid? inventoryId = null)
{
    private readonly Dictionary<Guid, LocationRow> locked = [];
    private readonly List<MovementRow> movements = [];
    private readonly HashSet<Guid> oracleIds = [];
    private readonly HashSet<Guid> changedLocations = [];

    public Guid BatchId { get; } = Ids.New();

    public NpgsqlTransaction Transaction => transaction;

    public NpgsqlConnection Connection => transaction.Connection!;

    public Guid OwnerId => ownerId;

    public DateTime Now => now;

    public IReadOnlyList<MovementRow> Movements => movements;

    /// <summary>
    /// Locks a location for the change, and checks that its content can change.
    /// </summary>
    /// <param name="locationId">The location.</param>
    /// <param name="allowDeck">Whether the location can be the location of an assembled deck. Only the Decks component changes these locations.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The location.</returns>
    public async Task<LocationRow> LockAsync(Guid locationId, bool allowDeck, CancellationToken cancellationToken)
    {
        if (locked.TryGetValue(locationId, out var known))
        {
            return known;
        }

        var location = await CollectionStore.FindLocationAsync(Connection, ownerId, locationId, cancellationToken, transaction, forUpdate: true)
            ?? throw CollectionException.LocationNotFound();
        if (location.DeckId is not null && !allowDeck)
        {
            throw CollectionException.DeckLocation();
        }

        if (location.EffectiveLocked)
        {
            throw CollectionException.Locked(location.FullName);
        }

        locked[locationId] = location;
        return location;
    }

    /// <summary>
    /// Adds copies to a location. The copies join the entry with the same card fields, or make a new entry.
    /// </summary>
    /// <returns>The entry that has the copies.</returns>
    public async Task<EntryRow> AddAsync(Guid locationId, CardLine line, int quantity, MovementEndpoint origin, CancellationToken cancellationToken)
    {
        var entry = await PutAsync(locationId, line, quantity, cancellationToken);
        Record(origin.IsLocation ? "move" : "add", origin, MovementEndpoint.At(locationId), line, quantity);
        return entry;
    }

    /// <summary>
    /// Removes copies from an entry. An entry with no copies disappears.
    /// </summary>
    public async Task RemoveAsync(EntryRow entry, int quantity, MovementEndpoint destination, CancellationToken cancellationToken)
    {
        Take(entry, quantity);
        await CollectionStore.SetQuantityAsync(transaction, entry.Id, entry.Quantity, now, cancellationToken);
        if (!destination.IsLocation)
        {
            Record("remove", MovementEndpoint.At(entry.LocationId), destination, entry.Line, quantity);
        }
    }

    /// <summary>
    /// Moves copies of an entry to another location. The copies keep their card fields.
    /// </summary>
    /// <returns>The entry of the destination.</returns>
    public async Task<EntryRow> MoveAsync(EntryRow entry, Guid destinationId, int quantity, CancellationToken cancellationToken)
    {
        var line = entry.Line;
        var origin = MovementEndpoint.At(entry.LocationId);
        await RemoveAsync(entry, quantity, MovementEndpoint.At(destinationId), cancellationToken);
        return await AddAsync(destinationId, line, quantity, origin, cancellationToken);
    }

    /// <summary>
    /// Writes the movements and the domain event of the change. Call it one time, before the commit.
    /// </summary>
    public async Task CompleteAsync(IDomainEventWriter events, CancellationToken cancellationToken)
    {
        if (movements.Count == 0)
        {
            return;
        }

        await CollectionStore.InsertMovementsAsync(transaction, movements, cancellationToken);
        await events.WriteAsync(
            DatabaseTarget.Content,
            transaction,
            new EntriesChanged(BatchId, [.. changedLocations.Order()], [.. oracleIds.Order()]),
            ownerId,
            cancellationToken);
    }

    private async Task<EntryRow> PutAsync(Guid locationId, CardLine line, int quantity, CancellationToken cancellationToken)
    {
        changedLocations.Add(locationId);
        var entry = await CollectionStore.FindSameEntryAsync(transaction, locationId, line.Fields, cancellationToken);
        if (entry is not null)
        {
            if (entry.Quantity + quantity > CollectionRules.MaxQuantity)
            {
                throw CollectionException.Validation(new BuildingBlocks.Api.FieldError(
                    "quantity", BuildingBlocks.Api.FieldErrorCodes.OutOfRange, $"An entry can have {CollectionRules.MaxQuantity} copies or fewer."));
            }

            entry.Quantity += quantity;
            entry.UpdatedAt = now;
            await CollectionStore.SetQuantityAsync(transaction, entry.Id, entry.Quantity, now, cancellationToken);
            return entry;
        }

        var fields = line.Fields;
        entry = new EntryRow
        {
            Id = Ids.New(),
            OwnerId = ownerId,
            LocationId = locationId,
            OracleId = fields.OracleId,
            PrintingId = fields.PrintingId,
            CardName = line.CardName,
            SetCode = line.SetCode,
            Language = fields.Language,
            Finish = fields.Finish,
            Condition = fields.Condition,
            PurchasePrice = fields.PurchasePrice,
            PurchaseCurrency = fields.PurchaseCurrency,
            AcquiredOn = fields.AcquiredOnValue,
            Note = fields.Note,
            Quantity = quantity,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await CollectionStore.InsertEntryAsync(transaction, entry, cancellationToken);
        return entry;
    }

    private void Take(EntryRow entry, int quantity)
    {
        if (quantity > entry.Quantity)
        {
            throw CollectionException.NotEnoughCards(entry.CardName, entry.Quantity);
        }

        changedLocations.Add(entry.LocationId);
        entry.Quantity -= quantity;
        entry.UpdatedAt = now;
    }

    private void Record(string kind, MovementEndpoint origin, MovementEndpoint destination, CardLine line, int quantity)
    {
        var fields = line.Fields;
        oracleIds.Add(fields.OracleId);
        movements.Add(new MovementRow
        {
            Id = Ids.New(),
            OwnerId = ownerId,
            BatchId = BatchId,
            Kind = kind,
            OriginKind = origin.Kind,
            OriginLocationId = origin.LocationId,
            DestinationKind = destination.Kind,
            DestinationLocationId = destination.LocationId,
            OracleId = fields.OracleId,
            PrintingId = fields.PrintingId,
            CardName = line.CardName,
            Language = fields.Language,
            Finish = fields.Finish,
            Condition = fields.Condition,
            PurchasePrice = fields.PurchasePrice,
            PurchaseCurrency = fields.PurchaseCurrency,
            AcquiredOn = fields.AcquiredOnValue,
            Note = fields.Note,
            Quantity = quantity,
            InventoryId = inventoryId,
            CreatedAt = now,
        });
    }
}
