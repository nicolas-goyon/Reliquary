using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Decks.Analysis;

/// <summary>
/// Finds the combos of Commander Spellbook in a deck: the complete combos, and the combos that need one more card.
/// </summary>
/// <remarks>
/// The search reads the commander, companion and main zones. See ADR 0030 and docs/architecture/deck-analysis.md.
/// The class has no input or output operations.
/// </remarks>
public static class DeckCombos
{
    /// <summary>The highest number of near combos in the response. The member <see cref="ComboAnalysis.NearCount"/> gives the full number.</summary>
    public const int MaxNearCombos = 50;

    /// <summary>
    /// Compares the combos with the cards of a deck.
    /// </summary>
    /// <param name="quantities">The quantity of each Oracle card in the commander, companion and main zones.</param>
    /// <param name="cards">The facts of the cards of the deck and of the combos.</param>
    /// <param name="search">The combos of the cards, or <see langword="null"/> when the instance has no combo data.</param>
    /// <returns>The combos of the deck.</returns>
    public static ComboAnalysis Find(IReadOnlyDictionary<Guid, int> quantities, IReadOnlyDictionary<Guid, CardFacts> cards, ComboSearch? search)
    {
        ArgumentNullException.ThrowIfNull(quantities);
        ArgumentNullException.ThrowIfNull(cards);
        if (search is not { Available: true })
        {
            return new ComboAnalysis(false, [], [], 0);
        }

        var complete = new List<DeckCombo>();
        var near = new List<DeckCombo>();
        foreach (var combo in search.Combos)
        {
            // A combo needs the facts of each card for the names and the mana values.
            if (combo.Cards.Count == 0 || combo.Cards.Any(card => !cards.ContainsKey(card.OracleId)))
            {
                continue;
            }

            var missing = combo.Cards.Where(card => quantities.GetValueOrDefault(card.OracleId) < card.Quantity).ToList();
            if (missing.Count > 1 || missing.Count == combo.Cards.Count)
            {
                continue;
            }

            var result = new DeckCombo(
                combo.Id,
                combo.Url,
                [.. combo.Cards.Select(card => new DeckComboCard(card.OracleId, cards[card.OracleId].Name, card.Quantity, quantities.GetValueOrDefault(card.OracleId) >= card.Quantity))],
                combo.Results,
                combo.GameEnding,
                combo.ManaNeeded,
                ManaValue(combo, cards),
                missing.Count == 0 ? null : missing[0].OracleId);
            (missing.Count == 0 ? complete : near).Add(result);
        }

        return new ComboAnalysis(true, complete, [.. near.Take(MaxNearCombos)], near.Count);
    }

    /// <summary>
    /// Tells whether a combo uses two cards: two distinct cards, or two copies of one card.
    /// </summary>
    /// <param name="combo">The combo.</param>
    /// <returns><see langword="true"/> for a two-card combo.</returns>
    public static bool IsTwoCard(DeckCombo combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        return combo.Cards.Sum(card => card.Quantity) == 2;
    }

    /// <summary>
    /// Gets the mana value of a combo: the larger of the mana that Commander Spellbook gives,
    /// and the sum of the mana values of the cards. The cards can start on the battlefield, so the mana of Commander Spellbook can omit them.
    /// </summary>
    private static int ManaValue(ComboFacts combo, IReadOnlyDictionary<Guid, CardFacts> cards)
    {
        var cardsValue = combo.Cards.Sum(card => cards[card.OracleId].ManaValue * card.Quantity);
        return (int)Math.Max(combo.ManaValueNeeded, Math.Ceiling(cardsValue));
    }
}
