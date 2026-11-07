using Dapper;
using Npgsql;

namespace Reliquary.Decks.Versions;

/// <summary>
/// A frozen version of a deck.
/// </summary>
internal sealed class VersionRow
{
    public Guid Id { get; set; }

    public Guid DeckId { get; set; }

    public int Number { get; set; }

    public string Kind { get; set; } = "commit";

    public string Message { get; set; } = string.Empty;

    public Guid[] ParentIds { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public int CardCount { get; set; }
}

/// <summary>
/// A frozen line of a deck version.
/// </summary>
internal sealed class VersionLineRow
{
    public Guid OracleId { get; set; }

    public Guid? PrintingId { get; set; }

    public string? Finish { get; set; }

    public int Quantity { get; set; }

    public string Zone { get; set; } = "main";

    public string Note { get; set; } = string.Empty;

    public string[] Categories { get; set; } = [];

    public string[] Tags { get; set; } = [];

    public string[] GlobalTags { get; set; } = [];

    public string? PurchasePreferences { get; set; }

    /// <summary>Gets the content that a comparison of two lists uses.</summary>
    public string Content =>
        $"{Zone}|{OracleId}|{PrintingId}|{Finish}|{Quantity}|{Note}|{string.Join(',', Categories.Order(StringComparer.Ordinal))}|{string.Join(',', Tags.Order(StringComparer.OrdinalIgnoreCase))}|{string.Join(',', GlobalTags.Order(StringComparer.OrdinalIgnoreCase))}|{PurchasePreferences}";
}

/// <summary>
/// The SQL of the deck versions. A database trigger refuses each change of a version.
/// </summary>
internal static class VersionStore
{
    private const string VersionColumns = """
        v.id, v.deck_id as DeckId, v.number, v.kind, v.message, v.parent_ids as ParentIds, v.created_at as CreatedAt,
        coalesce((select sum(l.quantity) from content.deck_version_lines l where l.version_id = v.id and l.zone in ('commander', 'companion', 'main')), 0)::int as CardCount
        """;

    private const string LineColumns = """
        oracle_id as OracleId, printing_id as PrintingId, finish, quantity, zone, note, categories, tags, global_tags as GlobalTags,
        purchase_preferences::text as PurchasePreferences
        """;

    public static async Task<VersionRow> InsertAsync(
        NpgsqlTransaction transaction, Guid ownerId, Guid deckId, string kind, string message, IReadOnlyList<Guid> parentIds, IReadOnlyList<VersionLineRow> lines, Guid id, DateTime now, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        var number = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select coalesce(max(number), 0) + 1 from content.deck_versions where deck_id = @deckId",
            new { deckId },
            transaction,
            cancellationToken: cancellationToken));
        var row = new VersionRow { Id = id, DeckId = deckId, Number = number, Kind = kind, Message = message, ParentIds = [.. parentIds], CreatedAt = now };
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.deck_versions (id, deck_id, owner_id, number, kind, message, parent_ids, created_at)
            values (@Id, @DeckId, @ownerId, @Number, @Kind, @Message, @ParentIds, @CreatedAt)
            """,
            new { row.Id, row.DeckId, ownerId, row.Number, row.Kind, row.Message, row.ParentIds, row.CreatedAt },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.deck_version_lines (version_id, owner_id, oracle_id, printing_id, finish, quantity, zone, note, categories, tags, global_tags, purchase_preferences)
            values (@versionId, @ownerId, @OracleId, @PrintingId, @Finish, @Quantity, @Zone, @Note, @Categories, @Tags, @GlobalTags, @PurchasePreferences::jsonb)
            """,
            lines.Select(line => new { versionId = id, ownerId, line.OracleId, line.PrintingId, line.Finish, line.Quantity, line.Zone, line.Note, line.Categories, line.Tags, line.GlobalTags, line.PurchasePreferences }),
            transaction,
            cancellationToken: cancellationToken));
        row.CardCount = lines.Where(line => line.Zone is "commander" or "companion" or "main").Sum(line => line.Quantity);
        return row;
    }

    public static async Task<IReadOnlyList<VersionRow>> ListAsync(NpgsqlConnection connection, Guid deckId, CancellationToken cancellationToken) =>
        [.. await connection.QueryAsync<VersionRow>(new CommandDefinition(
            $"select {VersionColumns} from content.deck_versions v where v.deck_id = @deckId order by v.number desc",
            new { deckId },
            cancellationToken: cancellationToken))];

    public static Task<VersionRow?> FindAsync(NpgsqlConnection connection, Guid versionId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        connection.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition(
            $"select {VersionColumns} from content.deck_versions v where v.id = @versionId",
            new { versionId },
            transaction,
            cancellationToken: cancellationToken));

    public static async Task<IReadOnlyList<VersionLineRow>> ListLinesAsync(NpgsqlConnection connection, Guid versionId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        [.. await connection.QueryAsync<VersionLineRow>(new CommandDefinition(
            $"select {LineColumns} from content.deck_version_lines where version_id = @versionId order by zone, oracle_id",
            new { versionId },
            transaction,
            cancellationToken: cancellationToken))];
}
