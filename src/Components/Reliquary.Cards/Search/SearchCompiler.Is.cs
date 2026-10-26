namespace Reliquary.Cards.Search;

/// <summary>
/// The conditions of the keywords <c>is:</c>, <c>not:</c> and <c>has:</c>.
/// </summary>
internal sealed partial class SearchCompiler
{
    // The expressions are the same as the expression statistics of the migration 0002, so the planner has statistics for them.
    // A null value gives a null condition. The negation of a condition changes null into false first.
    private const string FrontType = "lower(split_part(o.type_line, ' // ', 1))";
    private const string TypeLine = "lower(o.type_line)";
    private const string Text = "lower(o.oracle_text_plain)";
    private const string BasicType = "(plains|island|swamp|mountain|forest)";
    private const string IsLand = "o.type_line ilike '%land%'";

    /// <summary>A meld part has a relation to its meld result, another card. A meld result has relations to its parts only.</summary>
    private const string MeldPart = "exists (select 1 from reference.card_relations r where r.oracle_id = o.id and r.component = 'meld_result' and r.related_name <> o.name)";

    /// <summary>The conditions of the land nicknames, for example <c>is:fetchland</c>. Each condition contains no value of the query.</summary>
    private static readonly Dictionary<string, string> LandNicknames = new(StringComparer.Ordinal)
    {
        ["dual"] = $"o.type_line ~ '^Land — (Plains|Island|Swamp|Mountain|Forest) (Plains|Island|Swamp|Mountain|Forest)$' and coalesce(o.oracle_text_plain, '') = ''",
        ["shockland"] = $"{TypeLine} ~ '^land — {BasicType} {BasicType}$' and {Text} ~ 'pay 2 life\\. if you don.t, (it|this land) enters tapped'",
        ["fetchland"] = $"{IsLand} and {Text} ~ 'pay 1 life, sacrifice [^:]+: search your library for an? {BasicType} or {BasicType} card'",
        ["checkland"] = $"{IsLand} and {Text} ~ 'enters tapped unless you control an? {BasicType} or an? {BasicType}'",
        ["fastland"] = $"{IsLand} and {Text} like '%enters tapped unless you control two or fewer other lands%'",
        ["slowland"] = $"{IsLand} and {Text} like '%enters tapped unless you control two or more other lands%'",
        ["painland"] = $"{IsLand} and {Text} not like '%enters tapped%' and {Text} ~ '\\{{t\\}}: add \\{{[wubrg]\\}} or \\{{[wubrg]\\}}\\. [^.\\n]* deals 1 damage to you'",
        ["bounceland"] = $"{IsLand} and {Text} ~ 'enters, return a land you control to its owner.s hand'",
        ["gainland"] = $"{IsLand} and {Text} ~ 'enters tapped\\.\\nwhen [^\\n]* enters, you gain 1 life'",
        ["scryland"] = $"{IsLand} and {Text} ~ 'enters tapped\\.\\nwhen [^\\n]* enters, scry 1'",
        ["surveilland"] = $"{IsLand} and {Text} ~ 'enters tapped\\.\\nwhen [^\\n]* enters, surveil 1'",
        ["filterland"] = $"{IsLand} and {Text} ~ '\\{{[wubrg]/[wubrg]\\}}, \\{{t\\}}: add \\{{[wubrg]\\}}\\{{[wubrg]\\}}'",
        ["tangoland"] = $"{IsLand} and {Text} like '%enters tapped unless you control two or more basic lands%'",
        ["canopyland"] = $"{IsLand} and {Text} ~ '\\{{t\\}}, pay 1 life: add \\{{[wubrg]\\}} or \\{{[wubrg]\\}}' and {Text} like '%sacrifice%draw a card%'",
        ["shadowland"] = $"{IsLand} and {Text} ~ 'you may reveal an? {BasicType} or {BasicType} card from your hand\\. if you don.t'",
        ["storageland"] = $"{IsLand} and {Text} like '%storage counter%'",
        ["triland"] = $"{TypeLine} = 'land' and {Text} ~ '\\{{t\\}}: add \\{{[wubrg]\\}}, \\{{[wubrg]\\}}, or \\{{[wubrg]\\}}' and {Text} like '%enters tapped%'",
        ["tricycleland"] = $"{TypeLine} ~ '^land — {BasicType} {BasicType} {BasicType}$'",
        ["bikeland"] = $"{TypeLine} ~ '^land — {BasicType} {BasicType}$' and {Text} like '%cycling {{2}}%'",
        ["bondland"] = $"{IsLand} and {Text} like '%enters tapped unless you have two or more opponents%'",
        ["pathway"] = "o.layout = 'modal_dfc' and o.name like '%Pathway%'",
        ["manland"] = $"{FrontType} ~ '\\mland\\M' and {FrontType} !~ '\\mcreature\\M' and {Text} ~ '\\mbecomes? an? [^.]*creature'",
    };

