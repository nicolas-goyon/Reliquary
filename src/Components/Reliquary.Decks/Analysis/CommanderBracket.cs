using System.Text.RegularExpressions;
using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Decks.Analysis;

/// <summary>
/// Finds the indicative Commander bracket of a deck from the official rules of the brackets.
/// </summary>
/// <remarks>
/// Sources: "Introducing Commander Brackets Beta" (Wizards of the Coast, 11 February 2025),
/// "Commander Brackets Beta Update" (21 October 2025) and the update of 9 February 2026.
/// The Game Changers list comes from the card data. The combos come from Commander Spellbook (ADR 0030).
/// See docs/architecture/deck-analysis.md.
/// </remarks>
public static partial class CommanderBracket
{
    /// <summary>The deck has cards of the Game Changers list.</summary>
    public const string GameChangers = "game_changers";

    /// <summary>The deck has mass land denial cards.</summary>
    public const string MassLandDenial = "mass_land_denial";

    /// <summary>The deck has extra turn cards.</summary>
    public const string ExtraTurns = "extra_turns";

    /// <summary>
    /// The deck has an early-game two-card combo. As a code of <see cref="BracketAnalysis.Unchecked"/>, the instance has no combo data.
    /// </summary>
    public const string TwoCardCombos = "two_card_combos";

    /// <summary>The deck has a two-card combo that is not an early-game combo.</summary>
    public const string LateTwoCardCombos = "late_two_card_combos";

    /// <summary>
    /// The highest mana value of an early-game combo. Bracket 3 forbids two-card combos that "can happen cheaply and in about the first six or so turns".
    /// A player has about six mana on turn six. Reliquary chose this value.
    /// </summary>
    public const int EarlyGameManaValue = 6;

    /// <summary>The highest number of Game Changers in bracket 3.</summary>
    public const int MaxGameChangers = 3;

    /// <summary>The highest number of extra turn cards that the analysis accepts as a "low quantity" in brackets 2 and 3.</summary>
    public const int MaxExtraTurns = 3;

    /// <summary>
    /// Finds the lowest bracket that a deck fits.
    /// </summary>
    /// <param name="cards">The distinct cards of the commander, companion and main zones.</param>
    /// <param name="combos">The complete combos of the deck, or <see langword="null"/> when the instance has no combo data.</param>
    /// <returns>The bracket and its reasons.</returns>
    public static BracketAnalysis Evaluate(IEnumerable<CardFacts> cards, IReadOnlyList<DeckCombo>? combos = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var list = cards.DistinctBy(card => card.OracleId).OrderBy(card => card.Name, StringComparer.Ordinal).ToList();
        var reasons = new List<BracketReason>();

        var gameChangers = list.Where(card => card.GameChanger).Select(card => card.OracleId).ToList();
        if (gameChangers.Count > 0)
        {
            reasons.Add(new BracketReason(GameChangers, gameChangers.Count > MaxGameChangers ? 4 : 3, gameChangers, []));
        }

        var landDenial = list.Where(IsMassLandDenial).Select(card => card.OracleId).ToList();
        if (landDenial.Count > 0)
        {
            reasons.Add(new BracketReason(MassLandDenial, 4, landDenial, []));
        }

        var extraTurns = list.Where(IsExtraTurn).Select(card => card.OracleId).ToList();
        if (extraTurns.Count > 0)
        {
            reasons.Add(new BracketReason(ExtraTurns, extraTurns.Count > MaxExtraTurns ? 4 : 2, extraTurns, []));
        }

        if (combos is not null)
        {
            // Brackets 1 and 2 forbid the two-card infinite combos. Bracket 3 forbids the early-game ones.
            var twoCard = combos.Where(combo => combo.GameEnding && DeckCombos.IsTwoCard(combo)).ToList();
            AddComboReason(reasons, TwoCardCombos, 4, [.. twoCard.Where(IsEarlyGame)]);
            AddComboReason(reasons, LateTwoCardCombos, 3, [.. twoCard.Where(combo => !IsEarlyGame(combo))]);
        }

        var bracket = reasons.Count == 0 ? 1 : reasons.Max(reason => reason.Bracket);
        return new BracketAnalysis(bracket, reasons, combos is null ? [TwoCardCombos] : []);
    }

