namespace Reliquary.Decks;

/// <summary>
/// The fixed values of the deck model.
/// </summary>
public static class DeckRules
{
    /// <summary>The zones of a deck, in the order of the deck page.</summary>
    public static readonly IReadOnlyList<string> Zones = ["commander", "companion", "main", "sideboard", "considering", "tokens"];

    /// <summary>The visibilities of a deck.</summary>
    public static readonly IReadOnlyList<string> Visibilities = ["private", "unlisted", "public"];

    /// <summary>The finishes of a printing.</summary>
    public static readonly IReadOnlyList<string> Finishes = ["nonfoil", "foil", "etched"];

    /// <summary>
    /// The formats, with the names of the legalities of Scryfall. <c>freeform</c> has no rules.
    /// </summary>
    public static readonly IReadOnlyList<string> Formats =
    [
        "commander", "oathbreaker", "brawl", "standardbrawl", "paupercommander", "predh", "duel",
        "standard", "pioneer", "modern", "legacy", "vintage", "pauper", "premodern", "oldschool",
        "historic", "timeless", "alchemy", "gladiator", "explorer", "future", "penny", "freeform",
    ];

    /// <summary>The predefined categories of the analysis.</summary>
    public static readonly IReadOnlyList<string> Categories =
        ["ramp", "card_draw", "targeted_interaction", "board_wipe", "synergy", "land", "win_condition", "protection"];

    /// <summary>The maximum length of a tag.</summary>
    public const int MaxTagLength = 50;

    /// <summary>The maximum number of tags and categories of a deck line.</summary>
    public const int MaxTagsPerLine = 20;

    /// <summary>The maximum length of the name of a deck.</summary>
    public const int MaxNameLength = 100;

    /// <summary>The maximum length of the description of a deck.</summary>
    public const int MaxDescriptionLength = 10_000;

    /// <summary>The maximum length of the note of a deck line.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>The maximum number of lines in a deck.</summary>
    public const int MaxLines = 1_000;

    /// <summary>The maximum quantity of a deck line.</summary>
    public const int MaxQuantity = 999;

    /// <summary>The maximum number of deck tags on a deck.</summary>
    public const int MaxDeckTags = 20;

    /// <summary>The maximum length of a deck tag or of a folder name.</summary>
    public const int MaxLabelLength = 50;

    /// <summary>The maximum depth of the folders. A folder at the top has the depth 1.</summary>
    public const int MaxFolderDepth = 8;

    /// <summary>The letters of the colors in the order of a color identity.</summary>
    public const string ColorOrder = "WUBRG";

    /// <summary>
    /// Removes the spaces at the ends of the deck tags, the empty tags and the tags that differ only by case.
    /// </summary>
    /// <param name="tags">The deck tags.</param>
    /// <returns>The deck tags, in their first order.</returns>
    public static string[] NormalizeDeckTags(IEnumerable<string> tags) =>
        [.. tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
}
