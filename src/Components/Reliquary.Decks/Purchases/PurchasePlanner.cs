using Reliquary.BuildingBlocks.Collection;

namespace Reliquary.Decks.Purchases;

/// <summary>
/// One line of a list that needs cards.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Zone">The zone of the line.</param>
/// <param name="Quantity">The number of copies that the list needs.</param>
/// <param name="Preferences">The purchase preferences of the line.</param>
internal sealed record PurchaseNeed(Guid OracleId, string Zone, int Quantity, PurchasePreferences Preferences);

/// <summary>
/// Copies that a line takes from one location entry.
/// </summary>
/// <param name="Copy">The copies of the entry. <see cref="CollectionCopy.Quantity"/> is the quantity of the entry.</param>
/// <param name="Quantity">The number of copies that the line takes.</param>
internal sealed record TakenCopies(CollectionCopy Copy, int Quantity);

/// <summary>
/// Copies that do not fit a line, with the reasons.
/// </summary>
/// <param name="Copy">The copies.</param>
/// <param name="Quantity">The number of copies that are still free.</param>
/// <param name="Reasons">The reasons: <c>printing</c>, <c>language</c>, <c>finish</c>, <c>condition</c> or <c>unavailable</c>.</param>
internal sealed record OtherCopies(CollectionCopy Copy, int Quantity, IReadOnlyList<string> Reasons);

/// <summary>
/// The result for one line.
/// </summary>
/// <param name="Need">The line.</param>
/// <param name="FromDeck">The copies that are already in the location of the deck.</param>
/// <param name="FromCollection">The available copies that complete the line.</param>
/// <param name="ToBuy">The number of copies to buy.</param>
/// <param name="Closest">When the line needs to buy cards: the copies of the collection that do not fit the preferences or are not available.</param>
internal sealed record PurchasePlanLine(PurchaseNeed Need, IReadOnlyList<TakenCopies> FromDeck, IReadOnlyList<TakenCopies> FromCollection, int ToBuy, IReadOnlyList<OtherCopies> Closest);

/// <summary>
/// Calculates which cards of a list come from the collection and which cards to buy. See docs/architecture/purchase-list.md.
/// </summary>
/// <remarks>
/// The rules of the specification:
/// <list type="bullet">
/// <item>The available cards are the cards of the pickable locations. The cards of another assembled deck are never available.</item>
/// <item>The cards in the location of the deck itself are already in the deck.</item>
/// <item>A copy fits a line when it agrees with the purchase preferences of the line. An unknown field (no printing, no finish or no condition) agrees, except with a wanted printing.</item>
/// </list>
/// </remarks>
internal static class PurchasePlanner
{
    /// <summary>The reason of a copy in another assembled deck or in a location that is not pickable.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>
    /// Plans the lines of a list.
    /// </summary>
    /// <param name="needs">The lines.</param>
    /// <param name="copies">All the copies of the cards of the lines in the collection of the owner.</param>
    /// <param name="ownDeckIds">The decks whose location counts as the deck itself: the deck and, for a variant, its main deck.</param>
    /// <returns>One result for each line, in the order of the lines.</returns>
    public static IReadOnlyList<PurchasePlanLine> Plan(IReadOnlyList<PurchaseNeed> needs, IReadOnlyList<CollectionCopy> copies, IReadOnlySet<Guid> ownDeckIds)
    {
        var free = new Dictionary<CollectionCopy, int>(ReferenceComparer.Instance);
        foreach (var copy in copies)
        {
            free[copy] = copy.Quantity;
        }

        var results = new PurchasePlanLine?[needs.Count];

        // The lines with the most limits take their copies first, so that a line with no limit takes what stays.
        foreach (var index in Enumerable.Range(0, needs.Count).OrderByDescending(index => Limits(needs[index].Preferences)).ThenBy(index => index))
        {
            var need = needs[index];
            var cardCopies = copies.Where(copy => copy.OracleId == need.OracleId).ToList();
            var remaining = need.Quantity;
            var fromDeck = Take(cardCopies.Where(copy => IsOwn(copy, ownDeckIds) && Fits(copy, need.Preferences).Count == 0), free, ref remaining);
            var fromCollection = Take(
                cardCopies.Where(copy => !IsOwn(copy, ownDeckIds) && copy.Available && Fits(copy, need.Preferences).Count == 0).OrderBy(copy => copy.Locked),
                free,
                ref remaining);
            var closest = remaining == 0
                ? []
                : cardCopies
                    .Where(copy => free[copy] > 0)
                    .Select(copy => new OtherCopies(copy, free[copy], IsOwn(copy, ownDeckIds) || copy.Available ? Fits(copy, need.Preferences) : [Unavailable, .. Fits(copy, need.Preferences)]))
                    .Where(other => other.Reasons.Count > 0)
                    .OrderBy(other => other.Reasons.Count)
                    .ThenBy(other => other.Reasons.Contains(Unavailable))
                    .ToList();
            results[index] = new PurchasePlanLine(need, fromDeck, fromCollection, remaining, closest);
        }

        return [.. results.Select(result => result!)];
    }

    /// <summary>
    /// Gives the reasons why a copy does not fit the preferences. An empty list means that the copy fits.
    /// </summary>
    public static IReadOnlyList<string> Fits(CollectionCopy copy, PurchasePreferences preferences)
    {
        var reasons = new List<string>();
        var wanted = preferences.WantedPrintingIds ?? [];
        if ((wanted.Count > 0 && (copy.PrintingId is not { } printing || !wanted.Contains(printing)))
            || (copy.PrintingId is { } avoided && (preferences.AvoidedPrintingIds ?? []).Contains(avoided)))
        {
            reasons.Add("printing");
        }

        var languages = preferences.Languages ?? [];
        if (languages.Count > 0 && !languages.Contains(copy.Language, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("language");
        }

        var foil = copy.Finish is "foil" or "etched";
        if ((preferences.Foil == "foil" && !foil) || (preferences.Foil == "nonfoil" && foil))
        {
            reasons.Add("finish");
        }

        if (preferences.MinimumCondition is { } minimum && copy.Condition is { } condition && CardConditions.Rank(condition) > CardConditions.Rank(minimum))
        {
            reasons.Add("condition");
        }

        return reasons;
    }

    private static bool IsOwn(CollectionCopy copy, IReadOnlySet<Guid> ownDeckIds) =>
        copy.DeckId is { } deckId && ownDeckIds.Contains(deckId);

    private static List<TakenCopies> Take(IEnumerable<CollectionCopy> candidates, Dictionary<CollectionCopy, int> free, ref int remaining)
    {
        var taken = new List<TakenCopies>();
        foreach (var copy in candidates)
        {
            if (remaining == 0)
            {
                break;
            }

            var quantity = Math.Min(remaining, free[copy]);
            if (quantity == 0)
            {
                continue;
            }

            free[copy] -= quantity;
            remaining -= quantity;
            taken.Add(new TakenCopies(copy, quantity));
        }

        return taken;
    }

    private static int Limits(PurchasePreferences preferences) =>
        (preferences.WantedPrintingIds?.Count > 0 ? 1 : 0)
        + (preferences.AvoidedPrintingIds?.Count > 0 ? 1 : 0)
        + (preferences.Languages?.Count > 0 ? 1 : 0)
        + (preferences.MinimumCondition is not null ? 1 : 0)
        + (preferences.Foil is "foil" or "nonfoil" ? 1 : 0);

    private sealed class ReferenceComparer : IEqualityComparer<CollectionCopy>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(CollectionCopy? x, CollectionCopy? y) => ReferenceEquals(x, y);

        public int GetHashCode(CollectionCopy obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
