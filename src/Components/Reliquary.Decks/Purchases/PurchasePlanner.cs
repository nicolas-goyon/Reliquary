using Reliquary.BuildingBlocks.Cards;
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
/// <param name="LowestPrice">When the line needs to buy cards and has a maximum price: the lowest market price of a printing that fits the line, in the currency of the maximum price. <see langword="null"/> when no price is known.</param>
internal sealed record PurchasePlanLine(PurchaseNeed Need, IReadOnlyList<TakenCopies> FromDeck, IReadOnlyList<TakenCopies> FromCollection, int ToBuy, IReadOnlyList<OtherCopies> Closest, PriceLimit? LowestPrice = null)
{
    /// <summary>Gets a value that tells whether the lowest market price is more than the maximum price of the line.</summary>
    public bool AboveMaximumPrice => LowestPrice is { } lowest && Need.Preferences.MaximumPrice is { } maximum && lowest.Amount > maximum.Amount;
}

/// <summary>
/// Calculates which cards of a list come from the collection and which cards to buy. See docs/architecture/purchase-list.md.
/// </summary>
/// <remarks>
/// The rules of the specification:
/// <list type="bullet">
/// <item>The available cards are the cards of the pickable locations. The cards of another assembled deck are never available.</item>
/// <item>The cards in the location of the deck itself are already in the deck.</item>
/// <item>A copy fits a line when it agrees with the purchase preferences of the line. An unknown field (no printing, no finish or no condition) agrees, except with a wanted printing.</item>
/// <item>The collection has no prices. The maximum price applies only to the cards to buy: the planner compares it with the market prices of the printings.</item>
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
    /// <param name="prices">The market prices of the printings of the cards, by Oracle ID, or <see langword="null"/> to ignore the maximum prices.</param>
    /// <returns>One result for each line, in the order of the lines.</returns>
    public static IReadOnlyList<PurchasePlanLine> Plan(
        IReadOnlyList<PurchaseNeed> needs, IReadOnlyList<CollectionCopy> copies, IReadOnlySet<Guid> ownDeckIds, IReadOnlyDictionary<Guid, IReadOnlyList<PrintingPrices>>? prices = null)
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
            var lowest = remaining > 0 && prices is not null ? LowestPrice(need.Preferences, prices.GetValueOrDefault(need.OracleId) ?? []) : null;
            results[index] = new PurchasePlanLine(need, fromDeck, fromCollection, remaining, closest, lowest);
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

    /// <summary>
    /// Gets the lowest market price of a printing that fits the preferences, in the currency of the maximum price.
    /// The printing and finish preferences apply. The language and condition preferences do not apply, because the market prices have no language and no condition.
    /// </summary>
    /// <returns>The lowest price, or <see langword="null"/> when the preferences have no maximum price, or no fitting printing has a price in that currency.</returns>
    public static PriceLimit? LowestPrice(PurchasePreferences preferences, IReadOnlyList<PrintingPrices> printings)
    {
        if (preferences.MaximumPrice is not { } maximum || !PriceKeys.TryGetValue(maximum.Currency, out var keys))
        {
            return null;
        }

        var wanted = preferences.WantedPrintingIds ?? [];
        var avoided = preferences.AvoidedPrintingIds ?? [];
        var finishKeys = preferences.Foil switch
        {
            "foil" => keys.Skip(1),
            "nonfoil" => keys.Take(1),
            _ => keys,
        };
        var amounts = printings
            .Where(printing => (wanted.Count == 0 || wanted.Contains(printing.PrintingId)) && !avoided.Contains(printing.PrintingId))
            .SelectMany(printing => finishKeys.Select(key => printing.Prices.TryGetValue(key, out var amount) ? amount : (decimal?)null))
            .OfType<decimal>()
            .ToList();
        return amounts.Count == 0 ? null : new PriceLimit(amounts.Min(), maximum.Currency);
    }

    /// <summary>The keys of the market prices for each currency: the nonfoil price first, then the foil prices. Scryfall gives prices in US dollars and euros only.</summary>
    private static readonly Dictionary<string, string[]> PriceKeys = new(StringComparer.Ordinal)
    {
        ["USD"] = ["usd", "usd_foil", "usd_etched"],
        ["EUR"] = ["eur", "eur_foil"],
    };

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
