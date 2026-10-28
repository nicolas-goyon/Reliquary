using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Collection.Api;

/// <summary>A new storage rule.</summary>
/// <param name="LocationId">The location that the rule suggests.</param>
/// <param name="Query">A card search in the syntax of the card search, for example <c>c:u</c>.</param>
/// <param name="Priority">The priority. The rule with the lowest number applies first. The default is 0.</param>
public sealed record StorageRuleRequest(Guid? LocationId, string? Query, int? Priority);

/// <summary>A storage rule.</summary>
/// <param name="Id">The ID of the rule.</param>
/// <param name="LocationId">The location that the rule suggests.</param>
/// <param name="LocationName">The name of the location. For a section, the name of its parent location.</param>
/// <param name="SectionName">The name of the section, or <see langword="null"/>.</param>
/// <param name="Query">The card search.</param>
/// <param name="Priority">The priority. The rule with the lowest number applies first.</param>
public sealed record StorageRuleResponse(Guid Id, Guid LocationId, string LocationName, string? SectionName, string Query, int Priority);

/// <summary>Whether the owner has a token of an assembled deck.</summary>
/// <param name="Owned">Whether the owner has the token.</param>
public sealed record DeckTokenRequest(bool? Owned);

/// <summary>A token of an assembled deck.</summary>
/// <param name="OracleId">The Oracle card of the token.</param>
/// <param name="Name">The name of the token.</param>
/// <param name="Owned">Whether the owner has the token.</param>
public sealed record DeckTokenResponse(Guid OracleId, string Name, bool Owned);

