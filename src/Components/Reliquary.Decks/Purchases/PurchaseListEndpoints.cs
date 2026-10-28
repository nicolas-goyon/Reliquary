using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Purchases;

/// <summary>Copies of a card in one location entry of the collection.</summary>
/// <param name="LocationId">The location or the section.</param>
/// <param name="LocationName">The name of the location.</param>
/// <param name="SectionName">The name of the section, or <see langword="null"/>.</param>
/// <param name="LocationKind">The kind of the location.</param>
/// <param name="Locked">Whether the location is locked. The user must unlock it before the cards move.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="Language">The language.</param>
/// <param name="Finish">The finish, or <see langword="null"/>.</param>
/// <param name="Condition">The condition, or <see langword="null"/>.</param>
/// <param name="Quantity">The number of copies.</param>
/// <param name="Reasons">For a closest copy: why the copy does not fit the line. The values are <c>printing</c>, <c>language</c>, <c>finish</c>, <c>condition</c> and <c>unavailable</c>.</param>
public sealed record PurchaseCopyResponse(
    Guid LocationId,
    string LocationName,
    string? SectionName,
    string LocationKind,
    bool Locked,
    Guid? PrintingId,
    string Language,
    string? Finish,
    string? Condition,
    int Quantity,
    IReadOnlyList<string> Reasons);

/// <summary>One line of the purchase list.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Zone">The zone of the line.</param>
/// <param name="Quantity">The number of copies that the deck needs.</param>
/// <param name="PurchasePreferences">The purchase preferences of the line, or <see langword="null"/>.</param>
/// <param name="InDeck">The number of copies that are already in the location of the deck.</param>
/// <param name="FromCollection">The available copies that complete the line.</param>
/// <param name="ToBuy">The number of copies to buy.</param>
/// <param name="Closest">When <paramref name="ToBuy"/> is more than zero: the copies of the collection that do not fit the line, with the reasons. The best copies are first.</param>
public sealed record PurchaseListLineResponse(
    Guid OracleId,
    string Zone,
    int Quantity,
    PurchasePreferences? PurchasePreferences,
    int InDeck,
    IReadOnlyList<PurchaseCopyResponse> FromCollection,
    int ToBuy,
    IReadOnlyList<PurchaseCopyResponse> Closest);

/// <summary>The purchase list of a deck or a variant.</summary>
/// <param name="DeckId">The deck.</param>
/// <param name="IncludeTokens">Whether the list includes the tokens.</param>
/// <param name="Lines">The lines, in the order of the deck.</param>
/// <param name="TotalToBuy">The number of cards to buy.</param>
/// <param name="Cards">The data of the cards, by Oracle ID.</param>
/// <param name="Printings">The data of the printings, by printing ID.</param>
public sealed record PurchaseListResponse(
    Guid DeckId,
    bool IncludeTokens,
    IReadOnlyList<PurchaseListLineResponse> Lines,
    int TotalToBuy,
    IReadOnlyDictionary<Guid, DeckCardResponse> Cards,
    IReadOnlyDictionary<Guid, DeckPrintingResponse> Printings);

/// <summary>
/// The endpoint of the purchase list. See docs/architecture/purchase-list.md.
/// </summary>
internal static class PurchaseListEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/decks/{deckId:guid}/purchase-list", GetAsync)
            .WithTags("Purchase list")
            .AddEndpointFilter<DeckExceptionFilter>()
            .RequireAuthorization(AuthPolicies.User)
            .WithName("GetPurchaseList")
            .WithSummary("Calculates the purchase list of a deck or a variant: the cards of the deck minus the available cards of the collection. A line with cards to buy gives the closest copies of the collection.")
            .RequireScope(Scopes.CollectionRead);
    }

    private static async Task<Ok<PurchaseListResponse>> GetAsync(
        Guid deckId, bool? includeTokens, ClaimsPrincipal user, PurchaseListService purchases, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var tokens = includeTokens ?? false;
        var (deck, lines) = await purchases.CalculateAsync(user.GetAccountId(), deckId, tokens, cancellationToken);
        var printingIds = lines.SelectMany(line => line.FromDeck.Concat(line.FromCollection).Select(taken => taken.Copy.PrintingId).Concat(line.Closest.Select(other => other.Copy.PrintingId)));
        var (cards, printings) = await DeckEndpoints.CardDataAsync(catalog, lines.Select(line => line.Need.OracleId), printingIds, cancellationToken);
        return TypedResults.Ok(new PurchaseListResponse(
            deck.Id,
            tokens,
            [.. lines.Select(line => new PurchaseListLineResponse(
                line.Need.OracleId,
                line.Need.Zone,
                line.Need.Quantity,
                line.Need.Preferences.IsEmpty ? null : line.Need.Preferences,
                line.FromDeck.Sum(taken => taken.Quantity),
                [.. line.FromCollection.Select(taken => ToResponse(taken.Copy, taken.Quantity, []))],
                line.ToBuy,
                [.. line.Closest.Select(other => ToResponse(other.Copy, other.Quantity, other.Reasons))]))],
            lines.Sum(line => line.ToBuy),
            cards,
            printings));
    }

    private static PurchaseCopyResponse ToResponse(BuildingBlocks.Collection.CollectionCopy copy, int quantity, IReadOnlyList<string> reasons) =>
        new(copy.LocationId, copy.LocationName, copy.SectionName, copy.LocationKind, copy.Locked, copy.PrintingId, copy.Language, copy.Finish, copy.Condition, quantity, reasons);
}
