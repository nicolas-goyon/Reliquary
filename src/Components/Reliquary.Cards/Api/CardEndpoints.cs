using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Cards.Search;

namespace Reliquary.Cards.Api;

/// <summary>
/// The endpoints of the card reference data.
/// </summary>
internal static class CardEndpoints
{
    /// <summary>The maximum number of characters of a search query. Scryfall has the same limit.</summary>
    public const int MaxQueryLength = 1000;

    /// <summary>
    /// Maps the endpoints under <c>/cards</c>, <c>/printings</c> and <c>/sets</c>.
    /// </summary>
    /// <param name="api">The route group of the public API.</param>
    public static void Map(IEndpointRouteBuilder api)
    {
        var cards = api.MapGroup("/cards").WithTags("Cards");

        cards.MapGet("/search", SearchCardsAsync)
            .WithName("SearchCards")
            .WithSummary("Searches the cards with a query in the Scryfall syntax.")
            .WithDescription(
                "The parameter q contains the query, for example \"t:creature c:g mv<=2\". "
                + "The keywords unique:, order:, dir: and prefer: in the query win over the parameters unique, order and dir. "
                + "Specific error codes: search_syntax_error (with the member position), search_query_empty and search_timeout.");

        cards.MapGet("/autocomplete", AutocompleteAsync)
            .WithName("AutocompleteCardNames")
            .WithSummary("Suggests cards for the start of a name, in all the languages. The parameter q needs 2 characters or more.");

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

    /// <summary>The maximum number of suggestions of the autocomplete.</summary>
    public const int MaxAutocompleteLimit = 20;

    private static async Task<Results<Ok<IReadOnlyList<AutocompleteItem>>, ProblemHttpResult>> AutocompleteAsync(
        string? q, string? lang, int? limit, bool? includeExtras, CardQueries queries, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaxAutocompleteLimit)
        {
            return ApiProblems.Validation(new FieldError("limit", FieldErrorCodes.OutOfRange, $"The limit must be from 1 to {MaxAutocompleteLimit}."));
        }

        if (q is null || q.Trim().Length < 2)
        {
            return TypedResults.Ok<IReadOnlyList<AutocompleteItem>>([]);
        }

        if (q.Length > 100)
        {
            return ApiProblems.Validation(new FieldError("q", FieldErrorCodes.TooLong, "The text must have 100 characters or fewer."));
        }

        var items = await queries.AutocompleteAsync(q, lang, limit ?? 10, includeExtras == true, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok<CardSearchResponse>, ProblemHttpResult>> SearchCardsAsync(
        string? q,
        string? unique,
        string? order,
        string? dir,
        int? limit,
        string? cursor,
        CardSearch search,
        CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(q))
        {
            errors.Add(new FieldError("q", FieldErrorCodes.Required, "The query is necessary."));
        }
        else if (q.Length > MaxQueryLength)
        {
            errors.Add(new FieldError("q", FieldErrorCodes.TooLong, $"The query must have {MaxQueryLength} characters or fewer."));
        }

        SearchUnique? uniqueMode = unique?.ToLowerInvariant() switch
        {
            null or "" or "cards" => SearchUnique.Cards,
            "prints" => SearchUnique.Prints,
            "art" => SearchUnique.Art,
            _ => null,
        };
        if (uniqueMode is null)
        {
            errors.Add(new FieldError("unique", FieldErrorCodes.NotAllowed, "The value of unique must be cards, prints or art."));
        }

        var sortOrder = SearchOrder.Name;
        if (!string.IsNullOrEmpty(order) && !SearchCompiler.TryParseOrder(order, out sortOrder))
        {
            errors.Add(new FieldError("order", FieldErrorCodes.NotAllowed, "The value of order is not a known sort order. The OpenAPI description lists the sort orders."));
        }

        var direction = SearchDirection.Auto;
        if (!string.IsNullOrEmpty(dir) && !SearchCompiler.TryParseDirection(dir, out direction))
        {
            errors.Add(new FieldError("dir", FieldErrorCodes.NotAllowed, "The value of dir must be asc, desc or auto."));
        }

        if (!PageRequest.TryCreate(limit, cursor, out var page, out var pageError))
        {
            errors.Add(pageError!);
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var outcome = await search.SearchAsync(new CardSearchRequest(q!, new SearchDefaults(uniqueMode!.Value, sortOrder, direction), page), cancellationToken);
        return outcome switch
        {
            CardSearchSucceeded succeeded => TypedResults.Ok(succeeded.Response),
            CardSearchInvalidCursor => ApiProblems.Validation(Cursors.InvalidCursor()),
            CardSearchFailed failed => ApiProblems.Problem(
                StatusCodes.Status400BadRequest,
                failed.Code,
                failed.Message,
                new Dictionary<string, object?> { ["position"] = failed.Position, ["warnings"] = failed.Warnings }),
            _ => throw new InvalidOperationException("The search gave an unknown result."),
        };
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
