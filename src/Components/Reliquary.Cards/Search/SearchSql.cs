namespace Reliquary.Cards.Search;

/// <summary>
/// Builds the SQL statements of a compiled search: the page of results and the count of the matches.
/// </summary>
/// <remarks>
/// The statements use the aliases <c>o</c> (the Oracle card) and <c>p</c> (the printing), as the conditions of <see cref="SearchCompiler"/>.
/// The parameters <c>@limit</c> and <c>@offset</c> give the page.
/// </remarks>
internal static class SearchSql
{
    /// <summary>The set types of the printings that the default printing choice prefers.</summary>
    private const string RegularSetTypes = "'expansion', 'core', 'masters', 'draft_innovation', 'commander', 'starter', 'duel_deck', 'eternal', 'planechase', 'archenemy'";

    private const string Atypical =
        "coalesce(p.border_color = 'borderless' or p.full_art or p.textless or p.frame_effects && array['showcase', 'extendedart', 'inverted', 'etched', 'shatteredglass']::text[], false)";

    private const string CardColumns = """
        o.id as Id, o.name as Name, o.layout as Layout, o.mana_cost as ManaCost, o.mana_value as ManaValue, o.type_line as TypeLine,
        o.oracle_text as OracleText, o.colors as Colors, o.color_identity as ColorIdentity, o.power as Power, o.toughness as Toughness,
        o.loyalty as Loyalty, o.defense as Defense,
        (select coalesce(json_agg(json_build_object(
                'name', f.name, 'manaCost', f.mana_cost, 'manaValue', f.mana_value, 'typeLine', f.type_line, 'oracleText', f.oracle_text,
                'colors', f.colors, 'power', f.power, 'toughness', f.toughness, 'loyalty', f.loyalty, 'defense', f.defense) order by f.face_index), '[]')
         from reference.card_faces f where f.oracle_id = o.id)::text as Faces,
        p.id as PrintingId, p.set_code as SetCode, p.set_name as SetName, p.collector_number as CollectorNumber, p.lang as Lang,
        p.released_at as ReleasedAt, p.rarity as Rarity, p.printed_name as PrintedName, p.image_uris::text as ImageUris,
        (select coalesce(json_agg(json_build_object('name', pf.name, 'printedName', pf.printed_name, 'imageUris', pf.image_uris) order by pf.face_index), '[]')
         from reference.printing_faces pf where pf.printing_id = p.id)::text as PrintingFaces,
        page.total as Total
        """;

    /// <summary>
    /// Builds the statement that gives one page of results, with the total number of matches.
    /// </summary>
    /// <param name="search">The compiled search.</param>
    /// <returns>The SQL statement.</returns>
    public static string Page(CompiledSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        return search.Unique switch
        {
            SearchUnique.Prints => PrintsPage(search),
            SearchUnique.Art => ArtPage(search),
            _ => CardsPage(search),
        };
    }

    /// <summary>
    /// Builds the statement that counts the matches.
    /// </summary>
    /// <param name="search">The compiled search.</param>
    /// <returns>The SQL statement.</returns>
    public static string Count(CompiledSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        return search.Unique switch
        {
            SearchUnique.Prints => $"select count(*) {PrintingSource(search)}",
            SearchUnique.Art => $"select count(distinct coalesce(p.illustration_id, p.id)) {PrintingSource(search)}",
            _ => $"select count(*) from reference.oracle_cards o where {CardWhere(search)}",
        };
    }

    private static bool PrintingOrder(SearchOrder order) =>
        order is SearchOrder.Set or SearchOrder.Usd or SearchOrder.Eur or SearchOrder.Tix or SearchOrder.Rarity or SearchOrder.Released or SearchOrder.Artist;

    private static string Price(string currency) => currency switch
    {
        "usd" => "coalesce((p.prices->>'usd')::numeric, (p.prices->>'usd_foil')::numeric, (p.prices->>'usd_etched')::numeric)",
        "eur" => "coalesce((p.prices->>'eur')::numeric, (p.prices->>'eur_foil')::numeric)",
        _ => "(p.prices->>'tix')::numeric",
    };

