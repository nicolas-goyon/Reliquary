using System.Globalization;
using System.Text;
using Dapper;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// Gives the data of the Collection component for the export of an account, and deletes it for the deletion of an account.
/// </summary>
/// <remarks>
/// The archive contains JSON files with all the fields, and one text file for each location in the deck text format.
/// The text files do not contain the language and the advanced fields.
/// </remarks>
internal sealed class CollectionPersonalData(CollectionStore store, ICardCatalog catalog) : IPersonalDataProvider
{
    public string Name => "collection";

    public async Task ExportAsync(Guid ownerId, IPersonalDataArchive archive, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        var locations = await CollectionStore.ListLocationsAsync(connection, ownerId, cancellationToken);
        var entries = await CollectionStore.ListEntriesAsync(connection, ownerId, [.. locations.Select(location => location.Id)], cancellationToken);
        var printings = await catalog.GetPrintingsAsync([.. entries.Where(entry => entry.PrintingId is not null).Select(entry => entry.PrintingId!.Value).Distinct()], cancellationToken);
        var entriesByLocation = entries.ToLookup(entry => entry.LocationId);

        object Entries(Guid locationId) => entriesByLocation[locationId].Select(entry => new
        {
            entry.OracleId,
            entry.CardName,
            entry.PrintingId,
            entry.SetCode,
            CollectorNumber = entry.PrintingId is { } id ? printings.GetValueOrDefault(id)?.CollectorNumber : null,
            entry.Language,
            entry.Quantity,
            entry.Finish,
            entry.Condition,
            entry.PurchasePrice,
            entry.PurchaseCurrency,
            AcquiredOn = entry.Fields.AcquiredOn,
            entry.Note,
            entry.CreatedAt,
            entry.UpdatedAt,
        }).ToList();

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exported = new List<object>();
        foreach (var location in locations.Where(location => location.ParentId is null))
        {
            var sections = locations.Where(section => section.ParentId == location.Id).ToList();
            var stem = FileName(location.Name);
            var fileName = $"{stem}.txt";
            for (var number = 2; !usedNames.Add(fileName); number++)
            {
                fileName = $"{stem} ({number}).txt";
            }

            await archive.WriteTextAsync($"text/{fileName}", Text(location.Id, sections, entriesByLocation, printings), cancellationToken);
            exported.Add(new
            {
                location.Id,
                location.Name,
                location.Kind,
                location.Locked,
                location.Pickable,
                location.DeckId,
                location.Position,
                location.CreatedAt,
                location.UpdatedAt,
                TextFile = $"text/{fileName}",
                Entries = Entries(location.Id),
                Sections = sections.Select(section => new
                {
                    section.Id,
                    section.Name,
                    section.Locked,
                    section.Pickable,
                    section.Position,
                    section.CreatedAt,
                    section.UpdatedAt,
                    Entries = Entries(section.Id),
                }).ToList(),
            });
        }

        await archive.WriteJsonAsync("locations.json", exported, cancellationToken);

        var settings = await connection.QuerySingleOrDefaultAsync<string[]>(new CommandDefinition(
            "select advanced_fields from content.collection_settings where owner_id = @ownerId", new { ownerId }, cancellationToken: cancellationToken));
        await archive.WriteJsonAsync("settings.json", new { AdvancedFields = settings ?? [] }, cancellationToken);

        var movements = await connection.QueryAsync(new CommandDefinition(
            """
            select id, batch_id as "batchId", kind, origin_kind as "originKind", origin_location_id as "originLocationId",
                destination_kind as "destinationKind", destination_location_id as "destinationLocationId", oracle_id as "oracleId",
                printing_id as "printingId", card_name as "cardName", language, finish, condition, purchase_price as "purchasePrice",
                purchase_currency as "purchaseCurrency", to_char(acquired_on, 'YYYY-MM-DD') as "acquiredOn", note, quantity,
                inventory_id as "inventoryId", created_at as "createdAt"
            from content.movements where owner_id = @ownerId order by created_at, id
            """,
            new { ownerId },
            cancellationToken: cancellationToken));
        await archive.WriteJsonAsync("movements.json", movements.Select(Row).ToList(), cancellationToken);

        var inventories = (await connection.QueryAsync(new CommandDefinition(
            """
            select id, location_id as "locationId", status, created_at as "createdAt", applied_at as "appliedAt"
            from content.inventories where owner_id = @ownerId order by created_at, id
            """,
            new { ownerId },
            cancellationToken: cancellationToken))).Select(Row).ToList();
        var inventoryEntries = (await connection.QueryAsync(new CommandDefinition(
            """
            select inventory_id as "inventoryId", oracle_id as "oracleId", card_name as "cardName", printing_id as "printingId", set_code as "setCode",
                language, finish, condition, purchase_price as "purchasePrice", purchase_currency as "purchaseCurrency",
                to_char(acquired_on, 'YYYY-MM-DD') as "acquiredOn", note, quantity
            from content.inventory_entries where owner_id = @ownerId order by card_name, id
            """,
            new { ownerId },
            cancellationToken: cancellationToken))).Select(Row).ToLookup(row => row["inventoryId"]);
        await archive.WriteJsonAsync(
            "inventories.json",
            inventories.Select(inventory => new Dictionary<string, object?>(inventory) { ["entries"] = inventoryEntries[inventory["id"]].ToList() }).ToList(),
            cancellationToken);

        var rules = await connection.QueryAsync(new CommandDefinition(
            """
            select id, location_id as "locationId", query, priority, created_at as "createdAt", updated_at as "updatedAt"
            from content.storage_rules where owner_id = @ownerId order by priority, id
            """,
            new { ownerId },
            cancellationToken: cancellationToken));
        await archive.WriteJsonAsync("storage-rules.json", rules.Select(Row).ToList(), cancellationToken);

        var tokens = await connection.QueryAsync(new CommandDefinition(
            """
            select location_id as "locationId", oracle_id as "oracleId", owned, updated_at as "updatedAt"
            from content.deck_tokens where owner_id = @ownerId order by location_id, oracle_id
            """,
            new { ownerId },
            cancellationToken: cancellationToken));
        await archive.WriteJsonAsync("deck-tokens.json", tokens.Select(Row).ToList(), cancellationToken);

        await using var users = await store.OpenUsersAsync(cancellationToken);
        var shares = await users.QueryAsync(new CommandDefinition(
            """
            select id, owner_id as "ownerId", guest_id as "guestId", location_ids as "locationIds", rights, created_at as "createdAt"
            from users.collection_shares where owner_id = @ownerId or guest_id = @ownerId order by created_at, id
            """,
            new { ownerId },
            cancellationToken: cancellationToken));
        await archive.WriteJsonAsync("shares.json", shares.Select(Row).ToList(), cancellationToken);
    }

