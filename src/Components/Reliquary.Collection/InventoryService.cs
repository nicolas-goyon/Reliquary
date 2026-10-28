using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// An inventory of a location: <c>open</c> while the owner answers the differences, then <c>applied</c>.
/// </summary>
internal sealed class InventoryRow
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public string Status { get; set; } = "open";

    public DateTime CreatedAt { get; set; }

    public DateTime? AppliedAt { get; set; }

    public long CardCount { get; set; }
}

/// <summary>
/// A difference between the counted content of an inventory and the current content of the location.
/// </summary>
/// <param name="Key">The stable key of the card fields. The answers of the owner use it.</param>
/// <param name="Line">The card fields with the name and the set code.</param>
/// <param name="Expected">The number of copies in the location now.</param>
/// <param name="Counted">The number of copies that the owner counted.</param>
internal sealed record InventoryDifference(string Key, CardLine Line, int Expected, int Counted)
{
    /// <summary>Gets the change: a positive number for copies to add, a negative number for copies to remove.</summary>
    public int Delta => Counted - Expected;
}

/// <summary>
/// An inventory with its counted content and, while it is open, the differences with the location.
/// </summary>
internal sealed record InventoryView(InventoryRow Inventory, IReadOnlyList<EntryRow> Entries, IReadOnlyList<InventoryDifference> Differences);

/// <summary>
/// The answers of the owner for the differences of an inventory.
/// </summary>
/// <param name="ByKey">The origin of the added copies or the destination of the removed copies, for each difference.</param>
/// <param name="Origin">The origin of the added copies of the differences with no answer, or <see langword="null"/>.</param>
/// <param name="Destination">The destination of the removed copies of the differences with no answer, or <see langword="null"/>.</param>
internal sealed record InventoryAnswers(IReadOnlyDictionary<string, MovementEndpoint> ByKey, MovementEndpoint? Origin, MovementEndpoint? Destination);

/// <summary>
/// The inventories of the locations. An inventory compares a counted content with the location,
/// writes movements for the differences, and keeps the content of the location at its date.
/// </summary>
internal sealed class InventoryService(CollectionStore store, IDomainEventWriter events, TimeProvider time, ILogger<InventoryService> logger)
{
    private const string InventoryColumns = """
        i.id, i.location_id as LocationId, i.status, i.created_at as CreatedAt, i.applied_at as AppliedAt,
        coalesce((select sum(x.quantity) from content.inventory_entries x where x.inventory_id = i.id), 0) as CardCount
        """;

