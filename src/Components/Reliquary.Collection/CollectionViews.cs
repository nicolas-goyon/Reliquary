using Dapper;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// The filters and the page of the global view.
/// </summary>
/// <param name="LocationIds">Only the copies of these locations, or <see langword="null"/> for all the locations.</param>
/// <param name="Search">A part of the card name, or <see langword="null"/>.</param>
/// <param name="Language">Only the copies in this language, or <see langword="null"/>.</param>
/// <param name="SetCode">Only the copies of a printing of this set, or <see langword="null"/>.</param>
/// <param name="AfterName">The name of the last card of the previous page, or <see langword="null"/>.</param>
/// <param name="AfterId">The Oracle ID of the last card of the previous page, or <see langword="null"/>.</param>
/// <param name="Limit">The maximum number of cards.</param>
internal sealed record CardViewQuery(
    IReadOnlyList<Guid>? LocationIds,
    string? Search,
    string? Language,
    string? SetCode,
    string? AfterName,
    Guid? AfterId,
    int Limit);

/// <summary>
/// One Oracle card of the global view, with the number of copies in each location.
/// </summary>
internal sealed record CardView(Guid OracleId, string Name, int Quantity, IReadOnlyList<CardViewLocation> Locations);

/// <summary>
/// The copies of one card in one location or section.
/// </summary>
internal sealed record CardViewLocation(Guid LocationId, string LocationName, string? SectionName, string Kind, int Quantity);

/// <summary>
/// The global view of the collection: all the copies, grouped by Oracle card, with their locations.
/// The locations of a card come in the order of the location list, with the assembled decks at the end.
/// </summary>
/// <remarks>
/// The page uses the index on <c>(owner_id, card_name, oracle_id)</c> and a keyset on the name and the Oracle ID.
/// A collection of 50,000 entries gives its first page in less than 500 ms.
/// </remarks>
internal sealed class CollectionViews(CollectionStore store)
{
    private const string Filters = """
        e.owner_id = @ownerId
            and (@locationIds::uuid[] is null or e.location_id = any(@locationIds))
            and (@language::text is null or e.language = @language)
            and (@setCode::text is null or e.set_code = @setCode)
            and (@search::text is null or e.card_name ilike @search)
        """;

    public async Task<IReadOnlyList<CardView>> ListCardsAsync(Guid ownerId, CardViewQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        var parameters = new
        {
            ownerId,
            locationIds = query.LocationIds?.ToArray(),
            language = query.Language,
            setCode = query.SetCode?.ToLowerInvariant(),
            search = EntryService.Like(query.Search),
            afterName = query.AfterName,
            afterId = query.AfterId ?? Guid.Empty,
            limit = query.Limit,
        };
        var cards = (await connection.QueryAsync<(Guid OracleId, string Name, int Quantity)>(new CommandDefinition(
            $"""
            select e.oracle_id, e.card_name, sum(e.quantity)::int
            from content.location_entries e
            where {Filters}
                and (@afterName::text is null or (e.card_name, e.oracle_id) > (@afterName, @afterId))
            group by e.card_name, e.oracle_id
            order by e.card_name, e.oracle_id
            limit @limit
            """,
            parameters,
            cancellationToken: cancellationToken))).ToList();
        if (cards.Count == 0)
        {
            return [];
        }

        var copies = (await connection.QueryAsync<(Guid OracleId, Guid LocationId, string LocationName, string? SectionName, string Kind, int Quantity)>(new CommandDefinition(
            $"""
            select e.oracle_id, e.location_id, coalesce(p.name, l.name), case when p.id is null then null else l.name end, l.kind, sum(e.quantity)::int
            from content.location_entries e
            join content.locations l on l.id = e.location_id
            left join content.locations p on p.id = l.parent_id
            where {Filters} and e.oracle_id = any(@oracleIds)
            group by e.oracle_id, e.location_id, l.name, l.kind, l.position, p.id, p.name, p.position
            order by e.oracle_id, l.kind = 'deck', coalesce(p.position, l.position), coalesce(p.name, l.name), p.id nulls first, l.position, l.name
            """,
            new
            {
                parameters.ownerId,
                parameters.locationIds,
                parameters.language,
                parameters.setCode,
                parameters.search,
                oracleIds = cards.Select(card => card.OracleId).ToArray(),
            },
            cancellationToken: cancellationToken))).ToLookup(copy => copy.OracleId);
        return [.. cards.Select(card => new CardView(
            card.OracleId,
            card.Name,
            card.Quantity,
            [.. copies[card.OracleId].Select(copy => new CardViewLocation(copy.LocationId, copy.LocationName, copy.SectionName, copy.Kind, copy.Quantity))]))];
    }
}
