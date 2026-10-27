namespace Reliquary.Decks.Analysis;

/// <summary>
/// Compares the categories of a Commander deck with targets from known deck templates.
/// </summary>
/// <remarks>
/// The fixed targets come from the template of The Command Zone, episode 658 (February 2025).
/// The land target uses the formula of Frank Karsten for 99-card decks
/// ("How Many Lands Do You Need in Your Deck? An Updated Analysis", TCGplayer, 29 July 2022).
/// See docs/architecture/deck-analysis.md and docs/decisions/0027-deck-analysis.md.
/// </remarks>
public static class CommanderNeeds
{
    /// <summary>The category has fewer cards than the minimum.</summary>
    public const string Low = "low";

    /// <summary>The category has a number of cards from the minimum to the maximum.</summary>
    public const string Ok = "ok";

    /// <summary>The category has more cards than the maximum.</summary>
    public const string High = "high";

    /// <summary>The category has no target.</summary>
    public const string NoTarget = "no_target";

    /// <summary>The highest mana value of a cheap ramp or card draw card in the land formula.</summary>
    public const decimal CheapManaValue = 2;

    // The fixed targets: target, lowest count with the status ok, highest count with the status ok.
    private static readonly Dictionary<string, (int Target, int Min, int Max)> FixedTargets = new(StringComparer.Ordinal)
    {
        ["card_draw"] = (12, 10, 18),
        ["targeted_interaction"] = (12, 9, 18),
        ["board_wipe"] = (6, 3, 9),
    };

    /// <summary>
    /// Gets the land target of a 99-card deck: 31.42 + 3.13 × average mana value − 0.28 × cheap ramp and card draw cards.
    /// </summary>
    /// <param name="averageManaValue">The average mana value of the cards that are not lands.</param>
    /// <param name="cheapRampAndDraw">The number of ramp and card draw cards with a mana value of 2 or less.</param>
    /// <returns>The target, rounded to the nearest integer.</returns>
    public static int LandTarget(decimal averageManaValue, int cheapRampAndDraw) =>
        (int)Math.Round(31.42m + (3.13m * averageManaValue) - (0.28m * cheapRampAndDraw), MidpointRounding.AwayFromZero);

    /// <summary>
    /// Gets the ramp target: 10 + 2 × (average mana value − 3), from 8 to 14.
    /// </summary>
    /// <param name="averageManaValue">The average mana value of the cards that are not lands.</param>
    /// <returns>The target, rounded to the nearest integer.</returns>
    public static int RampTarget(decimal averageManaValue) =>
        Math.Clamp((int)Math.Round(10m + (2m * (averageManaValue - 3m)), MidpointRounding.AwayFromZero), 8, 14);

    /// <summary>
    /// Compares the number of cards of each category with its target.
    /// </summary>
    /// <param name="counts">The number of cards in each category.</param>
    /// <param name="averageManaValue">The average mana value of the cards that are not lands.</param>
    /// <param name="cheapRampAndDraw">The number of ramp and card draw cards with a mana value of 2 or less.</param>
    /// <returns>One need for each category, in the order of <see cref="DeckRules.Categories"/>.</returns>
    public static IReadOnlyList<CategoryNeed> Calculate(IReadOnlyDictionary<string, int> counts, decimal averageManaValue, int cheapRampAndDraw)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var lands = LandTarget(averageManaValue, cheapRampAndDraw);
        var ramp = RampTarget(averageManaValue);
        var needs = new List<CategoryNeed>();
        foreach (var category in DeckRules.Categories)
        {
            var count = counts.GetValueOrDefault(category);
            (int Target, int Min, int Max)? target = category switch
            {
                "land" => (lands, lands - 2, lands + 2),
                "ramp" => (ramp, ramp - 2, ramp + 5),
                _ => FixedTargets.TryGetValue(category, out var value) ? value : null,
            };

            if (target is not { } t)
            {
                needs.Add(new CategoryNeed(category, count, null, null, null, NoTarget, null));
                continue;
            }

            var status = count < t.Min ? Low : count > t.Max ? High : Ok;
            needs.Add(new CategoryNeed(category, count, t.Target, t.Min, t.Max, status, count - t.Target));
        }

        return needs;
    }
}
