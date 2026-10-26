using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Reliquary.Cards.Text;

namespace Reliquary.Cards.Search;

/// <summary>
/// The display options of a search request, from the query parameters of the endpoint.
/// The keywords of the query win over these options.
/// </summary>
/// <param name="Unique">The <c>unique</c> parameter.</param>
/// <param name="Order">The <c>order</c> parameter.</param>
/// <param name="Direction">The <c>dir</c> parameter.</param>
internal sealed record SearchDefaults(SearchUnique Unique = SearchUnique.Cards, SearchOrder Order = SearchOrder.Name, SearchDirection Direction = SearchDirection.Auto);

/// <summary>
/// A search query that the compiler changed into SQL conditions and options.
/// </summary>
internal sealed class CompiledSearch
{
    /// <summary>Gets the condition on the Oracle card (alias <c>o</c>), or <see langword="null"/> when there is none.</summary>
    public string? CardCondition { get; init; }

    /// <summary>Gets the condition on the printing (alias <c>p</c>) and the Oracle card (alias <c>o</c>), or <see langword="null"/>.</summary>
    public string? PrintingCondition { get; init; }

    /// <summary>Gets a value indicating whether the query gives a language with <c>lang:</c>. Then the search does not limit the printings to the default language.</summary>
    public bool LanguageGiven { get; init; }

    /// <summary>Gets a value indicating whether the search includes the extra cards.</summary>
    public bool IncludeExtras { get; init; }

    /// <summary>Gets an SQL expression on the Oracle card that gives the language of a matching localized name, or <see langword="null"/>.</summary>
    public string? NameLanguage { get; init; }

    /// <summary>Gets how the search removes the duplicate results.</summary>
    public SearchUnique Unique { get; init; }

    /// <summary>Gets the sort order.</summary>
    public SearchOrder Order { get; init; }

    /// <summary>Gets the sort direction.</summary>
    public SearchDirection Direction { get; init; }

    /// <summary>Gets the printing that the search shows for each card.</summary>
    public SearchPreference Prefer { get; init; }

    /// <summary>Gets the parameters of the SQL conditions.</summary>
    public required IReadOnlyDictionary<string, object> Parameters { get; init; }

    /// <summary>Gets the warnings about the terms that the search ignored.</summary>
    public required IReadOnlyList<SearchWarning> Warnings { get; init; }

    /// <summary>Gets the parameters that contain a regular expression, with the position of their term in the query.</summary>
    public required IReadOnlyList<(string Parameter, int Position)> Regexes { get; init; }

    /// <summary>Gets a value indicating whether the query has no search condition, for example when the search ignored all the terms.</summary>
    public bool IsEmpty => CardCondition is null && PrintingCondition is null && !LanguageGiven && !IncludeExtras;

    /// <summary>Gets a value indicating whether the conditions use the printings.</summary>
    public bool UsesPrintings => PrintingCondition is not null || LanguageGiven;
}

/// <summary>
/// Changes the syntax tree of a search query into one parameterized SQL condition.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Each term gives a condition on the Oracle card (alias <c>o</c>) or on a printing (alias <c>p</c>).
/// A card matches when one of its printings matches the full condition, as on Scryfall.</item>
/// <item>The SQL text never contains a value of the query. Each value goes into a parameter.</item>
/// <item>The compiler ignores a term with an unknown keyword or a value that is not valid, and gives a warning, as Scryfall does.</item>
/// </list>
/// </remarks>
internal sealed partial class SearchCompiler
{
    private const string Faces = "reference.card_faces";

    private static readonly Dictionary<string, string> KeywordAliases = new(StringComparer.Ordinal)
    {
        ["name"] = "name",
        ["n"] = "name",
        ["t"] = "type",
        ["type"] = "type",
        ["o"] = "oracle",
        ["oracle"] = "oracle",
        ["fo"] = "fulloracle",
        ["fulloracle"] = "fulloracle",
        ["kw"] = "keyword",
        ["keyword"] = "keyword",
        ["c"] = "color",
        ["color"] = "color",
        ["colors"] = "color",
        ["id"] = "identity",
        ["identity"] = "identity",
        ["ci"] = "identity",
        ["commander"] = "identity",
        ["m"] = "mana",
        ["mana"] = "mana",
        ["mv"] = "manavalue",
        ["manavalue"] = "manavalue",
        ["cmc"] = "manavalue",
        ["devotion"] = "devotion",
        ["produces"] = "produces",
        ["pow"] = "power",
        ["power"] = "power",
        ["tou"] = "toughness",
        ["toughness"] = "toughness",
        ["pt"] = "pt",
        ["powtou"] = "pt",
        ["loy"] = "loyalty",
        ["loyalty"] = "loyalty",
        ["def"] = "defense",
        ["defense"] = "defense",
        ["is"] = "is",
        ["not"] = "not",
        ["has"] = "has",
        ["include"] = "include",
        ["r"] = "rarity",
        ["rarity"] = "rarity",
        ["s"] = "set",
        ["e"] = "set",
        ["set"] = "set",
        ["edition"] = "set",
        ["cn"] = "number",
        ["number"] = "number",
        ["b"] = "block",
        ["block"] = "block",
        ["g"] = "group",
        ["group"] = "group",
        ["st"] = "settype",
        ["settype"] = "settype",
        ["in"] = "in",
        ["f"] = "format",
        ["format"] = "format",
        ["legal"] = "format",
        ["banned"] = "banned",
        ["restricted"] = "restricted",
        ["usd"] = "usd",
        ["eur"] = "eur",
        ["tix"] = "tix",
        ["cheapest"] = "cheapest",
        ["a"] = "artist",
        ["artist"] = "artist",
        ["artists"] = "artists",
        ["ft"] = "flavor",
        ["flavor"] = "flavor",
        ["wm"] = "watermark",
        ["watermark"] = "watermark",
        ["new"] = "new",
        ["border"] = "border",
        ["frame"] = "frame",
        ["stamp"] = "stamp",
        ["game"] = "game",
        ["year"] = "year",
        ["date"] = "date",
        ["art"] = "arttag",
        ["atag"] = "arttag",
        ["arttag"] = "arttag",
        ["function"] = "oracletag",
        ["otag"] = "oracletag",
        ["oracletag"] = "oracletag",
        ["prints"] = "prints",
        ["paperprints"] = "paperprints",
        ["sets"] = "sets",
        ["papersets"] = "papersets",
        ["illustrations"] = "illustrations",
        ["lang"] = "lang",
        ["language"] = "lang",
        ["edhrec"] = "edhrec",
        ["edhrecrank"] = "edhrec",
        ["penny"] = "penny",
        ["pennyrank"] = "penny",
        ["oracleid"] = "oracleid",
        ["unique"] = "unique",
        ["order"] = "order",
        ["sort"] = "order",
        ["dir"] = "direction",
        ["direction"] = "direction",
        ["prefer"] = "prefer",
        ["display"] = "display",
        ["cube"] = "cube",
    };

