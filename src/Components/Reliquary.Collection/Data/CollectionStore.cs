using Dapper;
using Npgsql;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Collection.Data;

/// <summary>
/// Reads and writes the collection in the shards of the owners.
/// </summary>
/// <param name="database">The databases of the instance.</param>
internal sealed class CollectionStore(IDatabase database)
{
    public const string LocationColumns = """
        l.id, l.owner_id as OwnerId, l.parent_id as ParentId, l.name, l.kind, l.locked, l.pickable, l.deck_id as DeckId,
        l.position, l.created_at as CreatedAt, l.updated_at as UpdatedAt,
        p.name as ParentName, coalesce(p.locked, false) as ParentLocked, coalesce(p.pickable, true) as ParentPickable
        """;

    public const string EntryColumns = """
        e.id, e.owner_id as OwnerId, e.location_id as LocationId, e.oracle_id as OracleId, e.printing_id as PrintingId,
        e.card_name as CardName, e.set_code as SetCode, e.language, e.finish, e.condition, e.purchase_price as PurchasePrice,
        e.purchase_currency as PurchaseCurrency, e.acquired_on::timestamp as AcquiredOn, e.note, e.quantity,
        e.created_at as CreatedAt, e.updated_at as UpdatedAt
        """;

    /// <summary>The condition that finds the entry with the same card fields in a location.</summary>
    private const string SameFields = """
        e.location_id = @locationId and e.oracle_id = @oracleId and e.printing_id is not distinct from @printingId
        and e.language = @language and e.finish is not distinct from @finish and e.condition is not distinct from @condition
        and e.purchase_price is not distinct from @purchasePrice and e.purchase_currency is not distinct from @purchaseCurrency
        and e.acquired_on is not distinct from @acquiredOn::date and e.note is not distinct from @note
        """;

    public ValueTask<NpgsqlConnection> OpenAsync(Guid ownerId, ContentAccess access, CancellationToken cancellationToken) =>
        database.OpenContentAsync(ownerId, access, cancellationToken);

    /// <summary>Opens the users database, for the collection shares.</summary>
    public ValueTask<NpgsqlConnection> OpenUsersAsync(CancellationToken cancellationToken) =>
        database.OpenUsersAsync(cancellationToken);

    /// <summary>
    /// Finds a location of an owner. With <paramref name="forUpdate"/>, the row lock stops other changes of the location until the transaction ends.
    /// </summary>
    public static Task<LocationRow?> FindLocationAsync(NpgsqlConnection connection, Guid ownerId, Guid locationId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null, bool forUpdate = false) =>
        connection.QuerySingleOrDefaultAsync<LocationRow>(new CommandDefinition(
            $"""
            select {LocationColumns}
            from content.locations l left join content.locations p on p.id = l.parent_id
            where l.id = @locationId and l.owner_id = @ownerId
            {(forUpdate ? "for update of l" : string.Empty)}
            """,
            new { locationId, ownerId },
            transaction,
            cancellationToken: cancellationToken));

