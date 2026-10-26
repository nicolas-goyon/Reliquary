using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Cards.Api;

/// <summary>
/// The endpoints of the card reference data.
/// </summary>
internal static class CardEndpoints
{
    /// <summary>
    /// Maps the endpoints under <c>/cards</c>, <c>/printings</c> and <c>/sets</c>.
    /// </summary>
    /// <param name="api">The route group of the public API.</param>
    public static void Map(IEndpointRouteBuilder api)
    {
        var cards = api.MapGroup("/cards").WithTags("Cards");

        cards.MapGet("/named", GetCardByNameAsync)
            .WithName("GetCardByName")
            .WithSummary("Gets a card by one of its names: the Oracle name, a face name, a localized name or an alternate name.");

        cards.MapGet("/{id:guid}", GetCardAsync)
            .WithName("GetCard")
            .WithSummary("Gets a card with its faces, names, related cards and default printing.");

        cards.MapGet("/{id:guid}/printings", GetPrintingsAsync)
            .WithName("GetCardPrintings")
            .WithSummary("Gets the printings of a card, the most recent first.");

        cards.MapGet("/{id:guid}/rulings", GetRulingsAsync)
            .WithName("GetCardRulings")
            .WithSummary("Gets the rulings of a card.");

        api.MapGet("/printings/{id:guid}", GetPrintingAsync)
            .WithTags("Cards")
            .WithName("GetPrinting")
            .WithSummary("Gets a printing of a card.");

        api.MapGet("/sets", GetSetsAsync)
            .WithTags("Cards")
            .WithName("GetSets")
            .WithSummary("Gets all the sets, the most recent first.");
    }

    private static async Task<Results<Ok<CardResponse>, ProblemHttpResult>> GetCardAsync(Guid id, CardQueries queries, CancellationToken cancellationToken) =>
        await queries.GetCardAsync(id, cancellationToken) is { } card
            ? TypedResults.Ok(card)
            : ApiProblems.NotFound("The card does not exist. Check the identifier.");

    private static async Task<Results<Ok<CardResponse>, ProblemHttpResult>> GetCardByNameAsync(string? name, bool? fuzzy, CardQueries queries, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ApiProblems.Validation(new FieldError("name", FieldErrorCodes.Required, "The name is necessary."));
        }

        if (name.Length > 200)
        {
            return ApiProblems.Validation(new FieldError("name", FieldErrorCodes.TooLong, "The name must have 200 characters or fewer."));
        }

        var id = await queries.FindByNameAsync(name, fuzzy ?? false, cancellationToken);
        return id is { } found && await queries.GetCardAsync(found, cancellationToken) is { } card
            ? TypedResults.Ok(card)
            : ApiProblems.NotFound("No card has this name. Check the spelling, or use fuzzy=true.");
    }

    private static async Task<Results<Ok<Page<PrintingResponse>>, ProblemHttpResult>> GetPrintingsAsync(
        Guid id,
        string? lang,
        int? limit,
        string? cursor,
        CardQueries queries,
        CancellationToken cancellationToken)
    {
        if (!PageRequest.TryCreate(limit, cursor, out var page, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        if (!await queries.CardExistsAsync(id, cancellationToken))
        {
            return ApiProblems.NotFound("The card does not exist. Check the identifier.");
        }

        var result = await queries.GetPrintingsAsync(id, string.IsNullOrWhiteSpace(lang) ? null : lang, page, cancellationToken);
        return result is null ? ApiProblems.Validation(Cursors.InvalidCursor()) : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<IReadOnlyList<RulingResponse>>, ProblemHttpResult>> GetRulingsAsync(Guid id, CardQueries queries, CancellationToken cancellationToken) =>
        await queries.CardExistsAsync(id, cancellationToken)
            ? TypedResults.Ok(await queries.GetRulingsAsync(id, cancellationToken))
            : ApiProblems.NotFound("The card does not exist. Check the identifier.");

    private static async Task<Results<Ok<PrintingResponse>, ProblemHttpResult>> GetPrintingAsync(Guid id, CardQueries queries, CancellationToken cancellationToken) =>
        await queries.GetPrintingAsync(id, cancellationToken) is { } printing
            ? TypedResults.Ok(printing)
            : ApiProblems.NotFound("The printing does not exist. Check the identifier.");

    private static async Task<Ok<IReadOnlyList<SetResponse>>> GetSetsAsync(CardQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.GetSetsAsync(cancellationToken));
}
