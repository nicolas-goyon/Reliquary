using Dapper;
using Npgsql;

namespace Reliquary.Decks.Data;

/// <summary>
/// Reads and writes the category rules of the users in the table <c>content.category_rules</c>. See ADR 0031.
/// </summary>
internal static class CategoryRuleStore
{
    /// <summary>
    /// Gets the rules of an owner for some Oracle cards.
    /// </summary>
    /// <returns>The categories of each card that has a rule, by Oracle ID.</returns>
    public static async Task<IReadOnlyDictionary<Guid, string[]>> ListAsync(NpgsqlConnection connection, Guid ownerId, IReadOnlyCollection<Guid> oracleIds, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null)
    {
        if (oracleIds.Count == 0)
        {
            return new Dictionary<Guid, string[]>();
        }

        var rows = await connection.QueryAsync<(Guid OracleId, string[] Categories)>(new CommandDefinition(
            "select oracle_id, categories from content.category_rules where owner_id = @ownerId and oracle_id = any(@ids)",
            new { ownerId, ids = oracleIds.Distinct().ToArray() },
            transaction,
            cancellationToken: cancellationToken));
        return rows.ToDictionary(row => row.OracleId, row => row.Categories);
    }

    /// <summary>
    /// Writes the rule of an owner for an Oracle card. The rule replaces the earlier rule of the card.
    /// </summary>
    public static Task UpsertAsync(NpgsqlTransaction transaction, Guid ownerId, Guid oracleId, IReadOnlyCollection<string> categories, DateTime now, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            insert into content.category_rules (owner_id, oracle_id, categories, updated_at)
            values (@ownerId, @oracleId, @categories, @now)
            on conflict (owner_id, oracle_id) do update set categories = excluded.categories, updated_at = excluded.updated_at
            """,
            new { ownerId, oracleId, categories = categories.ToArray(), now },
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>
    /// Deletes the rule of an owner for an Oracle card, when it exists.
    /// </summary>
    public static Task DeleteAsync(NpgsqlTransaction transaction, Guid ownerId, Guid oracleId, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "delete from content.category_rules where owner_id = @ownerId and oracle_id = @oracleId",
            new { ownerId, oracleId },
            transaction,
            cancellationToken: cancellationToken));
}
