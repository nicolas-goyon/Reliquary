using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Api;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Versions;

/// <summary>
/// The endpoints of the deck versions and the variants. See docs/decisions/0033-deck-versions-and-variants.md.
/// </summary>
internal static class VersionEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var decks = api.MapGroup("/decks/{deckId:guid}").WithTags("Deck versions").AddEndpointFilter<DeckExceptionFilter>();
        var owner = decks.MapGroup(string.Empty).RequireAuthorization(AuthPolicies.User);

        decks.MapGet("/versions", ListAsync)
            .WithName("ListDeckVersions")
            .WithSummary("Lists the versions of a deck, the most recent first, and tells whether the working list changed since the last version.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPost("/versions", CommitAsync)
            .WithName("CommitDeckVersion")
            .WithSummary("Commits the working list of a deck as a new version. A version never changes.")
            .RequireScope(Scopes.DecksWrite);

        decks.MapGet("/versions/{versionId:guid}", GetAsync)
            .WithName("GetDeckVersion")
            .WithSummary("Gets a version with its frozen lines and the data of its cards.")
            .RequireScope(Scopes.DecksRead);

        decks.MapGet("/versions/{versionId:guid}/changes", ChangesAsync)
            .WithName("GetDeckVersionChanges")
            .WithSummary("Compares a version with its first parent version. The changes of printing are in a separate list.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPost("/versions/{versionId:guid}/restore", RestoreAsync)
            .WithName("RestoreDeckVersion")
            .WithSummary("Copies a version into the working list and commits the result as a new version.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/versions/{versionId:guid}/revert", RevertAsync)
            .WithName("RevertDeckMerge")
            .WithSummary("Reverts a merge: the working list gets the content before the merge, in a new version.")
            .RequireScope(Scopes.DecksWrite);

        decks.MapGet("/variants", ListVariantsAsync)
            .WithName("ListDeckVariants")
            .WithSummary("Lists the variants of a deck. For a variant, it lists the variants of its main deck.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPost("/variants", CreateVariantAsync)
            .WithName("CreateDeckVariant")
            .WithSummary("Creates a variant of a deck from one of its versions. A variant is a deck: the deck endpoints change it.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/merge", MergeAsync)
            .WithName("MergeDeckVariant")
            .WithSummary("Merges a variant into its main deck, as a new version of the main deck with two parents.")
            .RequireScope(Scopes.DecksWrite);
    }

    private static async Task<Ok<DeckHistoryResponse>> ListAsync(Guid deckId, ClaimsPrincipal user, DeckVersionService versions, CancellationToken cancellationToken)
    {
        var (rows, changed, deck) = await versions.ListAsync(user.FindAccountId(), deckId, cancellationToken);
        return TypedResults.Ok(new DeckHistoryResponse([.. rows.Select(row => ToResponse(row, deck))], changed, deck.HeadVersionId, deck.AssembledVersionId));
    }

    private static async Task<Results<Created<DeckVersionResponse>, ProblemHttpResult>> CommitAsync(
        Guid deckId, VersionMessageRequest request, ClaimsPrincipal user, DeckVersionService versions, CancellationToken cancellationToken)
    {
        if (ValidateMessage(request.Message, required: true) is { } error)
        {
            return ApiProblems.Validation(error);
        }

        var version = await versions.CommitAsync(user.GetAccountId(), deckId, request.Message!, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/decks/{deckId}/versions/{version.Id}", ToResponse(version, null));
    }

    private static async Task<Ok<DeckVersionDetailResponse>> GetAsync(
        Guid deckId, Guid versionId, ClaimsPrincipal user, DeckVersionService versions, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var (version, lines, deck) = await versions.GetAsync(user.FindAccountId(), deckId, versionId, cancellationToken);
        var (cards, printings) = await DeckEndpoints.CardDataAsync(catalog, lines.Select(line => line.OracleId), lines.Select(line => line.PrintingId), cancellationToken);
        return TypedResults.Ok(new DeckVersionDetailResponse(
            ToResponse(version, deck),
            [.. lines.Select(line => new DeckVersionLineResponse(line.OracleId, line.PrintingId, line.Finish, line.Quantity, line.Zone, line.Note, line.Categories, line.Tags, line.GlobalTags))],
            cards,
            printings));
    }

    private static async Task<Ok<ListChangesResponse>> ChangesAsync(
        Guid deckId, Guid versionId, ClaimsPrincipal user, DeckVersionService versions, ICardCatalog catalog, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ToResponseAsync(await versions.ChangesAsync(user.FindAccountId(), deckId, versionId, cancellationToken), catalog, cancellationToken));

    private static async Task<Results<Created<DeckVersionResponse>, ProblemHttpResult>> RestoreAsync(
        Guid deckId, Guid versionId, VersionMessageRequest request, ClaimsPrincipal user, DeckVersionService versions, CancellationToken cancellationToken)
    {
        if (ValidateMessage(request.Message, required: false) is { } error)
        {
            return ApiProblems.Validation(error);
        }

        var version = await versions.RestoreAsync(user.GetAccountId(), deckId, versionId, request.Message, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/decks/{deckId}/versions/{version.Id}", ToResponse(version, null));
    }

    private static async Task<Results<Created<DeckVersionResponse>, ProblemHttpResult>> RevertAsync(
        Guid deckId, Guid versionId, VersionMessageRequest request, ClaimsPrincipal user, DeckVersionService versions, CancellationToken cancellationToken)
    {
        if (ValidateMessage(request.Message, required: false) is { } error)
        {
            return ApiProblems.Validation(error);
        }

        var version = await versions.RevertAsync(user.GetAccountId(), deckId, versionId, request.Message, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/decks/{deckId}/versions/{version.Id}", ToResponse(version, null));
    }

    private static async Task<Ok<IReadOnlyList<DeckVariantResponse>>> ListVariantsAsync(Guid deckId, ClaimsPrincipal user, DeckVersionService versions, CancellationToken cancellationToken)
    {
        var variants = await versions.ListVariantsAsync(user.FindAccountId(), deckId, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<DeckVariantResponse>>([.. variants.Select(ToVariant)]);
    }

    private static async Task<Results<Created<DeckVariantResponse>, ProblemHttpResult>> CreateVariantAsync(
        Guid deckId, CreateVariantRequest request, ClaimsPrincipal user, DeckVersionService versions, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return ApiProblems.Validation(new FieldError("name", FieldErrorCodes.Required, "The name of the variant is necessary."));
        }

        if (name.Length > DeckRules.MaxNameLength)
        {
            return ApiProblems.Validation(new FieldError("name", FieldErrorCodes.TooLong, $"The name must have {DeckRules.MaxNameLength} characters or fewer."));
        }

        var variant = await versions.CreateVariantAsync(user.GetAccountId(), deckId, name, request.BaseVersionId, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/decks/{variant.Id}", ToVariant(variant));
    }

    private static async Task<Results<Created<DeckVersionResponse>, ProblemHttpResult>> MergeAsync(
        Guid deckId, VersionMessageRequest request, ClaimsPrincipal user, DeckVersionService versions, CancellationToken cancellationToken)
    {
        if (ValidateMessage(request.Message, required: false) is { } error)
        {
            return ApiProblems.Validation(error);
        }

        var version = await versions.MergeAsync(user.GetAccountId(), deckId, request.Message, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/decks/{version.DeckId}/versions/{version.Id}", ToResponse(version, null));
    }

    internal static async Task<ListChangesResponse> ToResponseAsync(ListComparison comparison, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var oracleIds = comparison.Quantities.Select(change => change.OracleId)
            .Concat(comparison.Printings.Select(change => change.OracleId))
            .Concat(comparison.Notes.Select(change => change.OracleId));
        var printingIds = comparison.Printings.SelectMany(change => change.Before.Concat(change.After)).Select(copies => copies.PrintingId);
        var (cards, printings) = await DeckEndpoints.CardDataAsync(catalog, oracleIds, printingIds, cancellationToken);
        return new ListChangesResponse(
            [.. comparison.Quantities.Select(change => new QuantityChangeResponse(change.OracleId, change.Zone, change.Before, change.After))],
            [.. comparison.Printings.Select(change => new PrintingChangeResponse(
                change.OracleId,
                change.Zone,
                [.. change.Before.Select(copies => new PrintingCopiesResponse(copies.PrintingId, copies.Finish, copies.Quantity))],
                [.. change.After.Select(copies => new PrintingCopiesResponse(copies.PrintingId, copies.Finish, copies.Quantity))]))],
            [.. comparison.Notes.Select(change => new NoteChangeResponse(change.OracleId, change.Zone, change.Before, change.After))],
            cards,
            printings);
    }

    private static FieldError? ValidateMessage(string? message, bool required)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return required ? new FieldError("message", FieldErrorCodes.Required, "The message of the version is necessary.") : null;
        }

        return message.Trim().Length > DeckVersionService.MaxMessageLength
            ? new FieldError("message", FieldErrorCodes.TooLong, $"The message must have {DeckVersionService.MaxMessageLength} characters or fewer.")
            : null;
    }

    private static DeckVersionResponse ToResponse(VersionRow row, DeckRow? deck) =>
        new(row.Id, row.Number, row.Kind, row.Message, row.ParentIds, DeckEndpoints.Utc(row.CreatedAt), row.CardCount, deck?.AssembledVersionId == row.Id);

    private static DeckVariantResponse ToVariant(DeckRow row) =>
        new(row.Id, row.Name, row.BaseVersionId, row.HeadVersionId, DeckEndpoints.Utc(row.UpdatedAt));
}
