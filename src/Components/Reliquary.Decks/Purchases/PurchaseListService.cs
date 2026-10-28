using Reliquary.BuildingBlocks.Collection;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Purchases;

/// <summary>
/// The purchase list of a deck: the cards of the deck minus the available cards of the collection. See docs/architecture/purchase-list.md.
/// </summary>
internal sealed class PurchaseListService(DeckService decks, ICollectionCatalog collection)
{
    /// <summary>The zones of the purchase list. The considering zone is never in the list. The tokens are an option.</summary>
    public static readonly IReadOnlyList<string> Zones = ["commander", "companion", "main", "sideboard"];

    /// <summary>
    /// Calculates the purchase list of a deck or a variant of the owner.
    /// </summary>
    /// <param name="ownerId">The owner of the deck.</param>
    /// <param name="deckId">The deck or the variant.</param>
    /// <param name="includeTokens">Whether the list includes the tokens zone.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="DeckException">The deck does not exist, or the user is not its owner.</exception>
    public async Task<(DeckRow Deck, IReadOnlyList<PurchasePlanLine> Lines)> CalculateAsync(Guid ownerId, Guid deckId, bool includeTokens, CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(ownerId, deckId, cancellationToken);
        if (view.Deck.OwnerId != ownerId)
        {
            throw DeckException.NotFound();
        }

        var needs = view.Lines
            .Where(line => Zones.Contains(line.Zone) || (includeTokens && line.Zone == "tokens"))
            .Select(line => new PurchaseNeed(line.OracleId, line.Zone, line.Quantity, PurchasePreferences.FromJson(line.PurchasePreferences)))
            .ToList();
        var copies = needs.Count == 0
            ? []
            : await collection.GetCopiesAsync(ownerId, [.. needs.Select(need => need.OracleId).Distinct()], cancellationToken);

        // The location of the deck counts as the deck. A variant takes the cards of its main deck first.
        HashSet<Guid> own = [view.Deck.Id];
        if (view.Deck.VariantOf is { } mainDeckId)
        {
            own.Add(mainDeckId);
        }

        return (view.Deck, PurchasePlanner.Plan(needs, copies, own));
    }
}
