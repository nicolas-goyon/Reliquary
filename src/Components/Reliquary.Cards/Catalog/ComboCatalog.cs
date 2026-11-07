using Dapper;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Cards.Catalog;

/// <summary>
/// Gives the combos of Commander Spellbook to the other core components. See <see cref="IComboCatalog"/>.
/// </summary>
/// <param name="database">The databases of the instance.</param>
internal sealed class ComboCatalog(IDatabase database) : IComboCatalog
{
    /// <inheritdoc />
    public async Task<ComboSearch> FindCombosAsync(IReadOnlyCollection<Guid> oracleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oracleIds);
        await using var connection = await database.OpenReferenceAsync(cancellationToken);

        // The instance has combo data when the import kept at least one combo.
        var available = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from reference.combos)",
            cancellationToken: cancellationToken));
        if (!available || oracleIds.Count == 0)
        {
            return new ComboSearch(available, []);
        }

        var ids = oracleIds.Distinct().ToArray();
        var rows = await connection.QueryAsync<ComboRow>(new CommandDefinition(
            """
            select id, oracle_ids as OracleIds, quantities, results, game_ending as GameEnding,
                mana_needed as ManaNeeded, mana_value_needed as ManaValueNeeded, popularity
            from reference.combos c
            where c.oracle_ids && @ids
                and (select count(*) from unnest(c.oracle_ids) as card(id) where card.id <> all (@ids)) <= 1
            order by c.popularity desc nulls last, c.id
            """,
            new { ids },
            cancellationToken: cancellationToken));

        return new ComboSearch(true, [.. rows.Select(row => new ComboFacts(
            row.Id,
            [.. row.OracleIds.Select((oracleId, index) => new ComboCard(oracleId, index < row.Quantities.Length ? row.Quantities[index] : 1))],
            row.Results,
            row.GameEnding,
            row.ManaNeeded,
            row.ManaValueNeeded,
            row.Popularity))]);
    }

    private sealed class ComboRow
    {
        public string Id { get; set; } = string.Empty;

        public Guid[] OracleIds { get; set; } = [];

        public int[] Quantities { get; set; } = [];

        public string[] Results { get; set; } = [];

        public bool GameEnding { get; set; }

        public string? ManaNeeded { get; set; }

        public int ManaValueNeeded { get; set; }

        public int? Popularity { get; set; }
    }
}