    public async Task DeleteAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using (var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                delete from content.deck_tokens where owner_id = @ownerId;
                delete from content.storage_rules where owner_id = @ownerId;
                delete from content.inventory_entries where owner_id = @ownerId;
                delete from content.inventories where owner_id = @ownerId;
                delete from content.movements where owner_id = @ownerId;
                delete from content.location_entries where owner_id = @ownerId;
                delete from content.locations where owner_id = @ownerId;
                delete from content.collection_settings where owner_id = @ownerId;
                """,
                new { ownerId },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await using var users = await store.OpenUsersAsync(cancellationToken);
        await users.ExecuteAsync(new CommandDefinition(
            "delete from users.collection_shares where owner_id = @ownerId or guest_id = @ownerId",
            new { ownerId },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Writes the cards of a location and of its sections in the deck text format. A section starts with a comment line.
    /// </summary>
    private static string Text(Guid locationId, IReadOnlyList<LocationSummaryRow> sections, ILookup<Guid, EntryRow> entries, IReadOnlyDictionary<Guid, PrintingFacts> printings)
    {
        var text = new StringBuilder();
        void Write(IEnumerable<EntryRow> rows)
        {
            foreach (var entry in rows)
            {
                text.Append(entry.Quantity.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(entry.CardName);
                if (entry.SetCode is { } set)
                {
                    text.Append(" (").Append(set.ToUpperInvariant()).Append(')');
                    if (entry.PrintingId is { } id && printings.GetValueOrDefault(id) is { } printing)
                    {
                        text.Append(' ').Append(printing.CollectorNumber);
                    }
                }

                text.Append(entry.Finish switch { "foil" => " *F*", "etched" => " *E*", _ => string.Empty }).Append('\n');
            }
        }

        Write(entries[locationId]);
        foreach (var section in sections)
        {
            text.Append('\n').Append("// ").Append(section.Name).Append('\n');
            Write(entries[section.Id]);
        }

        return text.ToString();
    }

    private static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var clean = new string([.. name.Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character)]).Trim().TrimEnd('.');
        return clean.Length == 0 ? "location" : clean;
    }

    private static Dictionary<string, object?> Row(dynamic row) => new((IDictionary<string, object?>)row);
}