    /// <summary>
    /// Gives the sort keys of the order, without the keys that break the ties.
    /// </summary>
    private static string OrderKeys(CompiledSearch search)
    {
        var descending = search.Direction switch
        {
            SearchDirection.Ascending => false,
            SearchDirection.Descending => true,
            _ => search.Order is SearchOrder.Released or SearchOrder.Usd or SearchOrder.Eur or SearchOrder.Tix,
        };
        var direction = descending ? "desc nulls last" : "asc nulls last";
        return search.Order switch
        {
            SearchOrder.Name => $"o.name {direction}",
            SearchOrder.ManaValue => $"o.mana_value {direction}",
            SearchOrder.Power => $"reference.stat_value(o.power) {direction}",
            SearchOrder.Toughness => $"reference.stat_value(o.toughness) {direction}",
            SearchOrder.Color => $"(case when o.colors_mask & 31 = 0 then 2 when cardinality(o.colors) > 1 then 1 else 0 end) {direction}, (o.colors_mask & 31) {direction}",
            SearchOrder.Edhrec => $"o.edhrec_rank {direction}",
            SearchOrder.Penny => $"o.penny_rank {direction}",
            SearchOrder.Set => $"p.set_code {direction}, p.collector_number_value {direction}, p.collector_number {direction}",
            SearchOrder.Usd => $"{Price("usd")} {direction}",
            SearchOrder.Eur => $"{Price("eur")} {direction}",
            SearchOrder.Tix => $"{Price("tix")} {direction}",
            SearchOrder.Rarity => $"p.rarity_rank {direction}",
            SearchOrder.Released => $"p.released_at {direction}",
            SearchOrder.Artist => $"p.artist {direction}",
            _ => throw new ArgumentOutOfRangeException(nameof(search)),
        };
    }

    /// <summary>
    /// Gives the order of the printings of one card: the preference of the query, then the rules of the default printing.
    /// </summary>
    private static string PreferenceOrder(CompiledSearch search, string? hint)
    {
        var keys = new List<string>();
        if (hint is not null)
        {
            keys.Add($"(p.lang = {hint}) desc nulls last");
        }

        keys.Add(search.Prefer switch
        {
            SearchPreference.Oldest => "p.released_at asc",
            SearchPreference.Newest => "p.released_at desc",
            SearchPreference.UsdLow => $"{Price("usd")} asc nulls last",
            SearchPreference.UsdHigh => $"{Price("usd")} desc nulls last",
            SearchPreference.EurLow => $"{Price("eur")} asc nulls last",
            SearchPreference.EurHigh => $"{Price("eur")} desc nulls last",
            SearchPreference.TixLow => $"{Price("tix")} asc nulls last",
            SearchPreference.TixHigh => $"{Price("tix")} desc nulls last",
            SearchPreference.Promo => "p.promo desc",
            SearchPreference.Atypical => $"{Atypical} desc",
            SearchPreference.UniversesBeyond => "('universesbeyond' = any (p.promo_types)) desc",
            SearchPreference.NotUniversesBeyond => "('universesbeyond' = any (p.promo_types)) asc",
            _ => "(p.lang = 'en') desc",
        });

        keys.Add($"""
            (p.lang = 'en') desc,
            (p.released_at <= current_date) desc,
            ('paper' = any (p.games)) desc,
            (p.set_type in ({RegularSetTypes})) desc,
            (p.collector_number ~ '^[0-9]+[a-z]?$') desc,
            coalesce(p.frame in ('2015', '2003'), false) desc,
            (p.promo or p.variation or p.oversized or p.full_art) asc,
            coalesce(p.border_color = 'black', false) desc,
            {Atypical} asc,
            coalesce(p.image_status = 'highres_scan', false) desc,
            p.released_at desc,
            p.collector_number_value asc nulls last,
            p.id
            """);
        return string.Join(",\n", keys);
    }

    /// <summary>
    /// Gives the condition on the printings that the search can use: the default language unless the query gives a language,
    /// and no printing of a memorabilia set unless the query includes the extra cards.
    /// </summary>
    private static string Language(CompiledSearch search, string? hint)
    {
        var language = search.LanguageGiven ? null : hint is null ? "p.default_lang" : $"(p.default_lang or p.lang = {hint})";
        var memorabilia = search.IncludeExtras ? null : "p.set_type <> 'memorabilia' and p.games <> '{sega}'::text[]";
        return language is null && memorabilia is null ? "true" : string.Join(" and ", new[] { language, memorabilia }.OfType<string>());
    }

    private static string And(params string?[] conditions)
    {
        var present = conditions.Where(condition => !string.IsNullOrEmpty(condition)).ToList();
        return present.Count == 0 ? "true" : string.Join("\n  and ", present);
    }

    private static string? Extras(CompiledSearch search) => search.IncludeExtras ? null : "not o.is_extra and not o.search_hidden";

    /// <summary>
    /// Gives the condition of an Oracle card in the mode <c>unique:cards</c>: the card conditions,
    /// then a printing of the card that matches the printing conditions.
    /// </summary>
    private static string CardWhere(CompiledSearch search)
    {
        string? printing = null;
        if (search.UsesPrintings)
        {
            printing = $"exists (select 1 from reference.printings p where p.oracle_id = o.id and {Language(search, null)} and {search.PrintingCondition ?? "true"})";
        }

        return And(Extras(search), search.CardCondition, printing);
    }