    public static Task<LocationRow?> FindDeckLocationAsync(NpgsqlConnection connection, Guid ownerId, Guid deckId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null, bool forUpdate = false) =>
        connection.QuerySingleOrDefaultAsync<LocationRow>(new CommandDefinition(
            $"""
            select {LocationColumns}
            from content.locations l left join content.locations p on p.id = l.parent_id
            where l.deck_id = @deckId and l.owner_id = @ownerId
            {(forUpdate ? "for update of l" : string.Empty)}
            """,
            new { deckId, ownerId },
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>Lists the sections of a location.</summary>
    public static async Task<IReadOnlyList<LocationRow>> ListSectionsAsync(NpgsqlConnection connection, Guid ownerId, Guid locationId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        [.. await connection.QueryAsync<LocationRow>(new CommandDefinition(
            $"""
            select {LocationColumns}
            from content.locations l left join content.locations p on p.id = l.parent_id
            where l.parent_id = @locationId and l.owner_id = @ownerId
            order by l.position, lower(l.name), l.id
            """,
            new { locationId, ownerId },
            transaction,
            cancellationToken: cancellationToken))];

    /// <summary>Lists all the locations of an owner with their number of cards, in the order of the list: position, then name.</summary>
    public static async Task<IReadOnlyList<LocationSummaryRow>> ListLocationsAsync(NpgsqlConnection connection, Guid ownerId, CancellationToken cancellationToken) =>
        [.. await connection.QueryAsync<LocationSummaryRow>(new CommandDefinition(
            """
            select l.id, l.parent_id as ParentId, l.name, l.kind, l.locked, l.pickable, l.deck_id as DeckId, l.position,
                l.created_at as CreatedAt, l.updated_at as UpdatedAt,
                coalesce(s.cards, 0) as CardCount, coalesce(s.entries, 0) as EntryCount
            from content.locations l
            left join (
                select location_id, sum(quantity) as cards, count(*) as entries
                from content.location_entries where owner_id = @ownerId group by location_id
            ) s on s.location_id = l.id
            where l.owner_id = @ownerId
            order by l.position, lower(l.name), l.id
            """,
            new { ownerId },
            cancellationToken: cancellationToken))];

    public static Task<int> CountLocationsAsync(NpgsqlConnection connection, Guid ownerId, NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*)::int from content.locations where owner_id = @ownerId", new { ownerId }, transaction, cancellationToken: cancellationToken));

    public static Task InsertLocationAsync(NpgsqlTransaction transaction, LocationRow location, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            insert into content.locations (id, owner_id, parent_id, name, kind, locked, pickable, deck_id, position, created_at, updated_at)
            values (@Id, @OwnerId, @ParentId, @Name, @Kind, @Locked, @Pickable, @DeckId, @Position, @CreatedAt, @UpdatedAt)
            """,
            location,
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>Writes the name, the kind, the flags and the position of a location. The sections get the kind of their parent location.</summary>
    public static async Task UpdateLocationAsync(NpgsqlTransaction transaction, LocationRow location, CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            update content.locations
            set name = @Name, kind = @Kind, locked = @Locked, pickable = @Pickable, position = @Position, updated_at = @UpdatedAt
            where id = @Id and owner_id = @OwnerId;
            update content.locations set kind = @Kind where parent_id = @Id and owner_id = @OwnerId;
            """,
            location,
            transaction,
            cancellationToken: cancellationToken));
    }

    public static Task DeleteLocationAsync(NpgsqlTransaction transaction, Guid ownerId, Guid locationId, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "delete from content.locations where id = @locationId and owner_id = @ownerId",
            new { locationId, ownerId },
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>Counts the cards of a location and of its sections.</summary>
    public static Task<long> CountCardsAsync(NpgsqlConnection connection, Guid ownerId, Guid locationId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            select coalesce(sum(e.quantity), 0)
            from content.location_entries e join content.locations l on l.id = e.location_id
            where e.owner_id = @ownerId and (l.id = @locationId or l.parent_id = @locationId)
            """,
            new { ownerId, locationId },
            transaction,
            cancellationToken: cancellationToken));

    public static Task<EntryRow?> FindEntryAsync(NpgsqlConnection connection, Guid ownerId, Guid entryId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        connection.QuerySingleOrDefaultAsync<EntryRow>(new CommandDefinition(
            $"select {EntryColumns} from content.location_entries e where e.id = @entryId and e.owner_id = @ownerId",
            new { entryId, ownerId },
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>Lists all the entries of some locations, by card name.</summary>
    public static async Task<IReadOnlyList<EntryRow>> ListEntriesAsync(NpgsqlConnection connection, Guid ownerId, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        [.. await connection.QueryAsync<EntryRow>(new CommandDefinition(
            $"""
            select {EntryColumns}
            from content.location_entries e
            where e.owner_id = @ownerId and e.location_id = any(@locationIds)
            order by e.card_name, e.id
            """,
            new { ownerId, locationIds = locationIds.ToArray() },
            transaction,
            cancellationToken: cancellationToken))];

    /// <summary>Finds the entry of a location with the same card fields.</summary>
    public static Task<EntryRow?> FindSameEntryAsync(NpgsqlTransaction transaction, Guid locationId, CardFields fields, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<EntryRow>(new CommandDefinition(
            $"select {EntryColumns} from content.location_entries e where {SameFields}",
            FieldParameters(locationId, fields),
            transaction,
            cancellationToken: cancellationToken));

    public static Task InsertEntryAsync(NpgsqlTransaction transaction, EntryRow entry, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            insert into content.location_entries (
                id, owner_id, location_id, oracle_id, printing_id, card_name, set_code, language, finish, condition,
                purchase_price, purchase_currency, acquired_on, note, quantity, created_at, updated_at)
            values (
                @Id, @OwnerId, @LocationId, @OracleId, @PrintingId, @CardName, @SetCode, @Language, @Finish, @Condition,
                @PurchasePrice, @PurchaseCurrency, @AcquiredOnText::date, @Note, @Quantity, @CreatedAt, @UpdatedAt)
            """,
            entry,
            transaction,
            cancellationToken: cancellationToken));

    public static Task SetQuantityAsync(NpgsqlTransaction transaction, Guid entryId, int quantity, DateTime now, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            quantity > 0
                ? "update content.location_entries set quantity = @quantity, updated_at = @now where id = @entryId"
                : "delete from content.location_entries where id = @entryId",
            new { entryId, quantity, now },
            transaction,
            cancellationToken: cancellationToken));

    public static Task InsertMovementsAsync(NpgsqlTransaction transaction, IEnumerable<MovementRow> movements, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            insert into content.movements (
                id, owner_id, batch_id, kind, origin_kind, origin_location_id, destination_kind, destination_location_id,
                oracle_id, printing_id, card_name, language, finish, condition, purchase_price, purchase_currency, acquired_on, note,
                quantity, inventory_id, created_at)
            values (
                @Id, @OwnerId, @BatchId, @Kind, @OriginKind, @OriginLocationId, @DestinationKind, @DestinationLocationId,
                @OracleId, @PrintingId, @CardName, @Language, @Finish, @Condition, @PurchasePrice, @PurchaseCurrency, @AcquiredOnText::date, @Note,
                @Quantity, @InventoryId, @CreatedAt)
            """,
            movements,
            transaction,
            cancellationToken: cancellationToken));

    private static DynamicParameters FieldParameters(Guid locationId, CardFields fields)
    {
        var parameters = new DynamicParameters();
        parameters.Add("locationId", locationId);
        parameters.Add("oracleId", fields.OracleId);
        parameters.Add("printingId", fields.PrintingId, System.Data.DbType.Guid);
        parameters.Add("language", fields.Language);
        parameters.Add("finish", fields.Finish, System.Data.DbType.String);
        parameters.Add("condition", fields.Condition, System.Data.DbType.String);
        parameters.Add("purchasePrice", fields.PurchasePrice, System.Data.DbType.Decimal);
        parameters.Add("purchaseCurrency", fields.PurchaseCurrency, System.Data.DbType.String);
        parameters.Add("acquiredOn", fields.AcquiredOnText, System.Data.DbType.String);
        parameters.Add("note", fields.Note, System.Data.DbType.String);
        return parameters;
    }
}
