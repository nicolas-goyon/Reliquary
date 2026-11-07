using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Exploration;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints of the public decks: the search, the favorites and the authors.
/// </summary>
internal static class PublicDeckEndpoints
{
    private static readonly string[] Sorts = ["recent", "popular"];

    public static void Map(IEndpointRouteBuilder api)
    {
        var decks = api.MapGroup("/public-decks").WithTags("Public decks").AddEndpointFilter<DeckExceptionFilter>();
        var user = decks.MapGroup(string.Empty).RequireAuthorization(AuthPolicies.User);

        decks.MapGet(string.Empty, SearchAsync)
            .WithName("SearchPublicDecks")
            .WithSummary("Searches the public decks of all the users by name, format, commander, color identity, card and author. A visitor with no account can search.");

        user.MapGet("/favorites", ListFavoritesAsync)
            .WithName("ListFavoriteDecks")
            .WithSummary("Lists the favorite public decks of the user.");

        user.MapGet("/following", ListFollowedDecksAsync)
            .WithName("ListFollowedAuthorDecks")
            .WithSummary("Lists the public decks of the authors that the user follows, the most recent change first.");

        decks.MapGet("/{deckId:guid}/social", GetSocialAsync)
            .WithName("GetPublicDeckSocial")
            .WithSummary("Tells whether a deck is public, whether it is a favorite of the user, its numbers of favorites and copies, and its author.");

        user.MapPut("/{deckId:guid}/favorite", AddFavoriteAsync)
            .WithName("AddFavoriteDeck")
            .WithSummary("Adds a public deck to the favorites of the user.");

        user.MapDelete("/{deckId:guid}/favorite", RemoveFavoriteAsync)
            .WithName("RemoveFavoriteDeck")
            .WithSummary("Removes a deck from the favorites of the user.");

        var authors = api.MapGroup("/authors").WithTags("Public decks").AddEndpointFilter<DeckExceptionFilter>();
        var follower = authors.MapGroup(string.Empty).RequireAuthorization(AuthPolicies.User);

        follower.MapGet("/following", ListFollowedAuthorsAsync)
            .WithName("ListFollowedAuthors")
            .WithSummary("Lists the authors that the user follows.");

        authors.MapGet("/{authorId:guid}", GetAuthorAsync)
            .WithName("GetAuthor")
            .WithSummary("Gets the profile of an author: display name, number of public decks and number of followers.");

        follower.MapPut("/{authorId:guid}/follow", FollowAsync)
            .WithName("FollowAuthor")
            .WithSummary("Follows an author.");

        follower.MapDelete("/{authorId:guid}/follow", UnfollowAsync)
            .WithName("UnfollowAuthor")
            .WithSummary("Stops to follow an author.");
    }

    private static async Task<Results<Ok<Page<PublicDeckResponse>>, ProblemHttpResult>> SearchAsync(
        PublicDeckService service,
        ICardCatalog catalog,
        int? limit,
        string? cursor,
        string? q,
        string? format,
        string? commander,
        string? colors,
        string? card,
        Guid? author,
        string? sort,
        CancellationToken cancellationToken)
    {
        if (!TryReadPage(limit, cursor, out var page, out var offset, out var pageError))
        {
            return ApiProblems.Validation(pageError!);
        }

        var errors = new List<FieldError>();
        var colorIdentity = colors is null ? null : DeckEndpoints.ReadColors(colors);
        if (colors is not null && colorIdentity is null)
        {
            errors.Add(new FieldError("colors", FieldErrorCodes.InvalidFormat, "Give the letters W, U, B, R and G, or C for colorless."));
        }

        if (sort is not null && !Sorts.Contains(sort))
        {
            errors.Add(new FieldError("sort", FieldErrorCodes.NotAllowed, $"Use one of these values: {string.Join(", ", Sorts)}."));
        }

        var commanderId = await ResolveCardAsync(catalog, errors, "commander", commander, cancellationToken);
        var cardId = await ResolveCardAsync(catalog, errors, "card", card, cancellationToken);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var query = new PublicDeckQuery(
            string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            format,
            colorIdentity,
            commanderId,
            cardId,
            author is { } authorId ? [authorId] : null,
            null,
            sort ?? "recent",
            offset,
            page.Limit + 1);
        return TypedResults.Ok(ToPage(await service.SearchAsync(query, cancellationToken), page.Limit, offset));
    }

