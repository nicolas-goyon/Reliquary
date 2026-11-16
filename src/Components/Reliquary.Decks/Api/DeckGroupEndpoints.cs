using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using static Reliquary.Decks.Api.DeckRequestRules;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints that act on several decks of a user: the deck tags and the group actions.
/// </summary>
internal static class DeckGroupEndpoints
{
    internal static async Task<Ok<IReadOnlyList<DeckTagSummaryResponse>>> ListDeckTagsAsync(ClaimsPrincipal user, DeckService decks, CancellationToken cancellationToken)
    {
        var tags = await decks.ListDeckTagsAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<DeckTagSummaryResponse>>([.. tags.Select(tag => new DeckTagSummaryResponse(tag.Name, tag.DeckCount))]);
    }

    internal static async Task<Results<Ok<DeckGroupResponse>, ProblemHttpResult>> ApplyGroupActionAsync(
        DeckGroupRequest request, ClaimsPrincipal user, DeckService decks, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var deckIds = ValidateDeckIds(errors, request.DeckIds);
        if (string.IsNullOrEmpty(request.Action))
        {
            errors.Add(new FieldError("action", FieldErrorCodes.Required, "The action is necessary."));
        }
        else
        {
            AddChoiceError(errors, "action", request.Action, GroupActions);
        }

        var tag = request.Tag?.Trim();
        if (request.Action is "tag" or "untag")
        {
            ValidateDeckTag(errors, "tag", tag);
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        if (request.Action == "delete")
        {
            foreach (var deckId in deckIds)
            {
                await decks.DeleteAsync(user.GetAccountId(), deckId, cancellationToken);
            }

            return TypedResults.Ok(new DeckGroupResponse(deckIds.Count));
        }

        var count = await decks.ApplyGroupActionAsync(user.GetAccountId(), deckIds, new DeckGroupAction(request.Action!, request.FolderId, tag), cancellationToken);
        return TypedResults.Ok(new DeckGroupResponse(count));
    }

    private static readonly string[] GroupActions = ["move", "tag", "untag", "pin", "unpin", "archive", "unarchive", "delete"];
}