    private static readonly Dictionary<string, string> LandAliases = new(StringComparer.Ordinal)
    {
        ["cycleland"] = "bikeland",
        ["bicycleland"] = "bikeland",
        ["crowdland"] = "bondland",
        ["bbdland"] = "bondland",
        ["battlebondland"] = "bondland",
        ["karoo"] = "bounceland",
        ["canland"] = "canopyland",
        ["creatureland"] = "manland",
        ["snarl"] = "shadowland",
        ["battleland"] = "tangoland",
        ["trikeland"] = "tricycleland",
        ["triome"] = "tricycleland",
        ["shock"] = "shockland",
        ["fetch"] = "fetchland",
    };

    private const string Atypical =
        "(p.border_color = 'borderless' or p.full_art or p.textless or p.frame_effects && array['showcase', 'extendedart', 'inverted', 'etched', 'shatteredglass']::text[])";

    private Fragment? IsTerm(TermNode term, string value, bool negated)
    {
        if (value is "token" or "extra" or "emblem" or "reserved" or "funny" or "dfc" or "doublefaced" && !negated)
        {
            revealsExtras = true;
        }

        if (value == "scryfallpreview")
        {
            return Warn(term, SearchWarningCodes.UnsupportedKeyword, $"Reliquary has no data for \"{TermText(term)}\". The search ignores the term.");
        }

        if (IsCondition(value) is { } fragment)
        {
            return fragment;
        }

        var normalized = value.Replace("_", string.Empty, System.StringComparison.Ordinal);
        if (SearchVocabulary.FrameEffects.Contains(normalized))
        {
            return Printing($"{Param(normalized)} = any (p.frame_effects)");
        }

        if (SearchVocabulary.PromoTypes.Contains(normalized))
        {
            return Printing($"{Param(normalized)} = any (p.promo_types)");
        }

        return Warn(term, SearchWarningCodes.InvalidValue, $"The search does not know the value \"{term.Value}\" of the keyword \"{term.Keyword}\". The search ignores the term \"{TermText(term)}\".");
    }

