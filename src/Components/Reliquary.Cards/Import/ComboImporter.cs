using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Cards.Import.Spellbook;

namespace Reliquary.Cards.Import;

/// <summary>
/// The result of the import of the combos.
/// </summary>
/// <param name="Read">The number of combos in the file.</param>
/// <param name="Kept">The number of combos that the import keeps: all their cards are known Oracle cards.</param>
/// <param name="Templates">The number of combos that the import skips because they need a template, for example "a creature with a sacrifice ability".</param>
/// <param name="UnknownCards">The number of combos that the import skips because one or more cards are not known Oracle cards.</param>
/// <param name="Changed">The number of combos that the import added or changed.</param>
/// <param name="Deleted">The number of combos that the import deleted.</param>
internal sealed record ComboImportResult(int Read, int Kept, int Templates, int UnknownCards, int Changed, int Deleted);

/// <summary>
/// Imports the combos of the bulk file of Commander Spellbook into <c>reference.combos</c>. See ADR 0030.
/// </summary>
/// <remarks>
/// The import keeps a combo only when it needs no template and all its cards are known Oracle cards.
/// It finds a card by its Oracle ID. When the file gives no Oracle ID, it finds the card by its Oracle name.
/// The import changes only the rows that changed, so a second import of the same file changes nothing.
/// </remarks>
/// <param name="database">The databases.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The logger.</param>
internal sealed class ComboImporter(IDatabase database, TimeProvider time, ILogger<ComboImporter> logger)
{
    /// <summary>The import deletes the missing combos only when the file contains at least this part of the current combos.</summary>
    public const double MinimumShareForDeletion = 0.9;

    private const int CommandTimeoutSeconds = 1800;