    private readonly string query;
    private readonly Dictionary<string, object> parameters = [];
    private readonly List<SearchWarning> warnings = [];
    private readonly List<(string Parameter, int Position)> regexes = [];
    private bool languageGiven;
    private bool includeExtras;
    private bool revealsExtras;
    private SearchUnique unique;
    private SearchOrder order;
    private SearchDirection direction;
    private SearchPreference prefer = SearchPreference.Default;

    private SearchCompiler(string query, SearchDefaults defaults)
    {
        this.query = query;
        unique = defaults.Unique;
        order = defaults.Order;
        direction = defaults.Direction;
    }

    /// <summary>
    /// Compiles a syntax tree.
    /// </summary>
    /// <param name="query">The text of the query, for the warnings.</param>
    /// <param name="root">The syntax tree, or <see langword="null"/> for an empty query.</param>
    /// <param name="defaults">The display options of the query parameters.</param>
    /// <returns>The compiled search.</returns>
    public static CompiledSearch Compile(string query, SearchNode? root, SearchDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(defaults);
        var compiler = new SearchCompiler(query, defaults);
        var cardParts = new List<string>();
        var printingParts = new List<string>();
        var names = new List<NameNode>();

        foreach (var conjunct in root is null ? [] : Conjuncts(root))
        {
            if (compiler.CompileNode(conjunct, negated: false) is not { } fragment)
            {
                continue;
            }

            (fragment.Printing ? printingParts : cardParts).Add(fragment.Sql);
            if (conjunct is NameNode { Match: NameMatch.Contains or NameMatch.Exact } name)
            {
                names.Add(name);
            }
            else if (conjunct is TermNode { Keyword: "name", Kind: not SearchValueKind.Regex } term)
            {
                names.Add(new NameNode(term.Value, NameMatch.Contains, term.Position, term.Length));
            }
        }

        return new CompiledSearch
        {
            CardCondition = cardParts.Count == 0 ? null : string.Join("\n  and ", cardParts),
            PrintingCondition = printingParts.Count == 0 ? null : string.Join("\n  and ", printingParts),
            LanguageGiven = compiler.languageGiven,
            IncludeExtras = compiler.includeExtras || compiler.revealsExtras,
            NameLanguage = compiler.languageGiven || names.Count == 0 ? null : compiler.NameLanguage(names),
            Unique = compiler.unique,
            Order = compiler.order,
            Direction = compiler.direction,
            Prefer = compiler.prefer,
            Parameters = compiler.parameters,
            Warnings = compiler.warnings,
            Regexes = compiler.regexes,
        };
    }

    private static IEnumerable<SearchNode> Conjuncts(SearchNode node) =>
        node is AndNode and ? and.Children.SelectMany(Conjuncts) : [node];

