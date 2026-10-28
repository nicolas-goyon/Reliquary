using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints of the tags of a user.
/// </summary>
internal static class TagEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var tags = api.MapGroup("/tags").WithTags("Decks").AddEndpointFilter<DeckExceptionFilter>().RequireAuthorization(AuthPolicies.User);

        tags.MapGet(string.Empty, ListAsync)
            .WithName("ListTags")
            .WithSummary("Lists the global tags of the user, with the number of deck lines that use each tag.")
            .RequireScope(Scopes.DecksRead);

        tags.MapPatch("/{tagId:guid}", RenameAsync)
            .WithName("RenameTag")
            .WithSummary("Renames a global or local tag. When another tag of the same kind has the new name, the two tags become one tag.")
            .RequireScope(Scopes.DecksWrite);

        tags.MapDelete("/{tagId:guid}", DeleteAsync)
            .WithName("DeleteTag")
            .WithSummary("Deletes a tag. The deck lines keep their cards.")
            .RequireScope(Scopes.DecksWrite);
    }

    private static async Task<Ok<IReadOnlyList<TagResponse>>> ListAsync(ClaimsPrincipal user, TagService tags, CancellationToken cancellationToken)
    {
        var rows = await tags.ListGlobalAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<TagResponse>>([.. rows.Select(row => new TagResponse(row.Id, row.Name, row.LineCount))]);
    }

    private static async Task<Results<Ok<RenamedTagResponse>, ProblemHttpResult>> RenameAsync(Guid tagId, RenameTagRequest request, ClaimsPrincipal user, TagService tags, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return ApiProblems.Validation(new FieldError("name", FieldErrorCodes.Required, "The name of the tag is necessary."));
        }

        if (name.Length > DeckRules.MaxTagLength || name.Contains('#', StringComparison.Ordinal))
        {
            return ApiProblems.Validation(new FieldError("name", FieldErrorCodes.InvalidFormat, $"A tag must have {DeckRules.MaxTagLength} characters or fewer, and no \"#\"."));
        }

        var id = await tags.RenameAsync(user.GetAccountId(), tagId, name, cancellationToken);
        return TypedResults.Ok(new RenamedTagResponse(id, name));
    }

    private static async Task<NoContent> DeleteAsync(Guid tagId, ClaimsPrincipal user, TagService tags, CancellationToken cancellationToken)
    {
        await tags.DeleteAsync(user.GetAccountId(), tagId, cancellationToken);
        return TypedResults.NoContent();
    }
}
