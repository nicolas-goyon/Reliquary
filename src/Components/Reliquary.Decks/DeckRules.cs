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
}