    /// <summary>
    /// Gives the condition of an <c>is:</c> value that contains no value of the query, or <see langword="null"/>.
    /// </summary>
    private static Fragment? IsCondition(string value)
    {
        if (LandAliases.TryGetValue(value, out var land))
        {
            value = land;
        }

        if (LandNicknames.TryGetValue(value, out var nickname))
        {
            return Card($"({nickname})");
        }

        string? card = value switch
        {
            "split" => "o.layout = 'split'",
            "flip" => "o.layout = 'flip'",
            "transform" or "tdfc" => "o.layout = 'transform'",
            "meld" => "o.layout = 'meld'",
            "meldpart" => $"o.layout = 'meld' and {MeldPart}",
            "meldresult" => $"o.layout = 'meld' and not {MeldPart}",
            "leveler" => "o.layout = 'leveler'",
            "dfc" or "doublefaced" => "o.layout in ('transform', 'modal_dfc', 'reversible_card', 'double_faced_token', 'art_series')",
            "mdfc" => "o.layout = 'modal_dfc'",
            "adventure" => "o.layout = 'adventure'",
            "prepare" or "prepared" => "o.layout = 'prepare'",
            "prototype" => "o.layout = 'prototype'",
            "mutate" => "o.layout = 'mutate'",
            "class" => "o.layout = 'class'",
            "case" => "o.layout = 'case'",
            "room" => $"{TypeLine} like '%enchantment — room%'",
            "saga" => $"{TypeLine} ~ '\\msaga\\M'",
            "token" => "o.layout in ('token', 'double_faced_token')",
            "emblem" => "o.layout = 'emblem'",
            "extra" => "o.is_extra",
            "spell" => $"o.type_line is not null and {FrontType} not like '%land%' and {TypeLine} !~ '\\m(plane|phenomenon|scheme|vanguard|conspiracy|dungeon|emblem|token|card|stickers|attraction|hero)\\M'",
            "permanent" => $"{FrontType} like any (array['%artifact%', '%creature%', '%enchantment%', '%land%', '%planeswalker%', '%battle%'])",
            "historic" => $"{TypeLine} like any (array['%legendary%', '%artifact%', '%saga%'])",
            "party" => $"{TypeLine} ~ '\\mcreature\\M[^/]*—[^/]*\\m(cleric|rogue|warrior|wizard)\\M'",
            "outlaw" => $"{TypeLine} ~ '\\mcreature\\M[^/]*—[^/]*\\m(assassin|mercenary|pirate|rogue|warlock)\\M'",
            "modal" => "coalesce(o.oracle_text, '') like '%•%'",
            "vanilla" => $"{TypeLine} like '%creature%' and exists (select 1 from {Faces} f where f.oracle_id = o.id and lower(f.type_line) like '%creature%' and coalesce(reference.remove_reminder_text(f.oracle_text), '') = '')",
            "frenchvanilla" => $$"""
                {{TypeLine}} ~ '\mcreature\M' and cardinality(o.keywords) > 0 and coalesce(o.oracle_text, '') <> ''
                  and regexp_replace(regexp_replace({{Text}}, '\m(' || array_to_string(o.keywords_normalized, '|') || ')\M', '', 'g'), '(from [a-z]+|\{[^}]*\}|[0-9]+)', '', 'g') ~ '^[\s,;—-]*$'
                """,
            "bear" => $"o.mana_value = 2 and o.power = '2' and o.toughness = '2' and {TypeLine} like '%creature%'",
            "hybrid" => @"split_part(coalesce(o.mana_cost, ''), ' // ', 1) ~ '\{([WUBRGC2]/[WUBRG])(/P)?\}'",
            "phyrexian" => @"(coalesce(o.mana_cost, '') ~ '/P\}|\{P\}' or coalesce(o.oracle_text, '') ~ '\{[WUBRGC]/P\}|\{[WUBRG]/[WUBRG]/P\}')",
            "commander" or "duelcommander" => $"""
                (({FrontType} like '%legendary%' and ({FrontType} like '%creature%' or ({FrontType} ~ '\m(vehicle|spacecraft)\M' and o.power is not null)))
                  or coalesce(o.oracle_text, '') like '%can be your commander%')
                  and coalesce(o.legalities->>'{(value == "commander" ? "commander" : "duel")}', '') <> 'banned'
                  and not (o.layout = 'meld' and not {MeldPart})
                """,
            "brawler" => $"""
                ({FrontType} ~ '\mlegendary\M' and {FrontType} ~ '\m(creature|planeswalker)\M')
                  or coalesce(o.oracle_text, '') like '%can be your commander%'
                """,
            "oathbreaker" => $"{FrontType} ~ '\\mplaneswalker\\M'",
            "companion" => "o.keywords_normalized @> array['companion']",
            "partner" => "exists (select 1 from unnest(o.keywords_normalized) k where k like 'partner%' or k in ('friends forever', 'choose a background', 'doctor''s companion'))",
            "gamechanger" => "o.game_changer",
            "reserved" => "o.reserved",
            "unique" => "o.id in (select x.oracle_id from reference.printings x where x.default_lang group by x.oracle_id having count(distinct x.set_code) = 1)",
            "rebalanced" => "o.name like 'A-%'",
            "funny" => "o.is_funny",
            _ => null,
        };

        if (card is not null)
        {
            return Card($"({card})");
        }

        string? printing = value switch
        {
            "reprint" => "p.reprint",
            "promo" => "p.promo",
            "digital" => "p.digital",
            "spotlight" => "p.story_spotlight",
            "booster" => "p.booster",
            "hires" => "coalesce(p.image_status = 'highres_scan', false)",
            "foil" => "'foil' = any (p.finishes)",
            "nonfoil" => "'nonfoil' = any (p.finishes)",
            "etched" => "'etched' = any (p.finishes)",
            "glossy" => "'glossy' = any (p.promo_types)",
            "full" or "fullart" => "p.full_art",
            "textless" => "p.textless",
            "oversized" => "p.oversized",
            "variation" => "p.variation",
            "alchemy" => "(p.set_type = 'alchemy' or 'alchemy' = any (p.promo_types))",
            "new" => "coalesce(p.frame in ('2003', '2015', 'future'), false)",
            "old" => "coalesce(p.frame in ('1993', '1997'), false)",
            "modern" => "coalesce(p.frame = '2003', false)",
            "future" => "coalesce(p.frame = 'future', false)",
            "universesbeyond" or "ub" => "('universesbeyond' = any (p.promo_types) or coalesce(p.security_stamp = 'triangle', false))",
            "masterpiece" => "p.set_type = 'masterpiece'",
            "atypical" => Atypical,
            "default" or "typical" => $"not {Atypical}",
            _ => null,
        };

        return printing is null ? null : Printing($"({printing})");
    }

    private Fragment? HasTerm(TermNode term, string value) => value switch
    {
        "indicator" => Card($"(o.color_indicator is not null or exists (select 1 from {Faces} f where f.oracle_id = o.id and f.color_indicator is not null))"),
        "watermark" => Printing("(p.watermark is not null)"),
        "flavor" => Printing("(p.flavor_text is not null)"),
        "stamp" or "securitystamp" => Printing("(p.security_stamp is not null)"),
        "artist" => Printing("(p.artist is not null)"),
        _ => Warn(term, SearchWarningCodes.InvalidValue, $"The search does not know the value \"{term.Value}\" of the keyword \"has\". The search ignores the term \"{TermText(term)}\"."),
    };
}