    private static readonly CopyTable<ComboRow> StagingTable = new("reference.import_combos",
    [
        new("id", NpgsqlDbType.Text, row => row.Id),
        new("oracle_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, row => row.OracleIds),
        new("quantities", NpgsqlDbType.Array | NpgsqlDbType.Integer, row => row.Quantities),
        new("results", NpgsqlDbType.Array | NpgsqlDbType.Text, row => row.Results),
        new("game_ending", NpgsqlDbType.Boolean, row => row.GameEnding),
        new("mana_needed", NpgsqlDbType.Text, row => row.ManaNeeded),
        new("mana_value_needed", NpgsqlDbType.Integer, row => row.ManaValueNeeded),
        new("popularity", NpgsqlDbType.Integer, row => row.Popularity),
    ]);

    /// <summary>
    /// Imports the combos of a bulk file of Commander Spellbook.
    /// </summary>
    /// <param name="content">The content of <c>variants.json</c>, without compression.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The result of the import.</returns>
    /// <exception cref="JsonException">The file is not a bulk file of Commander Spellbook.</exception>
    public async Task<ComboImportResult> ImportAsync(Stream content, CancellationToken cancellationToken)
    {
        var file = await JsonSerializer.DeserializeAsync(content, SpellbookJson.Default.SpellbookFile, cancellationToken)
            ?? throw new JsonException("The combo file contains no object.");

        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        var cards = await KnownCardsAsync(connection, file.Variants, cancellationToken);

        var rows = new List<ComboRow>();
        int templates = 0, unknownCards = 0;
        foreach (var combo in file.Variants.Where(combo => combo.Status is null or "OK" or "E").DistinctBy(combo => combo.Id, StringComparer.Ordinal))
        {
            if (combo.Requires.Count > 0)
            {
                templates++;
                continue;
            }

            if (Map(combo, cards) is { } row)
            {
                rows.Add(row);
            }
            else
            {
                unknownCards++;
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("truncate reference.import_combos", transaction: transaction, cancellationToken: cancellationToken));
        await StagingTable.WriteAllAsync(connection, rows, cancellationToken);
        var existing = await connection.ExecuteScalarAsync<long>(new CommandDefinition("select count(*) from reference.combos", transaction: transaction, cancellationToken: cancellationToken));

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reference.combos (id, oracle_ids, quantities, results, game_ending, mana_needed, mana_value_needed, popularity, updated_at)
            select id, oracle_ids, quantities, results, game_ending, mana_needed, mana_value_needed, popularity, @now
            from reference.import_combos
            on conflict (id) do update set
                oracle_ids = excluded.oracle_ids, quantities = excluded.quantities, results = excluded.results,
                game_ending = excluded.game_ending, mana_needed = excluded.mana_needed,
                mana_value_needed = excluded.mana_value_needed, popularity = excluded.popularity, updated_at = excluded.updated_at
            where (combos.oracle_ids, combos.quantities, combos.results, combos.game_ending, combos.mana_needed, combos.mana_value_needed, combos.popularity)
                is distinct from
                (excluded.oracle_ids, excluded.quantities, excluded.results, excluded.game_ending, excluded.mana_needed, excluded.mana_value_needed, excluded.popularity)
            """,
            new { now = time.GetUtcNow() },
            transaction,
            CommandTimeoutSeconds,
            cancellationToken: cancellationToken));

        // A truncated file, or an import before the card data, must not delete the combos.
        var deleted = 0;
        if (rows.Count >= existing * MinimumShareForDeletion)
        {
            deleted = await connection.ExecuteAsync(new CommandDefinition(
                "delete from reference.combos c where not exists (select 1 from reference.import_combos s where s.id = c.id)",
                transaction: transaction,
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: cancellationToken));
        }
        else
        {
            logger.CombosNotDeleted(rows.Count, existing);
        }

        await connection.ExecuteAsync(new CommandDefinition("truncate reference.import_combos", transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("analyze reference.combos", cancellationToken: cancellationToken));

        var result = new ComboImportResult(file.Variants.Count, rows.Count, templates, unknownCards, changed, deleted);
        logger.CombosImported(result.Read, result.Kept, result.Templates, result.UnknownCards);
        return result;
    }

    /// <summary>
    /// Tells whether a combo is game-ending: one result is infinite or near-infinite,
    /// or Commander Spellbook marks a result as standalone (status <c>S</c>), for example "Win the game".
    /// </summary>
    /// <param name="results">The results of the combo.</param>
    /// <returns><see langword="true"/> for a game-ending combo.</returns>
    internal static bool IsGameEnding(IEnumerable<SpellbookFeature> results) =>
        results.Any(feature => feature.Status == "S" || feature.Name.Contains("infinite", StringComparison.OrdinalIgnoreCase));

    private static ComboRow? Map(SpellbookCombo combo, KnownCards cards)
    {
        if (combo.Uses.Count == 0)
        {
            return null;
        }

        var oracleIds = new List<Guid>();
        var quantities = new Dictionary<Guid, int>();
        foreach (var use in combo.Uses)
        {
            var oracleId = use.Card.OracleId is { } id
                ? cards.Ids.Contains(id) ? id : (Guid?)null
                : cards.ByName.GetValueOrDefault(use.Card.Name);
            if (oracleId is not { } known)
            {
                return null;
            }

            if (!quantities.ContainsKey(known))
            {
                oracleIds.Add(known);
            }

            quantities[known] = quantities.GetValueOrDefault(known) + Math.Max(1, use.Quantity);
        }

        var features = combo.Produces.Select(produces => produces.Feature).Where(feature => feature.Name.Length > 0).ToList();
        return new ComboRow(
            combo.Id,
            [.. oracleIds],
            [.. oracleIds.Select(id => quantities[id])],
            [.. features.Select(feature => feature.Name).Distinct(StringComparer.Ordinal)],
            IsGameEnding(features),
            string.IsNullOrEmpty(combo.ManaNeeded) ? null : combo.ManaNeeded,
            Math.Max(0, combo.ManaValueNeeded ?? 0),
            combo.Popularity);
    }

    private static async Task<KnownCards> KnownCardsAsync(Npgsql.NpgsqlConnection connection, List<SpellbookCombo> combos, CancellationToken cancellationToken)
    {
        var uses = combos.SelectMany(combo => combo.Uses).Select(use => use.Card).ToList();
        var oracleIds = uses.Select(card => card.OracleId).OfType<Guid>().Distinct().ToArray();
        var names = uses.Where(card => card.OracleId is null && card.Name.Length > 0).Select(card => card.Name).Distinct(StringComparer.Ordinal).ToArray();

        var ids = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "select id from reference.oracle_cards where id = any(@oracleIds) and not is_extra",
            new { oracleIds },
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: cancellationToken))).ToHashSet();

        var byName = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (names.Length > 0)
        {
            // A name that two Oracle cards share is ambiguous, so the import does not use it.
            var matches = await connection.QueryAsync<(string Name, Guid Id)>(new CommandDefinition(
                """
                select name, min(id::text)::uuid from reference.oracle_cards
                where name = any(@names) and not is_extra
                group by name having count(*) = 1
                """,
                new { names },
                cancellationToken: cancellationToken));
            foreach (var (name, id) in matches)
            {
                byName[name] = id;
            }
        }

        return new KnownCards(ids, byName);
    }

    private sealed record KnownCards(HashSet<Guid> Ids, Dictionary<string, Guid> ByName);

    private sealed record ComboRow(
        string Id,
        Guid[] OracleIds,
        int[] Quantities,
        string[] Results,
        bool GameEnding,
        string? ManaNeeded,
        int ManaValueNeeded,
        int? Popularity);
}