    /// <summary>
    /// Starts an inventory of a location with the counted content. An open inventory of the location stops.
    /// </summary>
    public async Task<Guid> StartAsync(Guid ownerId, Guid locationId, IReadOnlyList<ResolvedCard> counted, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var location = await CollectionStore.FindLocationAsync(connection, ownerId, locationId, cancellationToken, transaction, forUpdate: true)
            ?? throw CollectionException.LocationNotFound();
        if (location.DeckId is not null)
        {
            throw CollectionException.DeckLocation();
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "delete from content.inventories where location_id = @locationId and owner_id = @ownerId and status = 'open'",
            new { locationId, ownerId },
            transaction,
            cancellationToken: cancellationToken));
        var id = Ids.New();
        await connection.ExecuteAsync(new CommandDefinition(
            "insert into content.inventories (id, owner_id, location_id, status, created_at) values (@id, @ownerId, @locationId, 'open', @now)",
            new { id, ownerId, locationId, now = time.GetUtcNow().UtcDateTime },
            transaction,
            cancellationToken: cancellationToken));
        var rows = counted
            .GroupBy(card => card.Line.Fields)
            .Select(group => (group.First().Line, Quantity: group.Sum(card => card.Quantity)))
            .Select(card => new
            {
                Id = Ids.New(),
                InventoryId = id,
                OwnerId = ownerId,
                card.Line.Fields.OracleId,
                card.Line.Fields.PrintingId,
                card.Line.CardName,
                card.Line.SetCode,
                card.Line.Fields.Language,
                card.Line.Fields.Finish,
                card.Line.Fields.Condition,
                card.Line.Fields.PurchasePrice,
                card.Line.Fields.PurchaseCurrency,
                card.Line.Fields.AcquiredOnText,
                card.Line.Fields.Note,
                Quantity = Math.Min(card.Quantity, CollectionRules.MaxQuantity),
            });
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.inventory_entries (
                id, inventory_id, owner_id, oracle_id, printing_id, card_name, set_code, language, finish, condition,
                purchase_price, purchase_currency, acquired_on, note, quantity)
            values (
                @Id, @InventoryId, @OwnerId, @OracleId, @PrintingId, @CardName, @SetCode, @Language, @Finish, @Condition,
                @PurchasePrice, @PurchaseCurrency, @AcquiredOnText::date, @Note, @Quantity)
            """,
            rows,
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// Gets an inventory with its counted content. An open inventory also gives its differences with the location.
    /// </summary>
    public async Task<InventoryView> GetAsync(Guid ownerId, Guid inventoryId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        var inventory = await FindAsync(connection, null, ownerId, inventoryId, cancellationToken);
        var counted = await CountedAsync(connection, null, inventoryId, cancellationToken);
        if (inventory.Status != "open")
        {
            return new InventoryView(inventory, counted, []);
        }

        var settings = await SettingsService.GetAsync(connection, ownerId, cancellationToken);
        var current = await CollectionStore.ListEntriesAsync(connection, ownerId, [inventory.LocationId], cancellationToken);
        return new InventoryView(inventory, counted, Differences(counted, current, settings));
    }

    /// <summary>
    /// Lists the inventories of a location, the most recent first.
    /// </summary>
    public async Task<IReadOnlyList<InventoryRow>> ListAsync(Guid ownerId, Guid locationId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        _ = await CollectionStore.FindLocationAsync(connection, ownerId, locationId, cancellationToken) ?? throw CollectionException.LocationNotFound();
        return [.. await connection.QueryAsync<InventoryRow>(new CommandDefinition(
            $"select {InventoryColumns} from content.inventories i where i.location_id = @locationId and i.owner_id = @ownerId order by i.created_at desc, i.id desc",
            new { locationId, ownerId },
            cancellationToken: cancellationToken))];
    }

    /// <summary>
    /// Stops an open inventory with no change of the location.
    /// </summary>
    public async Task DeleteAsync(Guid ownerId, Guid inventoryId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var inventory = await FindAsync(connection, transaction, ownerId, inventoryId, cancellationToken, forUpdate: true);
        if (inventory.Status != "open")
        {
            throw Applied();
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "delete from content.inventories where id = @inventoryId", new { inventoryId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Applies an open inventory: it writes a movement for each difference, with the origin or the destination that the owner gave.
    /// Then the location has the counted content, and the inventory keeps this content.
    /// </summary>
    public async Task ApplyAsync(Guid ownerId, Guid inventoryId, InventoryAnswers answers, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var inventory = await FindAsync(connection, transaction, ownerId, inventoryId, cancellationToken, forUpdate: true);
        if (inventory.Status != "open")
        {
            throw Applied();
        }

        var settings = await SettingsService.GetAsync(connection, ownerId, cancellationToken, transaction);
        var change = new CollectionChange(transaction, ownerId, time.GetUtcNow().UtcDateTime, inventoryId);
        var counted = await CountedAsync(connection, transaction, inventoryId, cancellationToken);
        var current = await CollectionStore.ListEntriesAsync(connection, ownerId, [inventory.LocationId], cancellationToken, transaction);
        var differences = Differences(counted, current, settings);
        var plan = Plan(differences, answers, inventory.LocationId);
        if (plan.Count > 0)
        {
            var locations = plan.Select(step => step.Endpoint.LocationId).OfType<Guid>().Append(inventory.LocationId).Distinct().Order();
            foreach (var id in locations)
            {
                await change.LockAsync(id, allowDeck: false, cancellationToken);
            }
        }

        foreach (var (difference, endpoint) in plan)
        {
            if (difference.Delta > 0)
            {
                if (endpoint.LocationId is { } originId)
                {
                    var source = await CollectionStore.ListEntriesAsync(connection, ownerId, [originId], cancellationToken, transaction);
                    await TakeAsync(source, difference, difference.Delta, settings, entry => change.MoveAsync(entry.Row, inventory.LocationId, entry.Quantity, cancellationToken));
                }
                else
                {
                    await change.AddAsync(inventory.LocationId, difference.Line, difference.Delta, endpoint, cancellationToken);
                }
            }
            else
            {
                var here = await CollectionStore.ListEntriesAsync(connection, ownerId, [inventory.LocationId], cancellationToken, transaction);
                await TakeAsync(here, difference, -difference.Delta, settings, entry => endpoint.LocationId is { } destinationId
                    ? change.MoveAsync(entry.Row, destinationId, entry.Quantity, cancellationToken)
                    : change.RemoveAsync(entry.Row, entry.Quantity, endpoint, cancellationToken));
            }
        }

        await change.CompleteAsync(events, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "update content.inventories set status = 'applied', applied_at = @now where id = @inventoryId",
            new { inventoryId, now = change.Now },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        logger.InventoryApplied(inventoryId, inventory.LocationId, change.Movements.Count);
    }

    /// <summary>
    /// Compares the counted content with the current content, field by field, with the advanced fields that the owner sees.
    /// </summary>
    internal static IReadOnlyList<InventoryDifference> Differences(IReadOnlyList<EntryRow> counted, IReadOnlyList<EntryRow> current, AdvancedFields settings)
    {
        var expected = current
            .GroupBy(entry => settings.Output(entry.Fields))
            .ToDictionary(group => group.Key, group => (Line: new CardLine(group.Key, group.First().CardName, group.First().SetCode), Quantity: group.Sum(entry => entry.Quantity)));
        var found = counted
            .GroupBy(entry => settings.Output(entry.Fields))
            .ToDictionary(group => group.Key, group => (Line: new CardLine(group.Key, group.First().CardName, group.First().SetCode), Quantity: group.Sum(entry => entry.Quantity)));
        return [.. expected.Keys.Union(found.Keys)
            .Select(fields =>
            {
                var line = found.TryGetValue(fields, out var count) ? count.Line : expected[fields].Line;
                return new InventoryDifference(fields.Key, line, expected.GetValueOrDefault(fields).Quantity, found.GetValueOrDefault(fields).Quantity);
            })
            .Where(difference => difference.Delta != 0)
            .OrderBy(difference => difference.Line.CardName, StringComparer.Ordinal)
            .ThenBy(difference => difference.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Gives the answer of each difference: its own answer, or the answer for all the differences.
    /// </summary>
    private static List<(InventoryDifference Difference, MovementEndpoint Endpoint)> Plan(IReadOnlyList<InventoryDifference> differences, InventoryAnswers answers, Guid locationId)
    {
        var errors = new List<FieldError>();
        var plan = new List<(InventoryDifference, MovementEndpoint)>();
        foreach (var difference in differences)
        {
            var surplus = difference.Delta > 0;
            var endpoint = answers.ByKey.GetValueOrDefault(difference.Key) ?? (surplus ? answers.Origin : answers.Destination);
            var allowed = surplus ? CollectionRules.Origins : CollectionRules.Destinations;
            if (endpoint is null)
            {
                errors.Add(new FieldError("answers", FieldErrorCodes.Required, $"Give the {(surplus ? "origin" : "destination")} of the card \"{difference.Line.CardName}\" (key {difference.Key})."));
            }
            else if (!allowed.Contains(endpoint.Kind) || endpoint.LocationId == locationId)
            {
                errors.Add(new FieldError("answers", FieldErrorCodes.NotAllowed, $"The {(surplus ? "origin" : "destination")} of the card \"{difference.Line.CardName}\" must be one of these values: {string.Join(", ", allowed)}, and not the location of the inventory."));
            }
            else
            {
                plan.Add((difference, endpoint));
            }
        }

        if (errors.Count > 0)
        {
            throw CollectionException.Validation([.. errors]);
        }

        return plan;
    }

    /// <summary>
    /// Takes copies of the entries that have the fields of a difference, in the order of the entries.
    /// </summary>
    private static async Task TakeAsync(IReadOnlyList<EntryRow> entries, InventoryDifference difference, int quantity, AdvancedFields settings, Func<(EntryRow Row, int Quantity), Task> take)
    {
        var matching = entries.Where(entry => settings.Output(entry.Fields) == difference.Line.Fields).ToList();
        var available = matching.Sum(entry => entry.Quantity);
        if (available < quantity)
        {
            throw CollectionException.NotEnoughCards(difference.Line.CardName, available);
        }

        foreach (var entry in matching)
        {
            if (quantity == 0)
            {
                break;
            }

            var part = Math.Min(quantity, entry.Quantity);
            await take((entry, part));
            quantity -= part;
        }
    }

    private static async Task<InventoryRow> FindAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid ownerId, Guid inventoryId, CancellationToken cancellationToken, bool forUpdate = false) =>
        await connection.QuerySingleOrDefaultAsync<InventoryRow>(new CommandDefinition(
            $"select {InventoryColumns} from content.inventories i where i.id = @inventoryId and i.owner_id = @ownerId{(forUpdate ? " for update" : string.Empty)}",
            new { inventoryId, ownerId },
            transaction,
            cancellationToken: cancellationToken))
        ?? throw CollectionException.NotFound("The inventory does not exist.");

    private static async Task<IReadOnlyList<EntryRow>> CountedAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid inventoryId, CancellationToken cancellationToken) =>
        [.. await connection.QueryAsync<EntryRow>(new CommandDefinition(
            """
            select e.id, e.owner_id as OwnerId, e.oracle_id as OracleId, e.printing_id as PrintingId, e.card_name as CardName, e.set_code as SetCode,
                e.language, e.finish, e.condition, e.purchase_price as PurchasePrice, e.purchase_currency as PurchaseCurrency,
                e.acquired_on::timestamp as AcquiredOn, e.note, e.quantity
            from content.inventory_entries e
            where e.inventory_id = @inventoryId
            order by e.card_name, e.id
            """,
            new { inventoryId },
            transaction,
            cancellationToken: cancellationToken))];

    private static CollectionException Applied() =>
        new(StatusCodes.Status409Conflict, CollectionErrorCodes.InventoryApplied, "The inventory is applied. It cannot change. Start a new inventory.");
}
