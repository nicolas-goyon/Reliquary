using System.Text.RegularExpressions;

namespace Reliquary.Decks.AutoTag;

/// <summary>
/// The data of auto-tag: the map from the Oracle tags of Scryfall Tagger to the categories, and the rules on the card text.
/// See ADR 0031 and docs/architecture/auto-tag.md.
/// </summary>
internal static partial class AutoTagRules
{
    /// <summary>
    /// The Oracle tags that give a category. A card gets the category when it has the tag, or a tag below it in the tag tree.
    /// For example, <c>mana-rock</c> is below <c>mana-producer</c>, and <c>mana-producer</c> is below <c>ramp</c>.
    /// The slugs come from the Tagger data of 2026-10-26 in <c>data/demo</c>.
    /// </summary>
    public static readonly IReadOnlyList<OracleTagRule> OracleTags =
    [
        new("ramp", "ramp"),
        new("card-advantage", "card_draw"),
        new("spot-removal", "targeted_interaction"),
        new("counterspell", "targeted_interaction"),

        // "removal" is also above the sweepers. A board wipe gets no targeted interaction from this tag.
        new("removal", "targeted_interaction", IsGeneral: true),
        new("sweeper", "board_wipe"),
        new("protection", "protection"),
        new("overrun", "win_condition"),
        new("group-slug", "win_condition"),
        new("sacrifice-outlet-creature", "synergy"),
        new("blood-artist-ability", "synergy"),
        new("counter-increaser", "synergy"),
        new("token-increaser", "synergy"),
    ];

    /// <summary>
    /// The rules on the card text, for the cards that get no category from their Oracle tags.
    /// Each rule reads the text of all the faces, with no reminder text. The ramp rules do not read the land faces.
    /// </summary>
    public static readonly IReadOnlyList<TextRule> TextRules =
    [
        new("mana_ability", "ramp", ManaAbility(), SkipLandFaces: true),
        new("land_to_battlefield", "ramp", LandToBattlefield(), SkipLandFaces: true),
        new("extra_land", "ramp", ExtraLand(), SkipLandFaces: true),
        new("treasure", "ramp", Treasure(), SkipLandFaces: true),
        new("draw_cards", "card_draw", DrawCards()),
        new("impulse_draw", "card_draw", ImpulseDraw()),
        new("destroy_or_exile_target", "targeted_interaction", DestroyOrExileTarget()),
        new("counter_target", "targeted_interaction", CounterTarget()),
        new("bounce_target", "targeted_interaction", BounceTarget()),
        new("damage_target", "targeted_interaction", DamageTarget()),
        new("shrink_target", "targeted_interaction", ShrinkTarget()),
        new("tuck_target", "targeted_interaction", TuckTarget()),
        new("edict", "targeted_interaction", Edict()),
        new("destroy_all", "board_wipe", DestroyAll()),
        new("shrink_all", "board_wipe", ShrinkAll()),
        new("damage_all", "board_wipe", DamageAll()),
        new("bounce_all", "board_wipe", BounceAll()),
        new("keyword_to_yours", "protection", KeywordToYours()),
        new("phase_out", "protection", PhaseOut()),
        new("silence", "protection", Silence()),
        new("redirect", "protection", Redirect()),
        new("win_game", "win_condition", WinGame()),
        new("drain_x", "win_condition", DrainX()),
        new("overrun", "win_condition", Overrun()),
    ];

    /// <summary>The text rule that gives the category <c>land</c> to a card with a land as its first face.</summary>
    public const string LandRule = "land_type";

    /// <summary>
    /// Removes the reminder text, which is in parentheses, and uses the hyphen for the minus sign.
    /// </summary>
    public static string Clean(string? text) =>
        text is null ? string.Empty : ReminderText().Replace(text, string.Empty).Replace('−', '-');

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ReminderText();

    // "{T}: Add {G}.", "Add one mana of any color.", "Add an amount of {C} equal to ..."
    [GeneratedRegex(@"\badd (\{[wubrgcs]\}|(one|two|three|four|five|x) mana\b|an amount of \{)", RegexOptions.IgnoreCase)]
    private static partial Regex ManaAbility();

    // "Search your library for up to two basic land cards ... put one onto the battlefield tapped", also for a Forest card.
    [GeneratedRegex(@"\bsearch your library for [^.]*?\b(lands?|forests?|plains|islands?|swamps?|mountains?)\b[^.]*?\bonto the battlefield\b|\bput (a|up to \w+) land cards? from your hand onto the battlefield\b", RegexOptions.IgnoreCase)]
    private static partial Regex LandToBattlefield();

