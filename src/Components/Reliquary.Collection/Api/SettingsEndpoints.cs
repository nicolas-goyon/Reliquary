using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Collection.Api;

/// <summary>
/// The endpoints of the collection settings: the advanced fields.
/// </summary>
internal static class SettingsEndpoints
{
    public static void Map(RouteGroupBuilder collection)
    {
        collection.MapGet("/settings", GetAsync)
            .WithName("GetCollectionSettings")
            .WithSummary("Gets the advanced fields that the user enabled.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapPut("/settings", SetAsync)
            .WithName("SetCollectionSettings")
            .WithSummary("Sets the advanced fields: finish, condition, purchasePrice, acquiredOn and note. The API ignores a disabled field in a request and never returns it. The stored values stay.")
            .RequireScope(Scopes.CollectionWrite);
    }

    private static async Task<Ok<CollectionSettingsResponse>> GetAsync(ClaimsPrincipal user, SettingsService settings, CancellationToken cancellationToken)
    {
        var fields = await settings.GetAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Ok(ToResponse(fields));
    }

    private static async Task<Results<Ok<CollectionSettingsResponse>, ProblemHttpResult>> SetAsync(
        CollectionSettingsRequest request, ClaimsPrincipal user, SettingsService settings, CancellationToken cancellationToken)
    {
        if (request.AdvancedFields is null)
        {
            return ApiProblems.Validation(new FieldError("advancedFields", FieldErrorCodes.Required, "Give the list of the enabled advanced fields. An empty list disables all the fields."));
        }

        var errors = request.AdvancedFields
            .Select((field, index) => (field, index))
            .Where(item => !CollectionRules.AdvancedFields.Contains(item.field))
            .Select(item => new FieldError($"advancedFields[{item.index}]", FieldErrorCodes.NotAllowed, $"The advanced field must be one of these fields: {string.Join(", ", CollectionRules.AdvancedFields)}."))
            .ToList();
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var fields = await settings.SetAsync(user.GetAccountId(), request.AdvancedFields, cancellationToken);
        return TypedResults.Ok(ToResponse(fields));
    }

    private static CollectionSettingsResponse ToResponse(AdvancedFields fields) =>
        new([.. CollectionRules.AdvancedFields.Where(fields.Enabled.Contains)]);
}
