namespace Reliquary.Cards.Search;

/// <summary>
/// How the search removes the duplicate results.
/// </summary>
public enum SearchUnique
{
    /// <summary>One result for each Oracle card, with one chosen printing.</summary>
    Cards,

    /// <summary>One result for each printing.</summary>
    Prints,

    /// <summary>One result for each illustration.</summary>
    Art,
}

/// <summary>
/// The sort order of the results.
/// </summary>
public enum SearchOrder
{
    /// <summary>The name of the card.</summary>
    Name,

    /// <summary>The mana value of the card.</summary>
    ManaValue,

    /// <summary>The power of the card.</summary>
    Power,

    /// <summary>The toughness of the card.</summary>
    Toughness,

    /// <summary>The color of the card: white, blue, black, red, green, multicolor, then colorless.</summary>
    Color,

    /// <summary>The rank of the card on EDHREC.</summary>
    Edhrec,

    /// <summary>The rank of the card in Penny Dreadful.</summary>
    Penny,

    /// <summary>The set and the collector number of the printing.</summary>
    Set,

    /// <summary>The price in US dollars of the printing.</summary>
    Usd,

    /// <summary>The price in euros of the printing.</summary>
    Eur,

    /// <summary>The price in MTGO tickets of the printing.</summary>
    Tix,

    /// <summary>The rarity of the printing.</summary>
    Rarity,

    /// <summary>The release date of the printing.</summary>
    Released,

    /// <summary>The artist of the printing.</summary>
    Artist,
}

/// <summary>
/// The direction of the sort.
/// </summary>
public enum SearchDirection
{
    /// <summary>The natural direction of the sort order: ascending, but descending for the dates and the prices.</summary>
    Auto,

    /// <summary>Ascending.</summary>
    Ascending,

    /// <summary>Descending.</summary>
    Descending,
}

/// <summary>
/// The printing that the search shows for each card, when more than one printing matches.
/// </summary>
public enum SearchPreference
{
    /// <summary>The printing that a player recognizes: the same rules as the default printing of the card.</summary>
    Default,

    /// <summary>The oldest printing.</summary>
    Oldest,

    /// <summary>The most recent printing.</summary>
    Newest,

    /// <summary>The cheapest printing in US dollars.</summary>
    UsdLow,

    /// <summary>The most expensive printing in US dollars.</summary>
    UsdHigh,

    /// <summary>The cheapest printing in euros.</summary>
    EurLow,

    /// <summary>The most expensive printing in euros.</summary>
    EurHigh,

    /// <summary>The cheapest printing in MTGO tickets.</summary>
    TixLow,

    /// <summary>The most expensive printing in MTGO tickets.</summary>
    TixHigh,

    /// <summary>A promotional printing.</summary>
    Promo,

    /// <summary>A printing with an atypical frame: borderless, showcase, extended art or full art.</summary>
    Atypical,

    /// <summary>A Universes Beyond printing.</summary>
    UniversesBeyond,

    /// <summary>A printing that is not Universes Beyond.</summary>
    NotUniversesBeyond,
}

/// <summary>
/// A warning about a term that the search ignored.
/// </summary>
/// <param name="Code">The stable code of the warning, for example <c>unknown_keyword</c>.</param>
/// <param name="Message">The English message, in the strict mode of the writing rules.</param>
/// <param name="Position">The index of the first character of the term in the query.</param>
/// <param name="Term">The term, as the query writes it.</param>
public sealed record SearchWarning(string Code, string Message, int Position, string Term);

/// <summary>
/// The stable codes of the search warnings.
/// </summary>
public static class SearchWarningCodes
{
    /// <summary>The keyword of the term is not known.</summary>
    public const string UnknownKeyword = "unknown_keyword";

    /// <summary>Reliquary knows the keyword, but it does not have the data for it.</summary>
    public const string UnsupportedKeyword = "unsupported_keyword";

    /// <summary>The value of the term is not valid for its keyword.</summary>
    public const string InvalidValue = "invalid_value";

    /// <summary>The operator of the term is not valid for its keyword.</summary>
    public const string InvalidOperator = "invalid_operator";
}
