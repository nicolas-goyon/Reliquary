using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using static Reliquary.Decks.Api.DeckRequestRules;
using static Reliquary.Decks.Api.DeckResponses;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints of the lines of a deck.
/// </summary>
internal static class DeckLineEndpoints
{
    internal static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> AddLinesAsync(
        Guid deckId, AddLinesRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Lines is not { Count: > 0 })
        {
            errors.Add(new FieldError("lines", FieldErrorCodes.Required, "Give one line or more."));
        }

        var lines = await ReadLinesAsync(errors, request.Lines ?? [], catalog, cancellationToken);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var view = await decks.AddLinesAsync(user.GetAccountId(), deckId, lines, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    internal static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> ReplaceLinesAsync(
        Guid deckId, ReplaceLinesRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Lines is null)
        {
            errors.Add(new FieldError("lines", FieldErrorCodes.Required, "Give the list of the lines. An empty list removes all the lines."));
        }

        var lines = await ReadLinesAsync(errors, request.Lines ?? [], catalog, cancellationToken);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var view = await decks.ReplaceLinesAsync(user.GetAccountId(), deckId, lines, request.ExpectedVersion, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    internal static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> UpdateLineAsync(
        Guid deckId, Guid lineId, UpdateLineRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Quantity is < 0 or > DeckRules.MaxQuantity)
        {
            errors.Add(new FieldError("quantity", FieldErrorCodes.OutOfRange, $"The quantity must be from 0 to {DeckRules.MaxQuantity}."));
        }

        ValidateLineFields(errors, string.Empty, request.Zone, request.Finish, request.Note);
        ValidateTags(errors, string.Empty, request.Tags, request.GlobalTags, request.Categories);
        if (request.PurchasePreferences is { } preferences)
        {
            errors.AddRange(preferences.Validate("purchasePreferences"));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var view = await decks.UpdateLineAsync(
            user.GetAccountId(),
            deckId,
            lineId,
            new LineChanges(
                request.Quantity,
                request.Zone,
                request.PrintingId,
                request.ClearPrinting == true,
                request.Finish,
                request.ClearFinish == true,
                request.Note?.Trim(),
                Clean(request.Tags),
                Clean(request.GlobalTags),
                request.Categories,
                request.PurchasePreferences,
                request.ClearPurchasePreferences == true),
            cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    internal static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> ApplyLineGroupActionAsync(
        Guid deckId, LineGroupRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.LineIds is not { Count: > 0 })
        {
            errors.Add(new FieldError("lineIds", FieldErrorCodes.Required, "Give one line or more."));
        }
        else if (request.LineIds.Count > DeckRules.MaxLines)
        {
            errors.Add(new FieldError("lineIds", FieldErrorCodes.TooLong, $"Give {DeckRules.MaxLines} lines or fewer."));
        }

        if (string.IsNullOrEmpty(request.Action))
        {
            errors.Add(new FieldError("action", FieldErrorCodes.Required, "The action is necessary."));
        }
        else
        {
            AddChoiceError(errors, "action", request.Action, LineGroupActions);
        }

        ValidateLineFields(errors, string.Empty, request.Zone, request.Finish, null);
        switch (request.Action)
        {
            case "move" when request.Zone is null:
                errors.Add(new FieldError("zone", FieldErrorCodes.Required, "The zone is necessary for the action move."));
                break;
            case "tag" or "untag":
                ValidateTags(errors, string.Empty, request.Tags, request.GlobalTags, request.Categories);
                if ((request.Tags?.Count ?? 0) + (request.GlobalTags?.Count ?? 0) + (request.Categories?.Count ?? 0) == 0)
                {
                    errors.Add(new FieldError("tags", FieldErrorCodes.Required, "Give one tag, global tag or category or more."));
                }

                break;
            case "printing" when request.ClearPrinting != true && request.Finish is null:
                errors.Add(new FieldError("finish", FieldErrorCodes.Required, "Give a finish, or set clearPrinting to true."));
                break;
            case "quantity" when request.QuantityDelta is null or 0:
                errors.Add(new FieldError("quantityDelta", FieldErrorCodes.Required, "Give a number of cards that is not 0."));
                break;
            case "quantity" when request.QuantityDelta is < -DeckRules.MaxQuantity or > DeckRules.MaxQuantity:
                errors.Add(new FieldError("quantityDelta", FieldErrorCodes.OutOfRange, $"The number of cards must be from -{DeckRules.MaxQuantity} to {DeckRules.MaxQuantity}."));
                break;
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var action = new LineGroupAction(
            request.Action!,
            request.Zone,
            Clean(request.Tags) ?? [],
            Clean(request.GlobalTags) ?? [],
            [.. (request.Categories ?? []).Distinct()],
            request.QuantityDelta ?? 0,
            request.Finish,
            request.ClearPrinting == true);
        var view = await decks.ApplyLineGroupActionAsync(user.GetAccountId(), deckId, request.LineIds!.ToHashSet(), action, request.ExpectedVersion, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    internal static async Task<Ok<DeckResponse>> DeleteLineAsync(Guid deckId, Guid lineId, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var view = await decks.DeleteLineAsync(user.GetAccountId(), deckId, lineId, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static readonly string[] LineGroupActions = ["move", "tag", "untag", "printing", "quantity", "remove"];
}
