using System.Text.Json;
using Dapper;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Cards.Text;

namespace Reliquary.Cards.Catalog;

/// <summary>
/// Gives the card reference data to the other core components. See <see cref="ICardCatalog"/>.
/// </summary>
/// <param name="database">The databases of the instance.</param>
internal sealed class CardCatalog(IDatabase database) : ICardCatalog
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, CardFacts>> GetCardsAsync(IReadOnlyCollection<Guid> oracleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oracleIds);
        if (oracleIds.Count == 0)
        {
            return new Dictionary<Guid, CardFacts>();
        }

        var ids = oracleIds.Distinct().ToArray();
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        var cards = await connection.QueryAsync<CardRow>(new CommandDefinition(
            """
            select id, name, layout, mana_value as ManaValue, type_line as TypeLine, color_identity as ColorIdentity,
                legalities::text as Legalities, keywords, produced_mana as ProducedMana, is_extra as IsExtra,
                game_changer as GameChanger, default_printing_id as DefaultPrintingId
            from reference.oracle_cards
            where id = any(@ids)
            """,
            new { ids },
            cancellationToken: cancellationToken));
        var faces = (await connection.QueryAsync<FaceRow>(new CommandDefinition(
            """
            select oracle_id as OracleId, face_index as FaceIndex, name, mana_cost as ManaCost, mana_value as ManaValue,
                type_line as TypeLine, oracle_text as OracleText, colors
            from reference.card_faces
            where oracle_id = any(@ids)
            order by oracle_id, face_index
            """,
            new { ids },
            cancellationToken: cancellationToken)))
            .GroupBy(face => face.OracleId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CardFaceFacts>)[.. group.Select(face =>
                new CardFaceFacts(face.FaceIndex, face.Name, face.ManaCost, face.ManaValue, face.TypeLine, face.OracleText, face.Colors))]);

        return cards.ToDictionary(card => card.Id, card =>
        {
            var cardFaces = faces.GetValueOrDefault(card.Id) ?? [];
            return new CardFacts(
                card.Id,
                card.Name,
                card.Layout,
                card.ManaValue,
                card.TypeLine,
                card.ColorIdentity,
                JsonSerializer.Deserialize<Dictionary<string, string>>(card.Legalities) ?? [],
                card.Keywords,
                card.ProducedMana,
                card.IsExtra,
                card.GameChanger,
                card.DefaultPrintingId,
                cardFaces,
                CardAnalysisRules.Calculate(card.Layout, cardFaces));
        });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, CardNameMatch>> ResolveNamesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        var byNormalized = names.Distinct(StringComparer.Ordinal)
            .Select(name => (Name: name, Normalized: NormalizeForLookup(name)))
            .ToList();
        var result = byNormalized.ToDictionary(entry => entry.Name, _ => new CardNameMatch(CardNameStatus.NotFound, null, null), StringComparer.Ordinal);
        if (byNormalized.Count == 0)
        {
            return result;
        }

        var normalized = byNormalized.Select(entry => entry.Normalized).Distinct(StringComparer.Ordinal).ToArray();
        await using var connection = await database.OpenReferenceAsync(cancellationToken);

        // The same order as the name lookup of the API: a game card wins over an extra card, and the Oracle name wins over the other names.
        var matches = (await connection.QueryAsync<NameRow>(new CommandDefinition(
            """
            select distinct on (n.name_normalized) n.name_normalized as Normalized, o.id as OracleId, o.name
            from reference.card_names n
            join reference.oracle_cards o on o.id = n.oracle_id
            where n.name_normalized = any(@normalized)
            order by n.name_normalized, o.is_extra, case n.kind when 'oracle' then 0 when 'face' then 1 when 'flavor' then 2 else 3 end,
                o.edhrec_rank nulls last, o.id
            """,
            new { normalized },
            cancellationToken: cancellationToken))).ToDictionary(row => row.Normalized, StringComparer.Ordinal);

        foreach (var (name, key) in byNormalized)
        {
            if (matches.TryGetValue(key, out var match))
            {
                result[name] = new CardNameMatch(CardNameStatus.Found, match.OracleId, match.Name);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<PrintingReference, PrintingFacts>> FindPrintingsAsync(IReadOnlyCollection<PrintingReference> references, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        var distinct = references.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<PrintingReference, PrintingFacts>();
        }

        var sets = distinct.Select(reference => reference.SetCode.Trim().ToLowerInvariant()).ToArray();
        var numbers = distinct.Select(reference => reference.CollectorNumber.Trim()).ToArray();
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        var rows = (await connection.QueryAsync<PrintingRow>(new CommandDefinition(
            $"""
            select {PrintingColumns}
            from reference.printings p
            join unnest(@sets, @numbers) as wanted(set_code, collector_number)
                on p.set_code = wanted.set_code and p.collector_number = wanted.collector_number
            """,
            new { sets, numbers },
            cancellationToken: cancellationToken))).ToList();

        var result = new Dictionary<PrintingReference, PrintingFacts>();
        foreach (var reference in distinct)
        {
            var candidates = rows.Where(row => string.Equals(row.SetCode, reference.SetCode.Trim(), StringComparison.OrdinalIgnoreCase) && row.CollectorNumber == reference.CollectorNumber.Trim()).ToList();

            // Without a language, the English printing wins.
            var row = reference.Lang is { Length: > 0 } lang
                ? candidates.FirstOrDefault(candidate => candidate.Lang == lang)
                : candidates.OrderBy(candidate => candidate.Lang == "en" ? 0 : 1).ThenBy(candidate => candidate.Lang, StringComparer.Ordinal).FirstOrDefault();
            if (row is not null)
            {
                result[reference] = row.ToFacts();
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, PrintingFacts>> GetPrintingsAsync(IReadOnlyCollection<Guid> printingIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printingIds);
        if (printingIds.Count == 0)
        {
            return new Dictionary<Guid, PrintingFacts>();
        }

        var ids = printingIds.Distinct().ToArray();
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        var rows = await connection.QueryAsync<PrintingRow>(new CommandDefinition(
            $"select {PrintingColumns} from reference.printings p where p.id = any(@ids)",
            new { ids },
            cancellationToken: cancellationToken));
        return rows.ToDictionary(row => row.Id, row => row.ToFacts());
    }

    /// <summary>
    /// Normalizes a name for the lookup. Some tools write the halves of a split card with three slashes.
    /// </summary>
    internal static string NormalizeForLookup(string name) =>
        NameNormalizer.Normalize(name.Replace("///", "//", StringComparison.Ordinal));

    private const string PrintingColumns = """
        p.id, p.oracle_id as OracleId, p.set_code as SetCode, p.collector_number as CollectorNumber, p.lang, p.finishes,
        coalesce(p.image_uris ->> 'normal', (select f.image_uris ->> 'normal' from reference.printing_faces f where f.printing_id = p.id and f.face_index = 0)) as ImageUri
        """;

    private sealed class CardRow
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Layout { get; set; } = string.Empty;

        public decimal ManaValue { get; set; }

        public string? TypeLine { get; set; }

        public string[] ColorIdentity { get; set; } = [];

        public string Legalities { get; set; } = "{}";

        public string[] Keywords { get; set; } = [];

        public string[] ProducedMana { get; set; } = [];

        public bool IsExtra { get; set; }

        public bool GameChanger { get; set; }

        public Guid? DefaultPrintingId { get; set; }
    }

    private sealed class FaceRow
    {
        public Guid OracleId { get; set; }

        public short FaceIndex { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? ManaCost { get; set; }

        public decimal ManaValue { get; set; }

        public string? TypeLine { get; set; }

        public string? OracleText { get; set; }

        public string[] Colors { get; set; } = [];
    }

    private sealed class NameRow
    {
        public string Normalized { get; set; } = string.Empty;

        public Guid OracleId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class PrintingRow
    {
        public Guid Id { get; set; }

        public Guid OracleId { get; set; }

        public string SetCode { get; set; } = string.Empty;

        public string CollectorNumber { get; set; } = string.Empty;

        public string Lang { get; set; } = "en";

        public string[] Finishes { get; set; } = [];

        public string? ImageUri { get; set; }

        public PrintingFacts ToFacts() => new(Id, OracleId, SetCode, CollectorNumber, Lang, Finishes, ImageUri);
    }
}
