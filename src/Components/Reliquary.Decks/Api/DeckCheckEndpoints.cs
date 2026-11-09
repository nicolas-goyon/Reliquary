using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints that check a deck: the legality and the analysis.
/// </summary>
internal static class DeckCheckEndpoints
{
    internal static async Task<Ok<DeckLegalityResponse>> GetLegalityAsync(Guid deckId, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(user.FindAccountId(), deckId, cancellationToken);
        var cards = await catalog.GetCardsAsync([.. view.Lines.Select(line => line.OracleId).Distinct()], cancellationToken);
        var problems = Legality.DeckLegality.Check(view.Deck.Format, view.Lines.Select(line => (line.Zone, line.OracleId, line.Quantity)), cards);
        return TypedResults.Ok(new DeckLegalityResponse(view.Deck.Format, problems.Count == 0, problems));
    }

    internal static async Task<Ok<Analysis.DeckAnalysis>> GetAnalysisAsync(
        Guid deckId,
        ClaimsPrincipal user,
        DeckService decks,
        ICardCatalog catalog,
        IComboCatalog combos,
        CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(user.FindAccountId(), deckId, cancellationToken);
        var deckCards = view.Lines.Where(line => Analysis.DeckAnalyzer.Zones.Contains(line.Zone)).Select(line => line.OracleId).Distinct().ToList();
        var search = await combos.FindCombosAsync(deckCards, cancellationToken);

        // The facts of the cards of the combos give the names of the missing cards.
        var oracleIds = view.Lines.Select(line => line.OracleId).Concat(search.Combos.SelectMany(combo => combo.Cards).Select(card => card.OracleId)).Distinct().ToList();
        var cards = await catalog.GetCardsAsync(oracleIds, cancellationToken);
        var lines = view.Lines.Select(line => new Analysis.AnalysisLine(line.Zone, line.OracleId, line.Quantity, line.Categories));
        return TypedResults.Ok(Analysis.DeckAnalyzer.Analyze(view.Deck.Format, lines, cards, search.Available ? search : null));
    }
}
