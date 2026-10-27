using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.AutoTag;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints of auto-tag. See ADR 0031.
/// </summary>
internal static class AutoTagEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var autoTag = api.MapGroup("/decks/{deckId:guid}/auto-tag")
            .WithTags("Decks")
            .AddEndpointFilter<DeckExceptionFilter>()
            .RequireAuthorization(AuthPolicies.User);

        autoTag.MapGet(string.Empty, GetAsync)
            .WithName("SuggestDeckCategories")
            .WithSummary("Suggests the categories of each line of a deck of the user, with the reason of each category: a user rule, an Oracle tag or a text rule.");

        autoTag.MapPost(string.Empty, ApplyAsync)
            .WithName("ApplyDeckCategories")
            .WithSummary("Sets the categories of lines of a deck of the user in one change. A choice that differs from the automatic suggestion becomes a rule of the user.");
    }

    private static async Task<Ok<AutoTagResponse>> GetAsync(Guid deckId, ClaimsPrincipal user, AutoTagService autoTag, CancellationToken cancellationToken)
    {
        var (deck, lines) = await autoTag.SuggestAsync(user.GetAccountId(), deckId, cancellationToken);
        return TypedResults.Ok(new AutoTagResponse(
            deck.Id,
            deck.Version,
            [.. lines.Select(item => new AutoTagLineResponse(
                item.Line.Id,
                item.Line.OracleId,
                item.Name,
                item.Line.Zone,
                item.Line.Categories,
                item.Suggestion.Categories,
                item.Suggestion.Source,
                [.. item.Suggestion.Reasons.Select(reason => new AutoTagReasonResponse(reason.Category, reason.Source, reason.Detail))],
                !item.Suggestion.Categories.ToHashSet().SetEquals(item.Line.Categories)))]));
    }

    private static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> ApplyAsync(
        Guid deckId, ApplyAutoTagRequest request, ClaimsPrincipal user, AutoTagService autoTag, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Lines is not { Count: > 0 })
        {
            errors.Add(new FieldError("lines", FieldErrorCodes.Required, "Give one line or more."));
        }
        else if (request.Lines.Count > DeckRules.MaxLines)
        {
            errors.Add(new FieldError("lines", FieldErrorCodes.TooLong, $"Give {DeckRules.MaxLines} lines or fewer."));
        }
        else
        {
            for (var index = 0; index < request.Lines.Count; index++)
            {
                var line = request.Lines[index];
                if (line.LineId is null)
                {
                    errors.Add(new FieldError($"lines[{index}].lineId", FieldErrorCodes.Required, "The ID of the line is necessary."));
                }

                if (line.Categories is null)
                {
                    errors.Add(new FieldError($"lines[{index}].categories", FieldErrorCodes.Required, "Give the categories of the line. An empty list removes all the categories."));
                    continue;
                }

                for (var category = 0; category < line.Categories.Count; category++)
                {
                    if (!DeckRules.Categories.Contains(line.Categories[category]))
                    {
                        errors.Add(new FieldError($"lines[{index}].categories[{category}]", FieldErrorCodes.NotAllowed, $"The category must be one of these categories: {string.Join(", ", DeckRules.Categories)}."));
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var choices = request.Lines!.Select(line => new CategoryChoice(line.LineId!.Value, line.Categories!)).ToList();
        var view = await autoTag.ApplyAsync(user.GetAccountId(), deckId, choices, request.ExpectedVersion, cancellationToken);
        return TypedResults.Ok(await DeckEndpoints.ToResponseAsync(view, catalog, cancellationToken));
    }
}

/// <summary>The suggestions of auto-tag for a deck.</summary>
/// <param name="DeckId">The deck.</param>
/// <param name="Version">The version of the deck. Send it back in <c>expectedVersion</c> to apply the suggestions.</param>
/// <param name="Lines">One suggestion for each line, except the lines of the tokens zone.</param>
public sealed record AutoTagResponse(Guid DeckId, int Version, IReadOnlyList<AutoTagLineResponse> Lines);

/// <summary>The suggestion of auto-tag for one deck line.</summary>
/// <param name="LineId">The line.</param>
/// <param name="OracleId">The Oracle card of the line.</param>
/// <param name="Name">The Oracle name of the card.</param>
/// <param name="Zone">The zone of the line.</param>
/// <param name="Categories">The current categories of the line.</param>
/// <param name="Suggested">The suggested categories.</param>
/// <param name="Source">The main source of the suggestion: <c>user_rule</c>, <c>oracle_tag</c>, <c>text_rule</c> or <c>none</c>.</param>
/// <param name="Reasons">The reason for each suggested category.</param>
/// <param name="Changes">Whether the suggested categories differ from the current categories.</param>
public sealed record AutoTagLineResponse(
    Guid LineId,
    Guid OracleId,
    string Name,
    string Zone,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Suggested,
    string Source,
    IReadOnlyList<AutoTagReasonResponse> Reasons,
    bool Changes);

/// <summary>The reason for one suggested category.</summary>
/// <param name="Category">The category.</param>
/// <param name="Source">The source: <c>user_rule</c>, <c>oracle_tag</c> or <c>text_rule</c>.</param>
/// <param name="Detail">The slug of the Oracle tag, or the code of the text rule. <see langword="null"/> for a user rule.</param>
public sealed record AutoTagReasonResponse(string Category, string Source, string? Detail);

/// <summary>The categories that the user accepts or changes.</summary>
/// <param name="Lines">The categories of each line to change. The other lines do not change.</param>
/// <param name="ExpectedVersion">The version that the client read. The API refuses the change when the deck has another version.</param>
public sealed record ApplyAutoTagRequest(IReadOnlyList<AutoTagChoiceRequest>? Lines, int? ExpectedVersion);

/// <summary>The categories of one deck line.</summary>
/// <param name="LineId">The line.</param>
/// <param name="Categories">The categories. The list replaces the categories of the line.</param>
public sealed record AutoTagChoiceRequest(Guid? LineId, IReadOnlyList<string>? Categories);
