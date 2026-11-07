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

/// <summary>A card to remove from the assembled deck.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Quantity">The number of copies to remove.</param>
public sealed record TransitionRemovalResponse(Guid OracleId, int Quantity);

/// <summary>A card to add to the assembled deck.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Quantity">The number of copies to add.</param>
/// <param name="Take">The copies to take from the collection. A copy in a locked location needs an unlock first.</param>
/// <param name="ToBuy">The number of copies to buy.</param>
/// <param name="Closest">When <paramref name="ToBuy"/> is more than zero: the closest copies of the collection, with the reasons.</param>
public sealed record TransitionAdditionResponse(Guid OracleId, int Quantity, IReadOnlyList<PurchaseCopyResponse> Take, int ToBuy, IReadOnlyList<PurchaseCopyResponse> Closest);

/// <summary>A locked location that the plan takes cards from.</summary>
/// <param name="LocationId">The location or the section.</param>
/// <param name="LocationName">The name of the location.</param>
/// <param name="SectionName">The name of the section, or <see langword="null"/>.</param>
public sealed record LockedLocationResponse(Guid LocationId, string LocationName, string? SectionName);

/// <summary>The transition plan from the assembled version to the working list of a deck or a variant.</summary>
/// <param name="DeckId">The target deck or variant.</param>
/// <param name="AssembledDeckId">The assembled deck: the target or its main deck.</param>
/// <param name="AssembledVersionId">The assembled version of the assembled deck.</param>
/// <param name="Remove">The cards to remove from the assembled deck.</param>
/// <param name="Add">The cards to add, with the copies to take and the copies to buy.</param>
/// <param name="UnlockFirst">The locked locations to unlock before the cards move.</param>
/// <param name="TotalToBuy">The number of cards to buy.</param>
/// <param name="Cards">The data of the cards, by Oracle ID.</param>
/// <param name="Printings">The data of the printings, by printing ID.</param>
public sealed record TransitionPlanResponse(
    Guid DeckId,
    Guid AssembledDeckId,
    Guid AssembledVersionId,
    IReadOnlyList<TransitionRemovalResponse> Remove,
    IReadOnlyList<TransitionAdditionResponse> Add,
    IReadOnlyList<LockedLocationResponse> UnlockFirst,
    int TotalToBuy,
    IReadOnlyDictionary<Guid, DeckCardResponse> Cards,
    IReadOnlyDictionary<Guid, DeckPrintingResponse> Printings);

/// <summary>
/// The endpoints of the purchase list and the transition plan. See docs/architecture/purchase-list.md.
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

        api.MapGet("/decks/{deckId:guid}/transition-plan", GetTransitionPlanAsync)
            .WithTags("Purchase list")
            .AddEndpointFilter<DeckExceptionFilter>()
            .RequireAuthorization(AuthPolicies.User)
            .WithName("GetTransitionPlan")
            .WithSummary("Calculates the transition plan from the assembled version of the main deck to the working list of a deck or a variant: the cards to remove, the cards to take from the collection with their locations, and the cards to buy.")
            .RequireScope(Scopes.CollectionRead);
    }

    private static async Task<Ok<TransitionPlanResponse>> GetTransitionPlanAsync(
        Guid deckId, ClaimsPrincipal user, TransitionPlanService transitions, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var plan = await transitions.CalculateAsync(user.GetAccountId(), deckId, cancellationToken);
        var taken = plan.Additions.SelectMany(line => line.FromCollection).ToList();
        var printingIds = taken.Select(copy => copy.Copy.PrintingId).Concat(plan.Additions.SelectMany(line => line.Closest).Select(other => other.Copy.PrintingId));
        var (cards, printings) = await DeckEndpoints.CardDataAsync(
            catalog, plan.Removals.Select(removal => removal.OracleId).Concat(plan.Additions.Select(line => line.Need.OracleId)), printingIds, cancellationToken);
        return TypedResults.Ok(new TransitionPlanResponse(
            plan.Target.Id,
            plan.AssembledDeck.Id,
            plan.AssembledDeck.AssembledVersionId!.Value,
            [.. plan.Removals.Select(removal => new TransitionRemovalResponse(removal.OracleId, removal.Quantity))],
            [.. plan.Additions.Select(line => new TransitionAdditionResponse(
                line.Need.OracleId,
                line.Need.Quantity,
                [.. line.FromCollection.Select(copy => ToResponse(copy.Copy, copy.Quantity, []))],
                line.ToBuy,
                [.. line.Closest.Select(other => ToResponse(other.Copy, other.Quantity, other.Reasons))]))],
            [.. taken.Where(copy => copy.Copy.Locked).Select(copy => new LockedLocationResponse(copy.Copy.LocationId, copy.Copy.LocationName, copy.Copy.SectionName)).Distinct()],
            plan.Additions.Sum(line => line.ToBuy),
            cards,
            printings));
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