    [GeneratedRegex(@"\bplay (an|two|three) additional lands?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExtraLand();

    [GeneratedRegex(@"\bcreates? (a|an|one|two|three|four|x|that many) (tapped )?treasure tokens?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Treasure();

    // "Draw two cards.", "draws cards equal to", "draw two additional cards". The draws of an opponent do not count.
    [GeneratedRegex(@"(?<!opponent )\bdraws? ((a|one|two|three|four|five|six|seven|x|that many) )?(additional )?cards?\b", RegexOptions.IgnoreCase)]
    private static partial Regex DrawCards();

    // "Exile the top three cards of your library. You may play them this turn."
    [GeneratedRegex(@"\bexile the top (card|\w+ cards) of your library\b[^.]*\.\s*(until|you may (play|cast))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImpulseDraw();

    // "Destroy target creature." A card in a graveyard or a player is not a permanent, so "exile target card" does not count.
    [GeneratedRegex(@"\b(destroy|exile) (up to (one|two|three|x) )?(another )?target\b(?! (cards?|players?|opponents?)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex DestroyOrExileTarget();

    // "Counter target spell." "Counter target enchantment, instant, or sorcery spell."
    [GeneratedRegex(@"\bcounter (up to (one|two) )?target ([\w,-]+ )*?(spell|ability)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CounterTarget();

    // "Return target nonland permanent to its owner's hand." A permanent of the player does not count.
    [GeneratedRegex(@"\breturn (up to \w+ )?target ((?!you control)[^.])*?\bto (its|their) owner's hand\b", RegexOptions.IgnoreCase)]
    private static partial Regex BounceTarget();

    [GeneratedRegex(@"\bdeals? (\d+|x|that much) damage to (any (other )?target|(up to one )?target (creature|planeswalker|attacking|blocking))\b", RegexOptions.IgnoreCase)]
    private static partial Regex DamageTarget();

    [GeneratedRegex(@"\btarget creature (an opponent controls )?gets -(\d+|x)/-(\d+|x)", RegexOptions.IgnoreCase)]
    private static partial Regex ShrinkTarget();

    // "The owner of target permanent shuffles it into their library." "Put target creature on the bottom of its owner's library."
    [GeneratedRegex(@"\b(owner|controller) of target (permanent|creature|artifact|enchantment|nonland permanent)\b|\bput target [^.]*\bon (the )?(top|bottom) of (its|their) owner's library\b", RegexOptions.IgnoreCase)]
    private static partial Regex TuckTarget();

    [GeneratedRegex(@"\b(target (player|opponent)|each opponent) sacrifices (a|an|one) (creature|permanent|nonland permanent|artifact|enchantment|planeswalker)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Edict();

    // "Destroy all creatures." "Exile all artifacts." "Exile all cards from target player's graveyard" does not count.
    [GeneratedRegex(@"\b(destroy|exile) all (?!cards\b)(other )?([\w-]+ )*?(creatures|permanents|artifacts|enchantments|planeswalkers)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DestroyAll();

    [GeneratedRegex(@"\b(all|each) (other )?creatures? ([\w-]+ )*?gets? -(\d+|x)/-(\d+|x)", RegexOptions.IgnoreCase)]
    private static partial Regex ShrinkAll();

    [GeneratedRegex(@"\bdeals? (\d+|x|that much) damage to each (other )?creature\b", RegexOptions.IgnoreCase)]
    private static partial Regex DamageAll();

    [GeneratedRegex(@"\breturn all ([\w-]+ )*?(permanents|creatures)\b[^.]*\bto their owners' hands\b", RegexOptions.IgnoreCase)]
    private static partial Regex BounceAll();

    // "Permanents you control gain hexproof and indestructible." "Equipped creature has shroud."
    [GeneratedRegex(@"\b(you control|equipped creature|enchanted creature)\b[^.]*\b(gains?|has|have)\b[^.]*\b(hexproof|indestructible|shroud|protection from)\b", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordToYours();

    [GeneratedRegex(@"\b(permanents|creatures|artifacts) you control phase out\b", RegexOptions.IgnoreCase)]
    private static partial Regex PhaseOut();

    [GeneratedRegex(@"\byour opponents can't cast spells\b", RegexOptions.IgnoreCase)]
    private static partial Regex Silence();

    [GeneratedRegex(@"\bchoose new targets for target (spell|ability)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Redirect();

    [GeneratedRegex(@"\byou win the game\b|\b(each opponent|target (player|opponent)|that player) loses the game\b", RegexOptions.IgnoreCase)]
    private static partial Regex WinGame();

    [GeneratedRegex(@"\beach opponent loses x life\b", RegexOptions.IgnoreCase)]
    private static partial Regex DrainX();

    // "Creatures you control gain trample and get +X/+X." Both orders count.
    [GeneratedRegex(@"\bcreatures you control (get \+(\d+|x)/\+(\d+|x)[^.]*\btrample\b|gain trample[^.]*\bget \+(\d+|x)/\+(\d+|x))", RegexOptions.IgnoreCase)]
    private static partial Regex Overrun();
}

/// <summary>
/// An Oracle tag that gives a category.
/// </summary>
/// <param name="Slug">The slug of the tag.</param>
/// <param name="Category">The category.</param>
/// <param name="IsGeneral">Whether the tag is also above the tags of another category, so that the other category wins.</param>
internal sealed record OracleTagRule(string Slug, string Category, bool IsGeneral = false);

/// <summary>
/// A rule on the card text.
/// </summary>
/// <param name="Code">The stable code of the rule, in the reason of a suggestion.</param>
/// <param name="Category">The category that the rule gives.</param>
/// <param name="Pattern">The pattern that the text must contain.</param>
/// <param name="SkipLandFaces">Whether the rule ignores the faces that are lands.</param>
internal sealed record TextRule(string Code, string Category, Regex Pattern, bool SkipLandFaces = false);