    private static async Task<Results<Ok<Page<PublicDeckResponse>>, ProblemHttpResult>> ListFavoritesAsync(
        ClaimsPrincipal user, PublicDeckService service, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (!TryReadPage(limit, cursor, out var page, out var offset, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        var query = new PublicDeckQuery(null, null, null, null, null, null, user.GetAccountId(), "recent", offset, page.Limit + 1);
        return TypedResults.Ok(ToPage(await service.SearchAsync(query, cancellationToken), page.Limit, offset));
    }

    private static async Task<Results<Ok<Page<PublicDeckResponse>>, ProblemHttpResult>> ListFollowedDecksAsync(
        ClaimsPrincipal user, PublicDeckService service, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (!TryReadPage(limit, cursor, out var page, out var offset, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        return TypedResults.Ok(ToPage(await service.ListFollowedDecksAsync(user.GetAccountId(), offset, page.Limit + 1, cancellationToken), page.Limit, offset));
    }

    private static async Task<Ok<PublicDeckSocialResponse>> GetSocialAsync(Guid deckId, ClaimsPrincipal user, PublicDeckService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetSocialAsync(user.FindAccountId(), deckId, cancellationToken));

    private static async Task<NoContent> AddFavoriteAsync(Guid deckId, ClaimsPrincipal user, PublicDeckService service, CancellationToken cancellationToken)
    {
        await service.SetFavoriteAsync(user.GetAccountId(), deckId, true, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> RemoveFavoriteAsync(Guid deckId, ClaimsPrincipal user, PublicDeckService service, CancellationToken cancellationToken)
    {
        await service.SetFavoriteAsync(user.GetAccountId(), deckId, false, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<AuthorSummaryResponse>>> ListFollowedAuthorsAsync(ClaimsPrincipal user, PublicDeckService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListFollowedAuthorsAsync(user.GetAccountId(), cancellationToken));

    private static async Task<Ok<AuthorResponse>> GetAuthorAsync(Guid authorId, ClaimsPrincipal user, PublicDeckService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAuthorAsync(user.FindAccountId(), authorId, cancellationToken));

    private static async Task<NoContent> FollowAsync(Guid authorId, ClaimsPrincipal user, PublicDeckService service, CancellationToken cancellationToken)
    {
        await service.SetFollowAsync(user.GetAccountId(), authorId, true, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> UnfollowAsync(Guid authorId, ClaimsPrincipal user, PublicDeckService service, CancellationToken cancellationToken)
    {
        await service.SetFollowAsync(user.GetAccountId(), authorId, false, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Reads a card parameter: an Oracle ID or a card name in any language.
    /// </summary>
    private static async Task<Guid?> ResolveCardAsync(ICardCatalog catalog, List<FieldError> errors, string field, string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out var oracleId))
        {
            return oracleId;
        }

        var matches = await catalog.ResolveNamesAsync([value.Trim()], cancellationToken);
        if (matches.Values.SingleOrDefault() is { Status: CardNameStatus.Found, OracleId: { } found })
        {
            return found;
        }

        errors.Add(new FieldError(field, DeckFieldErrorCodes.CardNotFound, "No card has this name."));
        return null;
    }

    private static bool TryReadPage(int? limit, string? cursor, out PageRequest page, out int offset, out FieldError? error)
    {
        offset = 0;
        if (!PageRequest.TryCreate(limit, cursor, out page, out error))
        {
            return false;
        }

        if (page.Cursor is { } value)
        {
            if (!Cursors.TryDecode<OffsetCursor>(value, out var position) || position!.Offset < 0)
            {
                error = Cursors.InvalidCursor();
                return false;
            }

            offset = position.Offset;
        }

        return true;
    }

    private static Page<PublicDeckResponse> ToPage(IReadOnlyList<PublicDeckResponse> rows, int limit, int offset) =>
        new([.. rows.Take(limit)], rows.Count > limit ? Cursors.Encode(new OffsetCursor(offset + limit)) : null);

    private sealed record OffsetCursor(int Offset);
}
