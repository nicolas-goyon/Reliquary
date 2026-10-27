using System.Text.Json;
using Dapper;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Cards.Text;

namespace Reliquary.Cards.Api;

/// <summary>
/// Reads the card reference data for the public API.
/// </summary>
/// <param name="database">The databases.</param>
internal sealed class CardQueries(IDatabase database)
{
    private const string PrintingColumns = """
        p.id as Id, p.oracle_id as CardId, p.set_code as SetCode, p.set_name as SetName, p.collector_number as CollectorNumber,
        p.lang as Lang, p.released_at as ReleasedAt, p.rarity as Rarity, p.finishes as Finishes, p.printed_name as PrintedName,
        p.artist as Artist, p.image_uris::text as ImageUris, p.prices::text as Prices, p.external_ids::text as ExternalIds
        """;

    private const string PrintingFaceColumns = """
        f.printing_id as PrintingId, f.name as Name, f.printed_name as PrintedName, f.image_uris::text as ImageUris
        """;

    /// <summary>
    /// Gets an Oracle card with its faces, names, related cards and default printing.
    /// </summary>
    /// <param name="id">The Oracle identifier.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The card, or <see langword="null"/> when it does not exist.</returns>
    public async Task<CardResponse?> GetCardAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            $"""
            select id as Id, name as Name, layout as Layout, mana_cost as ManaCost, mana_value as ManaValue, type_line as TypeLine,
                   oracle_text as OracleText, colors as Colors, color_identity as ColorIdentity, keywords as Keywords,
                   produced_mana as ProducedMana, power as Power, toughness as Toughness, loyalty as Loyalty, defense as Defense,
                   legalities::text as Legalities, reserved as Reserved, game_changer as GameChanger, edhrec_rank as EdhrecRank,
                   is_extra as Extra, default_printing_id as DefaultPrintingId
            from reference.oracle_cards where id = @id;

            select name as Name, mana_cost as ManaCost, mana_value as ManaValue, type_line as TypeLine, oracle_text as OracleText,
                   colors as Colors, power as Power, toughness as Toughness, loyalty as Loyalty, defense as Defense
            from reference.card_faces where oracle_id = @id order by face_index;

            select name as Name, lang as Lang, kind as Kind
            from reference.card_names where oracle_id = @id and kind <> 'oracle' order by kind, lang, name;

            select related_printing_id as PrintingId, related_name as Name, component as Component
            from reference.card_relations where oracle_id = @id order by component, related_name;

            select {PrintingColumns}
            from reference.printings p
            where p.id = (select default_printing_id from reference.oracle_cards where id = @id);

            select {PrintingFaceColumns}
            from reference.printing_faces f
            where f.printing_id = (select default_printing_id from reference.oracle_cards where id = @id)
            order by f.face_index;
            """,
            new { id },
            cancellationToken: cancellationToken));

        var card = await results.ReadSingleOrDefaultAsync<CardRow>();
        if (card is null)
        {
            return null;
        }

        var faces = (await results.ReadAsync<CardFaceRow>()).ToList();
        var names = (await results.ReadAsync<NameRow>()).Select(row => new CardNameResponse(row.Name, row.Lang, row.Kind)).ToList();
        var related = (await results.ReadAsync<RelatedRow>()).Select(row => new RelatedCardResponse(row.PrintingId, row.Name, row.Component)).ToList();
        var printing = await results.ReadSingleOrDefaultAsync<PrintingRow>();
        var printingFaces = (await results.ReadAsync<PrintingFaceRow>()).ToList();

        return new CardResponse
        {
            Id = card.Id,
            Name = card.Name,
            Layout = card.Layout,
            ManaCost = card.ManaCost,
            ManaValue = card.ManaValue,
            TypeLine = card.TypeLine,
            OracleText = card.OracleText,
            Colors = card.Colors,
            ColorIdentity = card.ColorIdentity,
            Keywords = card.Keywords,
            ProducedMana = card.ProducedMana,
            Power = card.Power,
            Toughness = card.Toughness,
            Loyalty = card.Loyalty,
            Defense = card.Defense,
            Legalities = Dictionary(card.Legalities),
            Reserved = card.Reserved,
            GameChanger = card.GameChanger,
            EdhrecRank = card.EdhrecRank,
            Extra = card.Extra,
            Faces = [.. faces.Select(face => new CardFaceResponse(face.Name, face.ManaCost, face.ManaValue, face.TypeLine, face.OracleText, face.Colors, face.Power, face.Toughness, face.Loyalty, face.Defense))],
            DefaultPrinting = printing is null ? null : ToResponse(printing, printingFaces),
            Names = names,
            RelatedCards = related,
        };
    }

    /// <summary>
    /// Finds a card by one of its names: the Oracle name, a face name, a localized name or an alternate name.
    /// </summary>
    /// <param name="name">The name. The comparison ignores the case, the accents and the typographic punctuation.</param>
    /// <param name="fuzzy">When <see langword="true"/> and no name is equal, the search gives the most similar name.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The Oracle identifier, or <see langword="null"/> when no card has this name.</returns>
    /// <summary>
    /// Finds the cards whose names start with a text, then the cards whose names contain it.
    /// </summary>
    /// <param name="text">The text that the user typed.</param>
    /// <param name="lang">The language of the user. A name in this language wins over the other names of the card.</param>
    /// <param name="limit">The maximum number of cards.</param>
    /// <param name="includeExtras">Whether the result contains tokens and the other extra cards.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The cards, the best match first.</returns>
    public async Task<IReadOnlyList<AutocompleteItem>> AutocompleteAsync(string text, string? lang, int limit, bool includeExtras, CancellationToken cancellationToken)
    {
        var normalized = NameNormalizer.Normalize(text);
        var escaped = normalized.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        var rows = await connection.QueryAsync<AutocompleteRow>(new CommandDefinition(
            """
            with candidates as (
                (select oracle_id, name, lang, kind, true as prefix
                 from reference.card_names where name_normalized like @prefix escape '\' limit 500)
                union all
                (select oracle_id, name, lang, kind, false
                 from reference.card_names where @contains and name_normalized like @infix escape '\' limit 500)
            ),
            best as (
                select distinct on (c.oracle_id) c.oracle_id, c.name as matched, c.lang, c.prefix, o.name, o.is_extra, o.edhrec_rank
                from candidates c
                join reference.oracle_cards o on o.id = c.oracle_id
                where @extras or not o.is_extra
                order by c.oracle_id, c.prefix desc, (c.lang = @lang) desc, (c.lang = 'en') desc,
                    case c.kind when 'oracle' then 0 when 'face' then 1 when 'printed' then 2 else 3 end
            )
            select oracle_id as OracleId, name, matched as MatchedName, lang
            from best
            order by prefix desc, is_extra, edhrec_rank nulls last, length(name), name
            limit @limit
            """,
            new { prefix = escaped + "%", infix = "%" + escaped + "%", contains = normalized.Length >= 3, lang = lang ?? "en", extras = includeExtras, limit },
            cancellationToken: cancellationToken));
        return [.. rows.Select(row => new AutocompleteItem(row.OracleId, row.Name, row.MatchedName, row.Lang))];
    }

    public async Task<Guid?> FindByNameAsync(string name, bool fuzzy, CancellationToken cancellationToken)
    {
        var normalized = NameNormalizer.Normalize(name);
        await using var connection = await database.OpenReferenceAsync(cancellationToken);

        // An exact name wins. The Oracle name of a game card wins over the other names.
        var exact = await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            select n.oracle_id
            from reference.card_names n
            join reference.oracle_cards o on o.id = n.oracle_id
            where n.name_normalized = @normalized
            order by o.is_extra, case n.kind when 'oracle' then 0 when 'face' then 1 when 'flavor' then 2 else 3 end, o.edhrec_rank nulls last, o.id
            limit 1
            """,
            new { normalized },
            cancellationToken: cancellationToken));

        if (exact is not null || !fuzzy)
        {
            return exact;
        }

        return await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            select n.oracle_id
            from reference.card_names n
            join reference.oracle_cards o on o.id = n.oracle_id
            where n.name_normalized % @normalized
            order by similarity(n.name_normalized, @normalized) desc, o.is_extra, o.edhrec_rank nulls last, o.id
            limit 1
            """,
            new { normalized },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Gets a printing.
    /// </summary>
    /// <param name="id">The identifier of the printing.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The printing, or <see langword="null"/> when it does not exist.</returns>
    public async Task<PrintingResponse?> GetPrintingAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            $"""
            select {PrintingColumns} from reference.printings p where p.id = @id;
            select {PrintingFaceColumns} from reference.printing_faces f where f.printing_id = @id order by f.face_index;
            """,
            new { id },
            cancellationToken: cancellationToken));

        var printing = await results.ReadSingleOrDefaultAsync<PrintingRow>();
        return printing is null ? null : ToResponse(printing, [.. await results.ReadAsync<PrintingFaceRow>()]);
    }

    /// <summary>
    /// Gets the printings of a card, the most recent first.
    /// </summary>
    /// <param name="oracleId">The Oracle identifier of the card.</param>
    /// <param name="lang">The language of the printings, or <see langword="null"/> for all the languages.</param>
    /// <param name="page">The paging parameters.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The page, or <see langword="null"/> when the cursor is not valid.</returns>
    public async Task<Page<PrintingResponse>?> GetPrintingsAsync(Guid oracleId, string? lang, PageRequest page, CancellationToken cancellationToken)
    {
        PrintingCursor? after = null;
        if (page.Cursor is not null && !Cursors.TryDecode(page.Cursor, out after))
        {
            return null;
        }

        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        var printings = (await connection.QueryAsync<PrintingRow>(new CommandDefinition(
            $"""
            select {PrintingColumns}
            from reference.printings p
            where p.oracle_id = @oracleId
              and (@lang::text is null or p.lang = @lang)
              and (@afterDate::date is null or (p.released_at, p.id) < (@afterDate, @afterId))
            order by p.released_at desc, p.id desc
            limit @limit
            """,
            new { oracleId, lang, afterDate = after?.ReleasedAt, afterId = after?.Id, limit = page.Limit + 1 },
            cancellationToken: cancellationToken))).ToList();

        var hasMore = printings.Count > page.Limit;
        printings = printings.Take(page.Limit).ToList();
        var ids = printings.Select(printing => printing.Id).ToArray();
        var faces = (await connection.QueryAsync<PrintingFaceRow>(new CommandDefinition(
            $"select {PrintingFaceColumns} from reference.printing_faces f where f.printing_id = any (@ids) order by f.printing_id, f.face_index",
            new { ids },
            cancellationToken: cancellationToken))).ToLookup(face => face.PrintingId);

        var last = printings.LastOrDefault();
        return new Page<PrintingResponse>(
            [.. printings.Select(printing => ToResponse(printing, [.. faces[printing.Id]]))],
            hasMore && last is not null ? Cursors.Encode(new PrintingCursor(last.ReleasedAt, last.Id)) : null);
    }

    /// <summary>
    /// Gets the rulings of a card, the oldest first.
    /// </summary>
    /// <param name="oracleId">The Oracle identifier of the card.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The rulings.</returns>
    public async Task<IReadOnlyList<RulingResponse>> GetRulingsAsync(Guid oracleId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        var rows = await connection.QueryAsync<RulingRow>(new CommandDefinition(
            "select source as Source, published_at as PublishedAt, comment as Comment from reference.rulings where oracle_id = @oracleId order by published_at, comment",
            new { oracleId },
            cancellationToken: cancellationToken));
        return [.. rows.Select(row => new RulingResponse(row.Source, row.PublishedAt, row.Comment))];
    }

    /// <summary>
    /// Gets all the sets, the most recent first.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The sets.</returns>
    public async Task<IReadOnlyList<SetResponse>> GetSetsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        var rows = await connection.QueryAsync<SetRow>(new CommandDefinition(
            """
            select code as Code, name as Name, set_type as SetType, released_at as ReleasedAt, card_count as CardCount, digital as Digital, icon_svg_uri as IconSvgUri
            from reference.sets order by released_at desc nulls last, code
            """,
            cancellationToken: cancellationToken));
        return [.. rows.Select(row => new SetResponse(row.Code, row.Name, row.SetType, row.ReleasedAt, row.CardCount, row.Digital, row.IconSvgUri))];
    }

    /// <summary>
    /// Tells whether a card exists.
    /// </summary>
    /// <param name="oracleId">The Oracle identifier.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns><see langword="true"/> when the card exists.</returns>
    public async Task<bool> CardExistsAsync(Guid oracleId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from reference.oracle_cards where id = @oracleId)",
            new { oracleId },
            cancellationToken: cancellationToken));
    }

    private static PrintingResponse ToResponse(PrintingRow row, IReadOnlyList<PrintingFaceRow> faces) => new()
    {
        Id = row.Id,
        CardId = row.CardId,
        SetCode = row.SetCode,
        SetName = row.SetName,
        CollectorNumber = row.CollectorNumber,
        Lang = row.Lang,
        ReleasedAt = row.ReleasedAt,
        Rarity = row.Rarity,
        Finishes = row.Finishes,
        PrintedName = row.PrintedName,
        Artist = row.Artist,
        ImageUris = Dictionary(row.ImageUris),
        Faces = [.. faces.Select(face => new PrintingFaceResponse(face.Name, face.PrintedName, Dictionary(face.ImageUris)))],
        Prices = Dictionary(row.Prices),
        ExternalIds = Dictionary(row.ExternalIds),
    };

    private static Dictionary<string, string> Dictionary(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    private sealed class AutocompleteRow
    {
        public Guid OracleId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string MatchedName { get; set; } = string.Empty;

        public string Lang { get; set; } = "en";
    }

    private sealed class CardRow
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Layout { get; set; } = string.Empty;

        public string? ManaCost { get; set; }

        public decimal ManaValue { get; set; }

        public string? TypeLine { get; set; }

        public string? OracleText { get; set; }

        public string[] Colors { get; set; } = [];

        public string[] ColorIdentity { get; set; } = [];

        public string[] Keywords { get; set; } = [];

        public string[] ProducedMana { get; set; } = [];

        public string? Power { get; set; }

        public string? Toughness { get; set; }

        public string? Loyalty { get; set; }

        public string? Defense { get; set; }

        public string Legalities { get; set; } = string.Empty;

        public bool Reserved { get; set; }

        public bool GameChanger { get; set; }

        public int? EdhrecRank { get; set; }

        public bool Extra { get; set; }

        public Guid? DefaultPrintingId { get; set; }
    }

    private sealed class CardFaceRow
    {
        public string Name { get; set; } = string.Empty;

        public string? ManaCost { get; set; }

        public decimal ManaValue { get; set; }

        public string? TypeLine { get; set; }

        public string? OracleText { get; set; }

        public string[] Colors { get; set; } = [];

        public string? Power { get; set; }

        public string? Toughness { get; set; }

        public string? Loyalty { get; set; }

        public string? Defense { get; set; }
    }

    private sealed class PrintingRow
    {
        public Guid Id { get; set; }

        public Guid CardId { get; set; }

        public string SetCode { get; set; } = string.Empty;

        public string SetName { get; set; } = string.Empty;

        public string CollectorNumber { get; set; } = string.Empty;

        public string Lang { get; set; } = string.Empty;

        public DateOnly ReleasedAt { get; set; }

        public string Rarity { get; set; } = string.Empty;

        public string[] Finishes { get; set; } = [];

        public string? PrintedName { get; set; }

        public string? Artist { get; set; }

        public string? ImageUris { get; set; }

        public string? Prices { get; set; }

        public string? ExternalIds { get; set; }
    }

    private sealed class PrintingFaceRow
    {
        public Guid PrintingId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? PrintedName { get; set; }

        public string? ImageUris { get; set; }
    }

    private sealed record PrintingCursor(DateOnly ReleasedAt, Guid Id);

    private sealed class RulingRow
    {
        public string Source { get; set; } = string.Empty;

        public DateOnly PublishedAt { get; set; }

        public string Comment { get; set; } = string.Empty;
    }

    private sealed class NameRow
    {
        public string Name { get; set; } = string.Empty;

        public string Lang { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;
    }

    private sealed class RelatedRow
    {
        public Guid PrintingId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Component { get; set; } = string.Empty;
    }

    private sealed class SetRow
    {
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string SetType { get; set; } = string.Empty;

        public DateOnly? ReleasedAt { get; set; }

        public int CardCount { get; set; }

        public bool Digital { get; set; }

        public string? IconSvgUri { get; set; }
    }
}
