using Reliquary.BuildingBlocks.Collection;
using Reliquary.Decks.Data;
using Reliquary.Decks.Versions;

namespace Reliquary.Decks.Purchases;

/// <summary>Copies of a card to remove from the assembled deck.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Quantity">The number of copies to remove.</param>
internal sealed record TransitionRemoval(Guid OracleId, int Quantity);

/// <summary>The transition plan from the assembled version of a deck to a list.</summary>
/// <param name="Target">The deck or the variant whose working list is the target.</param>
/// <param name="AssembledDeck">The assembled deck: the target itself or its main deck.</param>
/// <param name="Removals">The cards to remove from the assembled deck.</param>
/// <param name="Additions">The cards to add: the copies to take from the collection and the copies to buy.</param>
internal sealed record TransitionPlan(DeckRow Target, DeckRow AssembledDeck, IReadOnlyList<TransitionRemoval> Removals, IReadOnlyList<PurchasePlanLine> Additions);

/// <summary>
/// The transition plan: the physical steps from the assembled version of a deck to a variant or to the working list. See docs/architecture/purchase-list.md.
/// </summary>
internal sealed class TransitionPlanService(DeckService decks, DeckVersionService versions, ICollectionCatalog collection)
{
    /// <summary>
    /// Calculates the transition plan from the assembled version of the main deck to the working list of a deck or a variant.
    /// </summary>
    /// <exception cref="DeckException">The deck does not exist, the user is not its owner, or the main deck is not an assembled deck.</exception>
    public async Task<TransitionPlan> CalculateAsync(Guid ownerId, Guid deckId, CancellationToken cancellationToken)
    {
        var target = await decks.GetAsync(ownerId, deckId, cancellationToken);
        if (target.Deck.OwnerId != ownerId)
        {
            throw DeckException.NotFound();
        }

        var assembled = target.Deck.VariantOf is { } mainDeckId ? (await decks.GetAsync(ownerId, mainDeckId, cancellationToken)).Deck : target.Deck;
        if (assembled.AssembledVersionId is not { } assembledVersionId)
        {
            throw AssembledDeckErrorCodes.NotAssembled();
        }

        var (_, assembledLines, _) = await versions.GetAsync(ownerId, assembled.Id, assembledVersionId, cancellationToken);
        var before = assembledLines
            .Where(line => AssembledDeckService.PhysicalZones.Contains(line.Zone))
            .GroupBy(line => line.OracleId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        var targetLines = target.Lines.Where(line => AssembledDeckService.PhysicalZones.Contains(line.Zone)).ToList();
        var after = targetLines
            .GroupBy(line => line.OracleId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        var removals = before
            .Where(card => card.Value > after.GetValueOrDefault(card.Key))
            .Select(card => new TransitionRemoval(card.Key, card.Value - after.GetValueOrDefault(card.Key)))
            .ToList();
        var needs = targetLines
            .GroupBy(line => line.OracleId)
            .Where(group => after[group.Key] > before.GetValueOrDefault(group.Key))
            .Select(group => new PurchaseNeed(
                group.Key,
                group.First().Zone,
                after[group.Key] - before.GetValueOrDefault(group.Key),
                PurchasePreferences.FromJson(group.Select(line => line.PurchasePreferences).FirstOrDefault(json => json is not null))))
            .ToList();

        // The cards of the assembled deck are already in the plan. Its location is not a source.
        var copies = needs.Count == 0
            ? []
            : (await collection.GetCopiesAsync(ownerId, [.. needs.Select(need => need.OracleId)], cancellationToken))
                .Where(copy => copy.DeckId != assembled.Id)
                .ToList();
        return new TransitionPlan(target.Deck, assembled, removals, PurchasePlanner.Plan(needs, copies, new HashSet<Guid>()));
    }
}