/// <summary>
/// The endpoints of the storage rules and of the tokens of the assembled decks.
/// </summary>
internal static class StorageRuleEndpoints
{
    public static void Map(RouteGroupBuilder collection)
    {
        collection.MapGet("/storage-rules", ListAsync)
            .WithName("ListStorageRules")
            .WithSummary("Lists the storage rules by priority. The rule with the lowest priority number applies first.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapPost("/storage-rules", CreateAsync)
            .WithName("CreateStorageRule")
            .WithSummary("Creates a storage rule: a location, a card search in the syntax of the card search, and a priority.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapPatch("/storage-rules/{ruleId:guid}", UpdateAsync)
            .WithName("UpdateStorageRule")
            .WithSummary("Changes the location, the card search or the priority of a storage rule.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapDelete("/storage-rules/{ruleId:guid}", DeleteAsync)
            .WithName("DeleteStorageRule")
            .WithSummary("Deletes a storage rule.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapGet("/storage-rules/suggestion", SuggestAsync)
            .WithName("SuggestStorageLocation")
            .WithSummary("Gives the first storage rule, by priority, whose card search matches the card oracleId. No content when no rule matches.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapGet("/locations/{locationId:guid}/tokens", ListTokensAsync)
            .WithName("ListDeckTokens")
            .WithSummary("Lists the tokens of an assembled deck location, and whether the user has each token.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapPut("/locations/{locationId:guid}/tokens/{oracleId:guid}", SetTokenAsync)
            .WithName("SetDeckToken")
            .WithSummary("Marks a token of an assembled deck location as owned or not. A token that is not in the list joins the list.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapDelete("/locations/{locationId:guid}/tokens/{oracleId:guid}", DeleteTokenAsync)
            .WithName("DeleteDeckToken")
            .WithSummary("Removes a token from the list of an assembled deck location.")
            .RequireScope(Scopes.CollectionWrite);
    }

    private static async Task<Ok<IReadOnlyList<StorageRuleResponse>>> ListAsync(ClaimsPrincipal user, StorageRuleService rules, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<StorageRuleResponse>>([.. (await rules.ListAsync(user.GetAccountId(), cancellationToken)).Select(ToResponse)]);

    private static async Task<Results<Created<StorageRuleResponse>, ProblemHttpResult>> CreateAsync(
        StorageRuleRequest request, ClaimsPrincipal user, StorageRuleService rules, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.LocationId is null)
        {
            errors.Add(new FieldError("locationId", FieldErrorCodes.Required, "The location is necessary."));
        }

        var query = request.Query?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            errors.Add(new FieldError("query", FieldErrorCodes.Required, "The card search is necessary."));
        }

        CheckFields(errors, query);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var rule = await rules.CreateAsync(user.GetAccountId(), request.LocationId!.Value, query!, request.Priority ?? 0, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/collection/storage-rules/{rule.Id}", ToResponse(rule));
    }

    private static async Task<Results<Ok<StorageRuleResponse>, ProblemHttpResult>> UpdateAsync(
        Guid ruleId, StorageRuleRequest request, ClaimsPrincipal user, StorageRuleService rules, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var query = request.Query?.Trim();
        if (request.Query is not null && string.IsNullOrEmpty(query))
        {
            errors.Add(new FieldError("query", FieldErrorCodes.Required, "The card search is necessary."));
        }

        CheckFields(errors, query);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var rule = await rules.UpdateAsync(user.GetAccountId(), ruleId, request.LocationId, query, request.Priority, cancellationToken);
        return TypedResults.Ok(ToResponse(rule));
    }

    private static async Task<NoContent> DeleteAsync(Guid ruleId, ClaimsPrincipal user, StorageRuleService rules, CancellationToken cancellationToken)
    {
        await rules.DeleteAsync(user.GetAccountId(), ruleId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<StorageRuleResponse>, NoContent, ProblemHttpResult>> SuggestAsync(
        Guid? oracleId, ClaimsPrincipal user, StorageRuleService rules, CancellationToken cancellationToken)
    {
        if (oracleId is null)
        {
            return ApiProblems.Validation(new FieldError("oracleId", FieldErrorCodes.Required, "The Oracle ID of the card is necessary."));
        }

        var rule = await rules.SuggestAsync(user.GetAccountId(), oracleId.Value, cancellationToken);
        return rule is null ? TypedResults.NoContent() : TypedResults.Ok(ToResponse(rule));
    }

    private static async Task<Ok<IReadOnlyList<DeckTokenResponse>>> ListTokensAsync(Guid locationId, ClaimsPrincipal user, DeckTokenService tokens, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<DeckTokenResponse>>([.. (await tokens.ListAsync(user.GetAccountId(), locationId, cancellationToken)).Select(token => new DeckTokenResponse(token.OracleId, token.Name, token.Owned))]);

    private static async Task<Results<Ok<IReadOnlyList<DeckTokenResponse>>, ProblemHttpResult>> SetTokenAsync(
        Guid locationId, Guid oracleId, DeckTokenRequest request, ClaimsPrincipal user, DeckTokenService tokens, CancellationToken cancellationToken)
    {
        if (request.Owned is null)
        {
            return ApiProblems.Validation(new FieldError("owned", FieldErrorCodes.Required, "Tell whether you have the token."));
        }

        await tokens.SetOwnedAsync(user.GetAccountId(), locationId, oracleId, request.Owned.Value, cancellationToken);
        return await ListTokensAsync(locationId, user, tokens, cancellationToken);
    }

    private static async Task<NoContent> DeleteTokenAsync(Guid locationId, Guid oracleId, ClaimsPrincipal user, DeckTokenService tokens, CancellationToken cancellationToken)
    {
        await tokens.DeleteAsync(user.GetAccountId(), locationId, oracleId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static void CheckFields(List<FieldError> errors, string? query)
    {
        if (query is { Length: > StorageRuleService.MaxQueryLength })
        {
            errors.Add(new FieldError("query", FieldErrorCodes.TooLong, $"The card search must have {StorageRuleService.MaxQueryLength} characters or fewer."));
        }
    }

    private static StorageRuleResponse ToResponse(StorageRuleRow rule) =>
        new(rule.Id, rule.LocationId, rule.LocationName, rule.SectionName, rule.Query, rule.Priority);
}