    /// <summary>
    /// Gives the subquery that chooses the printing of a card (alias <c>o</c>), with the language of a matching localized name in <c>h.lang</c>.
    /// </summary>
    private static string Chooser(CompiledSearch search, string? hint) => $"""
        (select p.id from reference.printings p
         where p.oracle_id = o.id and {Language(search, hint)} and {search.PrintingCondition ?? "true"}
         order by {PreferenceOrder(search, hint)}
         limit 1)
        """;

    private static bool UsesDefaultPrinting(CompiledSearch search) =>
        !search.UsesPrintings && search.Prefer == SearchPreference.Default && search.NameLanguage is null;

    private static string HintJoin(CompiledSearch search) =>
        search.NameLanguage is null ? string.Empty : $"left join lateral (select {search.NameLanguage} as lang) h on true";

    private static string? HintColumn(CompiledSearch search) => search.NameLanguage is null ? null : "h.lang";

    private static string CardsPage(CompiledSearch search)
    {
        var chooser = UsesDefaultPrinting(search)
            ? "o.default_printing_id"
            : $"coalesce({Chooser(search, HintColumn(search))}, o.default_printing_id)";

        if (PrintingOrder(search.Order))
        {
            return $"""
                with matched as (
                    select o.id as oracle_id, {chooser} as printing_id
                    from reference.oracle_cards o
                    {HintJoin(search)}
                    where {CardWhere(search)}
                ),
                page as materialized (
                    select m.oracle_id, m.printing_id, count(*) over () as total,
                           row_number() over (order by {OrderKeys(search)}, o.name, o.id) as position
                    from matched m
                    join reference.oracle_cards o on o.id = m.oracle_id
                    left join reference.printings p on p.id = m.printing_id
                    order by position
                    limit @limit offset @offset
                )
                select {CardColumns}
                from page
                join reference.oracle_cards o on o.id = page.oracle_id
                left join reference.printings p on p.id = page.printing_id
                order by page.position
                """;
        }

        return $"""
            with page as materialized (
                select o.id as oracle_id, count(*) over () as total,
                       row_number() over (order by {OrderKeys(search)}, o.name, o.id) as position
                from reference.oracle_cards o
                where {CardWhere(search)}
                order by position
                limit @limit offset @offset
            ),
            chosen as materialized (
                select page.oracle_id, page.total, page.position, {chooser} as printing_id
                from page
                join reference.oracle_cards o on o.id = page.oracle_id
                {HintJoin(search)}
            )
            select {CardColumns}
            from chosen page
            join reference.oracle_cards o on o.id = page.oracle_id
            left join reference.printings p on p.id = page.printing_id
            order by page.position
            """;
    }

    /// <summary>
    /// Gives the <c>from</c> and <c>where</c> clauses of the matching printings, for the modes <c>unique:prints</c> and <c>unique:art</c>.
    /// </summary>
    private static string PrintingSource(CompiledSearch search) => $"""
        from reference.printings p
        join reference.oracle_cards o on o.id = p.oracle_id
        {HintJoin(search)}
        where {And(Extras(search), search.CardCondition, Language(search, HintColumn(search)), search.PrintingCondition)}
        """;

    private static string PrintsPage(CompiledSearch search) => $"""
        with page as materialized (
            select p.id as printing_id, p.oracle_id, count(*) over () as total,
                   row_number() over (order by {OrderKeys(search)}, o.name, p.released_at desc, p.set_code, p.collector_number_value, p.id) as position
            {PrintingSource(search)}
            order by position
            limit @limit offset @offset
        )
        select {CardColumns}
        from page
        join reference.oracle_cards o on o.id = page.oracle_id
        join reference.printings p on p.id = page.printing_id
        order by page.position
        """;

    private static string ArtPage(CompiledSearch search) => $"""
        with arts as (
            select distinct on (coalesce(p.illustration_id, p.id)) p.id as printing_id, p.oracle_id
            {PrintingSource(search)}
            order by coalesce(p.illustration_id, p.id), {PreferenceOrder(search, HintColumn(search))}
        ),
        page as materialized (
            select a.printing_id, a.oracle_id, count(*) over () as total,
                   row_number() over (order by {OrderKeys(search)}, o.name, p.released_at desc, p.id) as position
            from arts a
            join reference.printings p on p.id = a.printing_id
            join reference.oracle_cards o on o.id = a.oracle_id
            order by position
            limit @limit offset @offset
        )
        select {CardColumns}
        from page
        join reference.oracle_cards o on o.id = page.oracle_id
        join reference.printings p on p.id = page.printing_id
        order by page.position
        """;
}