    private static string Sql(SearchOperator op) => op switch
    {
        SearchOperator.Colon or SearchOperator.Equal => "=",
        SearchOperator.NotEqual => "<>",
        SearchOperator.Less => "<",
        SearchOperator.LessOrEqual => "<=",
        SearchOperator.Greater => ">",
        SearchOperator.GreaterOrEqual => ">=",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static string Contains(string text) =>
        "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";

    private static bool TryNumber(string value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private static string Lower(string value) => value.ToLowerInvariant();

    private string Param(object value)
    {
        var name = "p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
        parameters[name] = value;
        return "@" + name;
    }

    private string RegexParam(string pattern, int position, out bool usesName)
    {
        var parameter = Param(RegexTranslator.Translate(pattern, out usesName));
        regexes.Add((parameter[1..], position));
        return parameter;
    }

    private string TermText(SearchNode node) =>
        node.Position >= 0 && node.Position + node.Length <= query.Length ? query.Substring(node.Position, node.Length) : string.Empty;

    private Fragment? Warn(TermNode term, string code, string message)
    {
        warnings.Add(new SearchWarning(code, message, term.Position, TermText(term)));
        return null;
    }

    private Fragment? InvalidValue(TermNode term) =>
        Warn(term, SearchWarningCodes.InvalidValue, $"The value \"{term.Value}\" is not valid for the keyword \"{term.Keyword}\". The search ignores the term \"{TermText(term)}\".");

    private Fragment? InvalidOperator(TermNode term) =>
        Warn(term, SearchWarningCodes.InvalidOperator, $"The keyword \"{term.Keyword}\" does not accept this operator. The search ignores the term \"{TermText(term)}\". Use \":\" or \"=\".");

    private Fragment? CompileNode(SearchNode node, bool negated)
    {
        switch (node)
        {
            case AndNode and:
                return Combine([.. and.Children.Select(child => CompileNode(child, negated))], "and");
            case OrNode or:
                return Combine([.. or.Children.Select(child => CompileNode(child, negated))], "or");
            case NotNode not:
                return CompileNode(not.Operand, !negated) is { } operand ? Negate(operand) : null;
            case NameNode name:
                return Card(NameCondition("o", name, out _));
            case TermNode term:
                return CompileTerm(term, negated);
            default:
                throw new ArgumentOutOfRangeException(nameof(node));
        }
    }

    private static Fragment? Combine(List<Fragment?> children, string separator)
    {
        var present = children.OfType<Fragment>().ToList();
        return present.Count switch
        {
            0 => null,
            1 => present[0],
            _ => new Fragment("(" + string.Join($"\n  {separator} ", present.Select(child => child.Sql)) + ")", present.Any(child => child.Printing)),
        };
    }

    /// <summary>
    /// Negates a condition. A condition can be null, for example a comparison with a missing power: its negation is true.
    /// </summary>
    private static Fragment Negate(Fragment fragment) => fragment with { Sql = $"not coalesce(({fragment.Sql}), false)" };

    private static Fragment Card(string sql) => new(sql, Printing: false);

    private static Fragment Printing(string sql) => new(sql, Printing: true);

    private Fragment? CompileTerm(TermNode term, bool negated)
    {
        if (!KeywordAliases.TryGetValue(term.Keyword, out var keyword))
        {
            return Warn(term, SearchWarningCodes.UnknownKeyword, $"The keyword \"{term.Keyword}\" is not known. The search ignores the term \"{TermText(term)}\".");
        }

        var value = term.Value;
        return keyword switch
        {
            "name" => RevealExtras(negated, Card(NameCondition("o", new NameNode(value, term.Kind == SearchValueKind.Regex ? NameMatch.Regex : NameMatch.Contains, term.Position, term.Length), out _))),
            "type" => TypeTerm(term, negated),
            "oracle" => TextTerm(term, plain: true),
            "fulloracle" => TextTerm(term, plain: false),
            "keyword" => Card($"o.keywords_normalized @> array[{Param(Lower(value))}::text]"),
            "color" => ColorTerm(term, "o.colors_mask", "o.colors", identity: false, allowColorless: false),
            "identity" => ColorTerm(term, "o.color_identity_mask", "o.color_identity", identity: true, allowColorless: false),
            "produces" => ColorTerm(term, "o.produced_mana_mask", "o.produced_mana", identity: false, allowColorless: true),
            "mana" => ManaTerm(term),
            "manavalue" => ManaValueTerm(term),
            "devotion" => DevotionTerm(term),
            "power" or "toughness" or "loyalty" or "defense" or "pt" => StatTerm(term, keyword),
            "is" => IsTerm(term, Lower(value), negated),
            "not" => IsTerm(term, Lower(value), !negated) is { } positive ? Negate(positive) : null,
            "has" => HasTerm(term, Lower(value)),
            "include" => IncludeTerm(term),
            "rarity" => RarityTerm(term),
            "set" => SetTerm(term, negated),
            "number" => NumberTerm(term),
            "block" => BlockTerm(term, negated),
            "group" => GroupTerm(term, negated),
            "settype" => SetTypeTerm(term),
            "in" => InTerm(term),
            "format" => FormatTerm(term, "legal", "restricted"),
            "banned" => RevealExtras(negated, FormatTerm(term, "banned")),
            "restricted" => RevealExtras(negated, FormatTerm(term, "restricted")),
            "usd" or "eur" or "tix" => PriceTerm(term, keyword),
            "cheapest" => CheapestTerm(term),
            "artist" => RevealExtras(negated, term.Kind == SearchValueKind.Regex
                ? Printing($"(p.artist is not null and lower(p.artist) ~* {RegexParam(value, term.Position, out _)})")
                : Printing($"(p.artist is not null and lower(p.artist) like {Param(Contains(Lower(value)))})")),
            "artists" => CountTerm(term, "cardinality(string_to_array(coalesce(p.artist, ''), ' & '))", printing: true),
            "flavor" => term.Kind == SearchValueKind.Regex
                ? Printing($"(p.flavor_text is not null and lower(p.flavor_text) ~* {RegexParam(value, term.Position, out _)})")
                : Printing($"(p.flavor_text is not null and lower(p.flavor_text) like {Param(Contains(Lower(value)))})"),
            "watermark" => WatermarkTerm(Lower(value)),
            "new" => NewTerm(term, Lower(value)),
            "border" => Printing($"coalesce(p.border_color = {Param(Lower(value))}, false)"),
            "frame" => FrameTerm(term, Lower(value)),
            "stamp" => Printing($"coalesce(p.security_stamp = {Param(Lower(value))}, false)"),
            "game" => SearchVocabulary.Games.Contains(Lower(value))
                ? Printing($"p.games @> array[{Param(Lower(value))}::text]")
                : InvalidValue(term),
            "year" => YearTerm(term),
            "date" => DateTerm(term),
            "arttag" => ArtTagTerm(term),
            "oracletag" => OracleTagTerm(term),
            "prints" => PrintCountTerm(term, "count(*)", paper: false),
            "paperprints" => PrintCountTerm(term, "count(*)", paper: true),
            "sets" => PrintCountTerm(term, "count(distinct x.set_code)", paper: false),
            "papersets" => PrintCountTerm(term, "count(distinct x.set_code)", paper: true),
            "illustrations" => PrintCountTerm(term, "count(distinct x.illustration_id)", paper: false),
            "lang" => LanguageTerm(term),
            "edhrec" => RankTerm(term, "o.edhrec_rank"),
            "penny" => RankTerm(term, "o.penny_rank"),
            "oracleid" => Guid.TryParse(value, out var oracleId) ? Card($"o.id = {Param(oracleId)}") : InvalidValue(term),
            "unique" or "order" or "direction" or "prefer" or "display" => DisplayTerm(term, keyword, Lower(value)),
            "cube" => Warn(term, SearchWarningCodes.UnsupportedKeyword, $"Reliquary has no data for the keyword \"{term.Keyword}\". The search ignores the term \"{TermText(term)}\"."),
            _ => throw new InvalidOperationException($"The keyword {keyword} has no compiler."),
        };
    }

    /// <summary>
    /// Gives the condition of a name term on the names of the table <c>card_names</c> or on an alias of it.
    /// </summary>
    private string NameCondition(string cardAlias, NameNode name, out string nameCondition)
    {
        string condition;
        switch (name.Match)
        {
            case NameMatch.Exact:
                condition = $"n.name_normalized = {Param(NameNormalizer.Normalize(name.Value))}";
                break;
            case NameMatch.Regex:
                condition = $"n.name_normalized ~* {RegexParam(name.Value, name.Position, out _)}";
                break;
            default:
                condition = $"n.name_normalized like {Param(Contains(NameNormalizer.Normalize(name.Value)))}";
                break;
        }

        nameCondition = condition;
        return $"{cardAlias}.id in (select n.oracle_id from reference.card_names n where {condition})";
    }

    /// <summary>
    /// Gives an SQL expression that finds the language of a localized name that matches all the name terms,
    /// when no English name of the card matches them.
    /// </summary>
    private string NameLanguage(List<NameNode> names)
    {
        var conditions = names.Select(name =>
        {
            NameCondition("o", name, out var condition);
            return condition;
        }).ToList();
        var localized = string.Join(" and ", conditions);
        var english = string.Join(" and ", conditions.Select(condition => condition.Replace("n.name_normalized", "e.name_normalized", StringComparison.Ordinal)));
        return $"""
            (select n.lang from reference.card_names n
             where n.oracle_id = o.id and n.lang <> 'en' and {localized}
               and not exists (select 1 from reference.card_names e where e.oracle_id = o.id and e.lang = 'en' and {english})
             order by n.lang limit 1)
            """;
    }

    private Fragment? TypeTerm(TermNode term, bool negated)
    {
        if (term.Kind == SearchValueKind.Regex)
        {
            return Card($"lower(o.type_line) ~* {RegexParam(term.Value, term.Position, out _)}");
        }

        var value = Lower(term.Value);
        if (!negated && SearchVocabulary.ExtraTypes.Contains(value))
        {
            revealsExtras = true;
        }

        if (term.Operator is not (SearchOperator.Colon or SearchOperator.Equal))
        {
            return InvalidOperator(term);
        }

        // A phrase matches a part of the type line. A word matches the start of a word: "t:legend" finds "Legendary".
        // A card type, a supertype or "plane" must be a full word, so "t:plane" does not find "Planet" or "Planeswalker".
        var contains = Card($"lower(o.type_line) like {Param(Contains(value))}");
        if (!TypeWordPattern().IsMatch(value))
        {
            return contains;
        }

        // The word contains only letters, digits, apostrophes and hyphens, so it needs no escape in a regular expression.
        var word = @"\m" + value + (SearchVocabulary.TypeWords.Contains(value) ? @"\M" : string.Empty);
        return Card($"({contains.Sql} and lower(o.type_line) ~ {Param(word)})");
    }

    private Fragment? TextTerm(TermNode term, bool plain)
    {
        var column = plain ? "o.oracle_text_plain" : "o.oracle_text";
        var faceText = plain ? "reference.remove_reminder_text(f.oracle_text)" : "f.oracle_text";
        if (term.Kind == SearchValueKind.Regex)
        {
            var pattern = RegexParam(term.Value, term.Position, out var usesName);
            if (!usesName)
            {
                return Card($"lower({column}) ~* {pattern}");
            }

            var names = $"""'(?:' || regexp_replace(lower(f.name), {Param(@"([][\\.^$*+?(){}|])")}, {Param(@"\\\1")}, 'g') || '|' || regexp_replace(lower(split_part(f.name, ',', 1)), {Param(@"([][\\.^$*+?(){}|])")}, {Param(@"\\\1")}, 'g') || '|' || {Param(SearchVocabulary.SelfReferencePattern[2..^2])} || ')'""";
            return Card($"({column} is not null and coalesce((select bool_or(lower({faceText}) ~* replace({pattern}, chr(1), {names})) from {Faces} f where f.oracle_id = o.id and f.oracle_text is not null), false))");
        }

        if (term.Operator is not (SearchOperator.Colon or SearchOperator.Equal))
        {
            return InvalidOperator(term);
        }

        var value = Lower(term.Value);
        if (!value.Contains('~', StringComparison.Ordinal))
        {
            return Card($"lower({column}) like {Param(Contains(value))}");
        }

        // "~" is the name of the card. The search replaces the name and the words "this creature", "this land" and others with "~".
        var longest = value.Split('~').Select(piece => piece.Trim()).MaxBy(piece => piece.Length) ?? string.Empty;
        var prefilter = longest.Length >= 3 ? $"lower({column}) like {Param(Contains(longest))} and " : string.Empty;
        var self = $"regexp_replace(replace(replace(lower({faceText}), lower(f.name), '~'), lower(split_part(f.name, ',', 1)), '~'), {Param(SearchVocabulary.SelfReferencePattern)}, '~', 'g')";
        // A scalar subquery, not "exists": the planner then checks the faces only for the cards that match the prefilter.
        return Card($"({column} is not null and {prefilter}coalesce((select bool_or({self} like {Param(Contains(value))}) from {Faces} f where f.oracle_id = o.id and f.oracle_text is not null), false))");
    }

    /// <summary>
    /// Compiles a color term. The conditions use the operators of the arrays and the equality of the masks,
    /// because PostgreSQL has statistics for them and estimates the number of matching cards correctly.
    /// </summary>
    private Fragment? ColorTerm(TermNode term, string mask, string array, bool identity, bool allowColorless)
    {
        var value = Lower(term.Value);
        string? special = null;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            special = $"cardinality({array}) {Sql(term.Operator)} {Param(count)}";
        }
        else if (value is "m" or "multicolor" or "multicolored")
        {
            special = term.Operator == SearchOperator.NotEqual ? $"cardinality({array}) < 2" : $"cardinality({array}) >= 2";
        }
        else if (!allowColorless && value is "c" or "colorless")
        {
            special = term.Operator == SearchOperator.NotEqual ? $"{mask} <> 0" : $"{mask} = 0";
        }

        if (special is not null)
        {
            return identity || allowColorless ? Card(special) : FaceColors(special, mask, array);
        }

        if (!TryColorMask(value, allowColorless, out var bits))
        {
            return Warn(term, SearchWarningCodes.InvalidValue, $"The value \"{term.Value}\" is not a color. The search ignores the term \"{TermText(term)}\". Use color letters such as \"wu\", or names such as \"azorius\".");
        }

        var letters = Param(SearchVocabulary.ColorBits.Where(color => (bits & color.Value) != 0).OrderBy(color => color.Value).Select(color => char.ToUpperInvariant(color.Key).ToString()).ToArray());
        var exact = Param((short)bits);
        var superset = $"{array} @> {letters}::text[]";
        var subset = $"{array} <@ {letters}::text[]";
        var condition = term.Operator switch
        {
            SearchOperator.Colon => identity ? subset : superset,
            SearchOperator.Equal => $"{mask} = {exact}",
            SearchOperator.NotEqual => $"{mask} <> {exact}",
            SearchOperator.GreaterOrEqual => superset,
            SearchOperator.Greater => $"{superset} and {mask} <> {exact}",
            SearchOperator.LessOrEqual => subset,
            SearchOperator.Less => $"{subset} and {mask} <> {exact}",
            _ => throw new ArgumentOutOfRangeException(nameof(term)),
        };
        return identity || allowColorless ? Card($"({condition})") : FaceColors(condition, mask, array);
    }

    /// <summary>
    /// Applies a color condition to the faces of a double-faced card. Scryfall gives no colors to a double-faced card,
    /// only to its faces, so one face must match the condition. The other cards use their own colors.
    /// </summary>
    private Fragment FaceColors(string condition, string mask, string array)
    {
        var faceCondition = condition.Replace(mask, "f.colors_mask", StringComparison.Ordinal).Replace(array, "f.colors", StringComparison.Ordinal);
        var layouts = Param(SearchVocabulary.FaceColorLayouts);
        return Card($"""
            ((o.layout <> all ({layouts}::text[]) and {condition})
             or (o.layout = any ({layouts}::text[])
                 and exists (select 1 from {Faces} f where f.oracle_id = o.id and {faceCondition})))
            """);
    }

    private static bool TryColorMask(string value, bool allowColorless, out int mask)
    {
        mask = 0;
        if (SearchVocabulary.ColorNames.TryGetValue(value, out var letters))
        {
            value = letters;
        }
        else if (allowColorless && value == "colorless")
        {
            value = "c";
        }

        if (value.Length == 0)
        {
            return false;
        }

        foreach (var letter in value)
        {
            if (!SearchVocabulary.ColorBits.TryGetValue(letter, out var bit) || (bit == 32 && !allowColorless))
            {
                return false;
            }

            mask |= bit;
        }

        return true;
    }

    private Fragment? ManaTerm(TermNode term)
    {
        if (!TryParseMana(term.Value, out var generic, out var symbols))
        {
            return Warn(term, SearchWarningCodes.InvalidValue, $"The value \"{term.Value}\" is not a mana cost. The search ignores the term \"{TermText(term)}\". Use symbols such as \"{{2}}{{W}}\" or \"2WW\".");
        }

        var g = Param(generic);
        var s = Param(Numbered(symbols));
        var atLeast = $"f.mana_generic >= {g} and f.mana_symbols @> {s}::text[]";
        var atMost = $"f.mana_generic <= {g} and f.mana_symbols <@ {s}::text[]";
        var equal = $"f.mana_generic = {g} and f.mana_symbols @> {s}::text[] and f.mana_symbols <@ {s}::text[]";
        var condition = term.Operator switch
        {
            SearchOperator.Colon or SearchOperator.GreaterOrEqual => atLeast,
            SearchOperator.Equal => equal,
            SearchOperator.NotEqual => $"not ({equal})",
            SearchOperator.Greater => $"{atLeast} and not ({equal})",
            SearchOperator.LessOrEqual => atMost,
            SearchOperator.Less => $"{atMost} and not ({equal})",
            _ => throw new ArgumentOutOfRangeException(nameof(term)),
        };
        return Card($"exists (select 1 from {Faces} f where f.oracle_id = o.id and f.mana_cost is not null and {condition})");
    }

    /// <summary>
    /// Reads a mana cost such as <c>{2}{W}{U/P}</c> or the short form <c>2WU</c>.
    /// </summary>
    internal static bool TryParseMana(string value, out int generic, out List<string> symbols)
    {
        generic = 0;
        symbols = [];
        var index = 0;
        while (index < value.Length)
        {
            var character = value[index];
            if (character == '{')
            {
                var end = value.IndexOf('}', index);
                if (end < 0)
                {
                    return false;
                }

                var symbol = value[(index + 1)..end].ToUpperInvariant();
                index = end + 1;
                if (symbol.Length == 0)
                {
                    return false;
                }

                if (int.TryParse(symbol, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    generic += number;
                }
                else
                {
                    symbols.Add(CanonicalSymbol(symbol));
                }
            }
            else if (char.IsAsciiDigit(character))
            {
                var start = index;
                while (index < value.Length && char.IsAsciiDigit(value[index]))
                {
                    index++;
                }

                generic += int.Parse(value[start..index], CultureInfo.InvariantCulture);
            }
            else if ("WUBRGCXYZSwubrgcxyzs".Contains(character, StringComparison.Ordinal))
            {
                symbols.Add(char.ToUpperInvariant(character).ToString());
                index++;
            }
            else
            {
                return false;
            }
        }

        return generic > 0 || symbols.Count > 0;
    }

    private static string CanonicalSymbol(string symbol)
    {
        var parts = symbol.Split('/');
        if (parts.Length >= 2)
        {
            // {P/R} becomes {R/P}. {B/U} becomes {U/B}.
            if (parts[0] == "P")
            {
                (parts[0], parts[^1]) = (parts[^1], parts[0]);
            }

            var pair = parts[0] + "/" + parts[1];
            var reversed = parts[1] + "/" + parts[0];
            if (!SearchVocabulary.HybridPairs.Contains(pair) && SearchVocabulary.HybridPairs.Contains(reversed))
            {
                (parts[0], parts[1]) = (parts[1], parts[0]);
            }

            return string.Join('/', parts);
        }

        return symbol;
    }

    /// <summary>
    /// Numbers the repeated symbols as the SQL function <c>reference.mana_symbols</c> does: W, W gives W#1, W#2.
    /// </summary>
    private static string[] Numbered(List<string> symbols) =>
        [.. symbols.GroupBy(symbol => symbol, StringComparer.Ordinal).SelectMany(group => group.Select((symbol, index) => symbol + "#" + (index + 1).ToString(CultureInfo.InvariantCulture)))];

    private Fragment? ManaValueTerm(TermNode term)
    {
        var value = Lower(term.Value);
        if (value is "even" or "odd")
        {
            return Card($"mod(o.mana_value, 2) = {(value == "even" ? 0 : 1)}");
        }

        return TryNumber(value, out var number)
            ? Card($"o.mana_value {Sql(term.Operator)} {Param(number)}")
            : InvalidValue(term);
    }

    private Fragment? DevotionTerm(TermNode term)
    {
        if (!TryParseMana(term.Value, out _, out var symbols) || symbols.Count == 0)
        {
            return InvalidValue(term);
        }

        var colors = string.Concat("WUBRG".Where(color => symbols.Any(symbol => symbol.Contains(color, StringComparison.Ordinal))));
        if (colors.Length == 0)
        {
            return InvalidValue(term);
        }

        var op = term.Operator == SearchOperator.Colon ? ">=" : Sql(term.Operator);
        return Card($"""
            ({IsCondition("permanent")!.Value.Sql} and o.id in (select f.oracle_id from {Faces} f where f.mana_cost ~ {Param($"[{colors}]")}
              and (select count(*) from unnest(f.mana_symbols) symbol where symbol ~ {Param($"^[^#]*[{colors}]")}) {op} {Param(symbols.Count)}))
            """);
    }

    private Fragment? StatTerm(TermNode term, string field)
    {
        static string Expression(string field) => field switch
        {
            "pt" => "(reference.stat_value(f.power) + reference.stat_value(f.toughness))",
            _ => $"reference.stat_value(f.{field})",
        };

        static string Present(string field) => field switch
        {
            "pt" => "f.power is not null and f.toughness is not null",
            _ => $"f.{field} is not null",
        };

        var value = Lower(term.Value);
        string condition;
        string? other = null;
        if (KeywordAliases.TryGetValue(value, out var alias) && alias is "power" or "toughness" or "loyalty" or "defense" or "pt")
        {
            // Scryfall compares the values of all the faces, for example the power of the back face with the toughness of the front face.
            other = alias;
            var right = Expression(other).Replace("f.", "g.", StringComparison.Ordinal);
            var rightPresent = Present(other).Replace("f.", "g.", StringComparison.Ordinal);
            condition = $"{Present(field)} and exists (select 1 from {Faces} g where g.oracle_id = o.id and {rightPresent} and {Expression(field)} {Sql(term.Operator)} {right})";
        }
        else if (TryNumber(value, out var number))
        {
            condition = $"{Present(field)} and {Expression(field)} {Sql(term.Operator)} {Param(number)}";
        }
        else
        {
            return InvalidValue(term);
        }

        if (field == "pt" || other == "pt")
        {
            // Scryfall computes the total of power and toughness on the front face only.
            condition += " and f.face_index = 0";
        }

        return Card($"exists (select 1 from {Faces} f where f.oracle_id = o.id and {condition})");
    }

    private Fragment? IncludeTerm(TermNode term)
    {
        if (Lower(term.Value) != "extras")
        {
            return InvalidValue(term);
        }

        includeExtras = true;
        return null;
    }

    private Fragment? RarityTerm(TermNode term) =>
        SearchVocabulary.Rarities.TryGetValue(Lower(term.Value), out var rank)
            ? Printing($"p.rarity_rank {Sql(term.Operator)} {Param(rank)}")
            : Warn(term, SearchWarningCodes.InvalidValue, $"The value \"{term.Value}\" is not a rarity. The search ignores the term \"{TermText(term)}\". Use common, uncommon, rare, special, mythic or bonus.");

    private Fragment? SetTerm(TermNode term, bool negated)
    {
        if (term.Operator is not (SearchOperator.Colon or SearchOperator.Equal or SearchOperator.NotEqual))
        {
            return InvalidOperator(term);
        }

        RevealExtras(negated);
        return Printing($"p.set_code {Sql(term.Operator)} {Param(Lower(term.Value))}");
    }

    /// <summary>
    /// Includes the extra cards and the memorabilia printings, when the term is not negated. Scryfall does the same for
    /// the terms that target a set, an artist, a ban or the Reserved List.
    /// </summary>
    private void RevealExtras(bool negated)
    {
        if (!negated)
        {
            revealsExtras = true;
        }
    }

    private Fragment? RevealExtras(bool negated, Fragment? fragment)
    {
        if (fragment is not null)
        {
            RevealExtras(negated);
        }

        return fragment;
    }

    private Fragment? NumberTerm(TermNode term)
    {
        if (term.Operator is SearchOperator.Colon or SearchOperator.Equal or SearchOperator.NotEqual)
        {
            return Printing($"lower(p.collector_number) {Sql(term.Operator)} {Param(Lower(term.Value))}");
        }

        return int.TryParse(term.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? Printing($"coalesce(p.collector_number_value {Sql(term.Operator)} {Param(number)}, false)")
            : InvalidValue(term);
    }

    private Fragment? BlockTerm(TermNode term, bool negated)
    {
        RevealExtras(negated);
        var code = Param(Lower(term.Value));
        return Printing($"""
            p.set_code in (select s.code from reference.sets s
                           where s.block_code = (select b.block_code from reference.sets b where b.code = {code})
                              or s.code = {code})
            """);
    }

    private Fragment? GroupTerm(TermNode term, bool negated)
    {
        RevealExtras(negated);
        var code = Param(Lower(term.Value));
        return Printing($"""
            p.set_code in (
                with recursive ancestors (code, parent) as (
                    select s.code, s.parent_set_code from reference.sets s where s.code = {code}
                    union
                    select s.code, s.parent_set_code from reference.sets s join ancestors a on s.code = a.parent
                ),
                family (code) as (
                    select a.code from ancestors a where a.parent is null
                    union
                    select s.code from reference.sets s join family on s.parent_set_code = family.code
                )
                select family.code from family)
            """);
    }

    private Fragment? SetTypeTerm(TermNode term)
    {
        if (!SearchVocabulary.SetTypes.TryGetValue(Lower(term.Value).Replace("_", string.Empty, StringComparison.Ordinal), out var type))
        {
            return InvalidValue(term);
        }

        return Printing($"p.set_type {(term.Operator == SearchOperator.NotEqual ? "<>" : "=")} {Param(type)}");
    }

    private Fragment? InTerm(TermNode term)
    {
        var value = Lower(term.Value);
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal);
        string condition;
        if (SearchVocabulary.Games.Contains(value))
        {
            condition = $"x.games @> array[{Param(value)}::text]";
        }
        else if (SearchVocabulary.Rarities.TryGetValue(value, out var rank) && value.Length > 1)
        {
            // Scryfall does not count the printings of Secret Lair and the other box sets for in:rare.
            condition = $"x.rarity_rank = {Param(rank)} and x.set_type <> 'box'";
        }
        else if (SearchVocabulary.SetTypes.TryGetValue(normalized, out var type))
        {
            condition = $"x.set_type = {Param(type)}";
        }
        else if (value == "booster")
        {
            condition = "x.booster";
        }
        else if (SearchVocabulary.Languages.TryGetValue(value, out var language) && value.Length <= 3)
        {
            condition = $"x.lang = {Param(language)}";
        }
        else
        {
            condition = $"x.set_code = {Param(value)}";
        }

        return Card($"exists (select 1 from reference.printings x where x.oracle_id = o.id and {condition})");
    }

    private Fragment? FormatTerm(TermNode term, params string[] statuses)
    {
        if (!SearchVocabulary.Formats.TryGetValue(Lower(term.Value), out var format))
        {
            return Warn(term, SearchWarningCodes.InvalidValue, $"The value \"{term.Value}\" is not a format. The search ignores the term \"{TermText(term)}\".");
        }

        var conditions = statuses.Select(status => $"o.legalities @> {Param($"{{\"{format}\":\"{status}\"}}")}::jsonb");
        return Card("(" + string.Join(" or ", conditions) + ")");
    }

    private static string PriceExpression(string alias, string currency) => currency switch
    {
        "usd" => $"coalesce(({alias}.prices->>'usd')::numeric, ({alias}.prices->>'usd_foil')::numeric, ({alias}.prices->>'usd_etched')::numeric)",
        "eur" => $"coalesce(({alias}.prices->>'eur')::numeric, ({alias}.prices->>'eur_foil')::numeric)",
        _ => $"({alias}.prices->>'tix')::numeric",
    };

    private Fragment? PriceTerm(TermNode term, string currency) =>
        TryNumber(term.Value, out var price)
            ? Printing($"coalesce({PriceExpression("p", currency)} {Sql(term.Operator)} {Param(price)}, false)")
            : InvalidValue(term);

    private Fragment? CheapestTerm(TermNode term)
    {
        var currency = Lower(term.Value);
        if (currency is not ("usd" or "eur" or "tix"))
        {
            return InvalidValue(term);
        }

        var price = PriceExpression("x", currency);
        return Printing($"p.id = (select x.id from reference.printings x where x.oracle_id = p.oracle_id and x.default_lang and {price} is not null order by {price}, x.id limit 1)");
    }

    private Fragment? CountTerm(TermNode term, string expression, bool printing) =>
        int.TryParse(term.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? new Fragment($"{expression} {Sql(term.Operator)} {Param(count)}", printing)
            : InvalidValue(term);

    private Fragment? PrintCountTerm(TermNode term, string aggregate, bool paper)
    {
        if (!int.TryParse(term.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return InvalidValue(term);
        }

        var filter = paper ? " and 'paper' = any (x.games)" : string.Empty;
        var condition = $"o.id in (select x.oracle_id from reference.printings x where x.default_lang{filter} group by x.oracle_id having {aggregate} {Sql(term.Operator)} {Param(count)})";

        // A card without paper printings has 0 paper printings, and the group gives no row for it.
        if (paper && SatisfiesZero(term.Operator, count))
        {
            condition = $"({condition} or not exists (select 1 from reference.printings x where x.oracle_id = o.id and x.default_lang{filter}))";
        }

        return Card(condition);
    }

    private static bool SatisfiesZero(SearchOperator op, int count) => op switch
    {
        SearchOperator.Colon or SearchOperator.Equal => count == 0,
        SearchOperator.NotEqual => count != 0,
        SearchOperator.Less => count > 0,
        SearchOperator.LessOrEqual => count >= 0,
        _ => false,
    };

    private Fragment? NewTerm(TermNode term, string value)
    {
        var earlier = "x.oracle_id = p.oracle_id and x.released_at < p.released_at";
        string? condition = value switch
        {
            "art" => $"p.illustration_id is not null and not exists (select 1 from reference.printings x where {earlier} and x.illustration_id = p.illustration_id)",
            "artist" => $"p.artist is not null and not exists (select 1 from reference.printings x where {earlier} and x.artist = p.artist)",
            "flavor" => $"p.flavor_text is not null and not exists (select 1 from reference.printings x where {earlier} and x.flavor_text = p.flavor_text)",
            "frame" => $"not exists (select 1 from reference.printings x where {earlier} and x.frame is not distinct from p.frame)",
            "rarity" => $"not exists (select 1 from reference.printings x where {earlier} and x.rarity = p.rarity)",
            "language" or "lang" => $"not exists (select 1 from reference.printings x where {earlier} and x.lang = p.lang)",
            _ => null,
        };

        return condition is null ? InvalidValue(term) : Printing($"({condition})");
    }

    private Fragment? WatermarkTerm(string value)
    {
        var watermark = Param(value);
        return Printing($"(lower(p.watermark) = {watermark} or exists (select 1 from reference.printing_faces pf where pf.printing_id = p.id and lower(pf.watermark) = {watermark}))");
    }

    private Fragment? FrameTerm(TermNode term, string value) =>
        SearchVocabulary.FrameEditions.Contains(value)
            ? Printing($"coalesce(p.frame = {Param(value)}, false)")
            : Printing($"{Param(value)} = any (p.frame_effects)");

    private Fragment? YearTerm(TermNode term)
    {
        if (!int.TryParse(term.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 1000 or > 9998)
        {
            return InvalidValue(term);
        }

        var start = Param(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
        var end = Param(new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
        var condition = term.Operator switch
        {
            SearchOperator.Colon or SearchOperator.Equal => $"(p.released_at >= {start}::date and p.released_at < {end}::date)",
            SearchOperator.NotEqual => $"not (p.released_at >= {start}::date and p.released_at < {end}::date)",
            SearchOperator.Less => $"p.released_at < {start}::date",
            SearchOperator.LessOrEqual => $"p.released_at < {end}::date",
            SearchOperator.Greater => $"p.released_at >= {end}::date",
            SearchOperator.GreaterOrEqual => $"p.released_at >= {start}::date",
            _ => throw new ArgumentOutOfRangeException(nameof(term)),
        };
        return Printing(condition);
    }

    private Fragment? DateTerm(TermNode term)
    {
        var value = Lower(term.Value);
        string date;
        if (value is "now" or "today")
        {
            date = "current_date";
        }
        else if (DateTime.TryParseExact(value, ["yyyy-MM-dd", "yyyy-M-d", "yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = Param(parsed) + "::date";
        }
        else if (SetCodePattern().IsMatch(value))
        {
            date = $"(select s.released_at from reference.sets s where s.code = {Param(value)})";
        }
        else
        {
            return Warn(term, SearchWarningCodes.InvalidValue, $"The value \"{term.Value}\" is not a date or a set code. The search ignores the term \"{TermText(term)}\". Use the form yyyy-mm-dd.");
        }

        return Printing($"coalesce(p.released_at {Sql(term.Operator)} {date}, false)");
    }

    private static string TagSlug(string value) => SlugSeparators().Replace(Lower(value).Trim(), "-");

    private Fragment? ArtTagTerm(TermNode term)
    {
        var slug = Param(TagSlug(term.Value));
        var illustrations = $"""
            select it.illustration_id from reference.illustration_taggings it
            join reference.tag_closure tc on tc.descendant_id = it.tag_id
            join reference.tags t on t.id = tc.ancestor_id
            where t.kind = 'illustration' and (t.slug = {slug} or {slug} = any (t.aliases))
            """;
        // "= any (array(...))" computes the list once for the query, also inside the subquery that chooses the printing.
        return Printing($"""
            (p.illustration_id = any (array({illustrations}))
             or p.id = any (array(select pf.printing_id from reference.printing_faces pf where pf.illustration_id = any (array({illustrations})))))
            """);
    }

    private Fragment? OracleTagTerm(TermNode term)
    {
        var slug = Param(TagSlug(term.Value));
        return Card($"""
            o.id = any (array(select ot.oracle_id from reference.oracle_taggings ot
                     join reference.tag_closure tc on tc.descendant_id = ot.tag_id
                     join reference.tags t on t.id = tc.ancestor_id
                     where t.kind = 'oracle' and (t.slug = {slug} or {slug} = any (t.aliases))))
            """);
    }

    private Fragment? LanguageTerm(TermNode term)
    {
        var value = Lower(term.Value);
        if (value == "any")
        {
            languageGiven = true;
            return Printing("true");
        }

        if (!SearchVocabulary.Languages.TryGetValue(value, out var language))
        {
            return Warn(term, SearchWarningCodes.InvalidValue, $"The value \"{term.Value}\" is not a language. The search ignores the term \"{TermText(term)}\". Use a code such as \"fr\" or a name such as \"french\".");
        }

        languageGiven = true;
        return Printing($"p.lang {(term.Operator == SearchOperator.NotEqual ? "<>" : "=")} {Param(language)}");
    }

    private Fragment? RankTerm(TermNode term, string column) =>
        int.TryParse(term.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var rank)
            ? Card($"coalesce({column} {Sql(term.Operator)} {Param(rank)}, false)")
            : InvalidValue(term);

    private Fragment? DisplayTerm(TermNode term, string keyword, string value)
    {
        switch (keyword)
        {
            case "unique":
                SearchUnique? mode = value switch
                {
                    "cards" or "card" => SearchUnique.Cards,
                    "prints" or "print" or "printings" => SearchUnique.Prints,
                    "art" or "artwork" => SearchUnique.Art,
                    _ => null,
                };
                if (mode is null)
                {
                    return InvalidValue(term);
                }

                unique = mode.Value;
                return null;
            case "order":
                if (!TryParseOrder(value, out var parsedOrder))
                {
                    return InvalidValue(term);
                }

                order = parsedOrder;
                return null;
            case "direction":
                if (!TryParseDirection(value, out var parsedDirection))
                {
                    return InvalidValue(term);
                }

                direction = parsedDirection;
                return null;
            case "prefer":
                SearchPreference? preference = value switch
                {
                    "default" or "typical" => SearchPreference.Default,
                    "oldest" => SearchPreference.Oldest,
                    "newest" => SearchPreference.Newest,
                    "usd-low" or "usdlow" => SearchPreference.UsdLow,
                    "usd-high" or "usdhigh" => SearchPreference.UsdHigh,
                    "eur-low" or "eurlow" => SearchPreference.EurLow,
                    "eur-high" or "eurhigh" => SearchPreference.EurHigh,
                    "tix-low" or "tixlow" => SearchPreference.TixLow,
                    "tix-high" or "tixhigh" => SearchPreference.TixHigh,
                    "promo" => SearchPreference.Promo,
                    "atypical" => SearchPreference.Atypical,
                    "universesbeyond" or "ub" => SearchPreference.UniversesBeyond,
                    "notuniversesbeyond" or "notub" => SearchPreference.NotUniversesBeyond,
                    _ => null,
                };
                if (preference is null)
                {
                    return InvalidValue(term);
                }

                prefer = preference.Value;
                return null;
            default:
                // The display keyword changes only the presentation of the Scryfall site. The API ignores it.
                return null;
        }
    }

    /// <summary>
    /// Reads the value of an <c>order</c> parameter or keyword.
    /// </summary>
    /// <param name="value">The value, for example <c>cmc</c>.</param>
    /// <param name="order">The order.</param>
    /// <returns><see langword="true"/> when the value is valid.</returns>
    public static bool TryParseOrder(string value, out SearchOrder order)
    {
        SearchOrder? parsed = value.ToLowerInvariant() switch
        {
            "name" or "review" => SearchOrder.Name,
            "cmc" or "mv" or "manavalue" => SearchOrder.ManaValue,
            "power" or "pow" => SearchOrder.Power,
            "toughness" or "tou" => SearchOrder.Toughness,
            "color" or "colors" => SearchOrder.Color,
            "edhrec" => SearchOrder.Edhrec,
            "penny" => SearchOrder.Penny,
            "set" => SearchOrder.Set,
            "usd" => SearchOrder.Usd,
            "eur" => SearchOrder.Eur,
            "tix" => SearchOrder.Tix,
            "rarity" => SearchOrder.Rarity,
            "released" or "spoiled" => SearchOrder.Released,
            "artist" => SearchOrder.Artist,
            _ => null,
        };
        order = parsed ?? SearchOrder.Name;
        return parsed is not null;
    }

    /// <summary>
    /// Reads the value of a <c>dir</c> parameter or keyword.
    /// </summary>
    /// <param name="value">The value: <c>asc</c>, <c>desc</c> or <c>auto</c>.</param>
    /// <param name="direction">The direction.</param>
    /// <returns><see langword="true"/> when the value is valid.</returns>
    public static bool TryParseDirection(string value, out SearchDirection direction)
    {
        SearchDirection? parsed = value.ToLowerInvariant() switch
        {
            "asc" or "ascending" => SearchDirection.Ascending,
            "desc" or "descending" => SearchDirection.Descending,
            "auto" => SearchDirection.Auto,
            _ => null,
        };
        direction = parsed ?? SearchDirection.Auto;
        return parsed is not null;
    }

    [GeneratedRegex("^[a-z0-9]{2,8}$")]
    private static partial Regex SetCodePattern();

    [GeneratedRegex(@"^[\p{L}\p{N}'-]+$")]
    private static partial Regex TypeWordPattern();

    [GeneratedRegex(@"[\s_]+")]
    private static partial Regex SlugSeparators();

    /// <summary>
    /// An SQL condition, and whether it uses the printing.
    /// </summary>
    /// <param name="Sql">The condition.</param>
    /// <param name="Printing"><see langword="true"/> when the condition uses the printing (alias <c>p</c>).</param>
    private readonly record struct Fragment(string Sql, bool Printing);
}
