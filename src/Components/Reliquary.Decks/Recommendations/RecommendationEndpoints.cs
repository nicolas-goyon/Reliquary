using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Recommendations;

/// <summary>
/// The endpoint of the card recommendations of a deck.
/// </summary>
internal static class RecommendationEndpoints
{
    /// <summary>The number of cards when the request gives no limit.</summary>
    public const int DefaultLimit = 30;

    /// <summary>The maximum number of cards in one response.</summary>
    public const int MaxLimit = 100;

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGroup("/decks")
            .WithTags("Decks")
            .AddEndpointFilter<DeckExceptionFilter>()
            .RequireAuthorization(AuthPolicies.User)
            .MapGet("/{deckId:guid}/recommendations", GetAsync)
            .WithName("GetDeckRecommendations")
            .WithSummary("Gets card recommendations for the commanders of a deck, from recommander.cards. Only the owner gets them. The list does not contain the cards of the deck. Show the credit of the source with the list.")
            .RequireScope(Scopes.DecksRead);
    }

    private static async Task<Results<Ok<DeckRecommendationsResponse>, ProblemHttpResult>> GetAsync(
        Guid deckId, int? limit, ClaimsPrincipal user, DeckService decks, RecommendationService recommendations, CancellationToken cancellationToken)
    {
        if (!recommendations.Enabled)
        {
            return ApiProblems.Problem(
                StatusCodes.Status404NotFound,
                RecommendationCodes.Disabled,
                "The recommendations are not enabled on this instance. The administrator can enable them with the setting Recommendations:Enabled.");
        }

        if (limit is < 1 or > MaxLimit)
        {
            return ApiProblems.Validation(new FieldError("limit", FieldErrorCodes.InvalidFormat, $"Give a limit from 1 to {MaxLimit}."));
        }

        var view = await decks.GetAsync(user.GetAccountId(), deckId, cancellationToken);
        if (!view.IsOwner)
        {
            throw DeckException.NotFound();
        }

        var commanders = view.Lines
            .Where(line => line.Zone == "commander")
            .OrderBy(line => line.CreatedAt)
            .Select(line => line.OracleId)
            .Distinct()
            .ToList();
        var deckCards = view.Lines.Select(line => line.OracleId).ToHashSet();
        return TypedResults.Ok(await recommendations.RecommendAsync(commanders, deckCards, limit ?? DefaultLimit, cancellationToken));
    }
}
