using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Purchases;

/// <summary>
/// The purchase list of a deck: the cards of the deck minus the available cards of the collection. See docs/architecture/purchase-list.md.
/// </summary>
internal sealed class PurchaseListService(DeckService decks, DeckStore store, ICollectionCatalog collection, ICardCatalog catalog)
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

        // Only the lines with a maximum price need the market prices.
        var priced = needs.Where(need => need.Preferences.MaximumPrice is not null).Select(need => need.OracleId).Distinct().ToList();
        var prices = priced.Count == 0 ? new Dictionary<Guid, IReadOnlyList<PrintingPrices>>() : await catalog.GetPricesAsync(priced, cancellationToken);
        return (view.Deck, PurchasePlanner.Plan(needs, copies, own, prices));
    }

    /// <summary>
    /// Finds the decks of the owner that are complete: their purchase list, with no tokens, has no card to buy.
    /// A deck with no line in the zones of the purchase list is complete.
    /// </summary>
    /// <param name="ownerId">The owner of the decks.</param>
    /// <param name="deckIds">The decks. They must be decks of the owner, and not variants.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The IDs of the complete decks.</returns>
    public async Task<IReadOnlySet<Guid>> FindCompleteDecksAsync(Guid ownerId, IReadOnlyCollection<Guid> deckIds, CancellationToken cancellationToken)
    {
        if (deckIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        IReadOnlyList<DeckLineRow> lines;
        await using (var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken))
        {
            lines = await DeckStore.ListLinesOfDecksAsync(connection, deckIds, Zones, cancellationToken);
        }

        var oracleIds = lines.Select(line => line.OracleId).Distinct().ToList();
        var copies = oracleIds.Count == 0 ? [] : await collection.GetCopiesAsync(ownerId, oracleIds, cancellationToken);
        var copiesByCard = copies.ToLookup(copy => copy.OracleId);
        var linesByDeck = lines.ToLookup(line => line.DeckId);
        var complete = new HashSet<Guid>();
        foreach (var deckId in deckIds)
        {
            var needs = linesByDeck[deckId]
                .Select(line => new PurchaseNeed(line.OracleId, line.Zone, line.Quantity, PurchasePreferences.FromJson(line.PurchasePreferences)))
                .ToList();
            var deckCopies = needs.Select(need => need.OracleId).Distinct().SelectMany(oracleId => copiesByCard[oracleId]).ToList();
            if (PurchasePlanner.Plan(needs, deckCopies, new HashSet<Guid> { deckId }).All(line => line.ToBuy == 0))
            {
                complete.Add(deckId);
            }
        }

        return complete;
    }
}