    /// <summary>
    /// Tells whether a combo is an early-game combo: its mana value is <see cref="EarlyGameManaValue"/> or less.
    /// </summary>
    /// <param name="combo">The combo.</param>
    /// <returns><see langword="true"/> for an early-game combo.</returns>
    public static bool IsEarlyGame(DeckCombo combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        return combo.ManaValue <= EarlyGameManaValue;
    }

    private static void AddComboReason(List<BracketReason> reasons, string code, int bracket, List<DeckCombo> combos)
    {
        if (combos.Count > 0)
        {
            var cards = combos.SelectMany(combo => combo.Cards).Select(card => card.OracleId).Distinct().ToList();
            reasons.Add(new BracketReason(code, bracket, cards, [.. combos.Select(combo => combo.Id)]));
        }
    }

    /// <summary>
    /// Tells whether a card gives extra turns: its text says that a player takes one or more extra turns.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <returns><see langword="true"/> for an extra turn card.</returns>
    public static bool IsExtraTurn(CardFacts card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return ExtraTurnPattern().IsMatch(TextOf(card));
    }

    /// <summary>
    /// Tells whether a card is a mass land denial card, from the rules of its text.
    /// </summary>
    /// <remarks>
    /// The official definition: cards that destroy, exile or bounce other lands, keep lands tapped,
    /// or change the mana that lands produce, for four or more lands of each player, without a replacement.
    /// The rules find these texts:
    /// <list type="number">
    /// <item>"destroy all", "exile all" or "return all" with "lands" in the same sentence, for example Armageddon and Jokulhaups.</item>
    /// <item>"each player sacrifices" four or more, X, half or all of their lands, for example Wildfire.</item>
    /// <item>lands that do not untap, for example Back to Basics, or "can't untap more than" one or two lands or permanents, for example Winter Orb.</item>
    /// <item>nonbasic lands that become basic land types, for example Blood Moon.</item>
    /// </list>
    /// </remarks>
    /// <param name="card">The card.</param>
    /// <returns><see langword="true"/> for a mass land denial card.</returns>
    public static bool IsMassLandDenial(CardFacts card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var text = TextOf(card);
        return AllLandsPattern().IsMatch(text)
            || SacrificeLandsPattern().IsMatch(text)
            || LandsDoNotUntapPattern().IsMatch(text)
            || BasicTypePattern().IsMatch(text);
    }

    private static string TextOf(CardFacts card) => string.Join('\n', card.Faces.Select(face => face.OracleText));

    [GeneratedRegex(@"\btakes? (an|one|two|three|four|x) extra turns?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExtraTurnPattern();

    // "Destroy all artifacts, creatures, and lands." The sentence stops at a period or a line break.
    // A sentence that names "lands you control" only affects its controller, so it does not count.
    [GeneratedRegex(@"\b(destroy|exile|return) all\b[^.\n]*\blands\b(?! you control)", RegexOptions.IgnoreCase)]
    private static partial Regex AllLandsPattern();

    [GeneratedRegex(@"\beach player sacrifices (four|five|six|seven|eight|nine|ten|x|half|all)\b[^.\n]*\blands\b", RegexOptions.IgnoreCase)]
    private static partial Regex SacrificeLandsPattern();

    [GeneratedRegex(@"(\blands don't untap\b|\bcan't untap more than (one|two) (lands?|permanents?)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex LandsDoNotUntapPattern();

    [GeneratedRegex(@"\bnonbasic lands are (plains|islands|swamps|mountains|forests)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BasicTypePattern();
}
