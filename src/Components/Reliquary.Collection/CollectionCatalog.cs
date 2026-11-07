using Dapper;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// Gives the copies of the collection to the other core components. See <see cref="ICollectionCatalog"/>.
/// </summary>
internal sealed class CollectionCatalog(CollectionStore store) : ICollectionCatalog
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionCopy>> GetCopiesAsync(Guid ownerId, IReadOnlyCollection<Guid> oracleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oracleIds);
        if (oracleIds.Count == 0)
        {
            return [];
        }

        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        var rows = await connection.QueryAsync<CopyRow>(new CommandDefinition(
            """
            select e.location_id as LocationId, coalesce(p.name, l.name) as LocationName, case when p.id is null then null else l.name end as SectionName,
                l.kind as LocationKind, l.locked or coalesce(p.locked, false) as Locked, l.pickable and coalesce(p.pickable, true) as Pickable,
                coalesce(l.deck_id, p.deck_id) as DeckId, e.oracle_id as OracleId, e.printing_id as PrintingId, e.language,
                e.finish, e.condition, e.quantity
            from content.location_entries e
            join content.locations l on l.id = e.location_id
            left join content.locations p on p.id = l.parent_id
            where e.owner_id = @ownerId and e.oracle_id = any(@oracleIds)
            order by e.oracle_id, l.position, l.name, e.id
            """,
            new { ownerId, oracleIds = oracleIds.Distinct().ToArray() },
            cancellationToken: cancellationToken));
        var settings = await SettingsService.GetAsync(connection, ownerId, cancellationToken);
        return [.. rows.Select(row => new CollectionCopy(
            row.LocationId,
            row.LocationName,
            row.SectionName,
            row.LocationKind,
            row.Locked,
            row.Pickable,
            row.DeckId,
            row.OracleId,
            row.PrintingId,
            row.Language,
            settings.Finish ? row.Finish : null,
            settings.Condition ? row.Condition : null,
            row.Quantity))];
    }

    private sealed class CopyRow
    {
        public Guid LocationId { get; set; }

        public string LocationName { get; set; } = string.Empty;

        public string? SectionName { get; set; }

        public string LocationKind { get; set; } = string.Empty;

        public bool Locked { get; set; }

        public bool Pickable { get; set; }

        public Guid? DeckId { get; set; }

        public Guid OracleId { get; set; }

        public Guid? PrintingId { get; set; }

        public string Language { get; set; } = "en";

        public string? Finish { get; set; }

        public string? Condition { get; set; }

        public int Quantity { get; set; }
    }
}
