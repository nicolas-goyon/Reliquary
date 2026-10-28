using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Collection.Data;

namespace Reliquary.Collection.Api;

/// <summary>
/// The endpoints of the locations.
/// </summary>
internal static class LocationEndpoints
{
    public static void Map(RouteGroupBuilder collection)
    {
        collection.MapGet("/locations", ListAsync)
            .WithName("ListLocations")
            .WithSummary("Lists the locations of the user, with their sections and their number of cards.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapPost("/locations", CreateAsync)
            .WithName("CreateLocation")
            .WithSummary("Creates a location or a section. Kinds: rack, binder, box or cube. A section has the kind of its parent location, and a section cannot have a section.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapGet("/locations/{locationId:guid}", GetAsync)
            .WithName("GetLocation")
            .WithSummary("Gets a location with its sections.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapPatch("/locations/{locationId:guid}", UpdateAsync)
            .WithName("UpdateLocation")
            .WithSummary("Changes the name, the kind, the pickable flag or the position of a location. Error deck_location for the location of an assembled deck.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapPost("/locations/{locationId:guid}/lock", LockAsync)
            .WithName("LockLocation")
            .WithSummary("Locks a location. A locked location and its sections refuse each change of their content with the error location_locked.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapPost("/locations/{locationId:guid}/unlock", UnlockAsync)
            .WithName("UnlockLocation")
            .WithSummary("Unlocks a location.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapDelete("/locations/{locationId:guid}", DeleteAsync)
            .WithName("DeleteLocation")
            .WithSummary("Deletes a location and its sections. Errors: location_not_empty when a card is in the location, location_locked, deck_location.")
            .RequireScope(Scopes.CollectionWrite);
    }

    private static async Task<Ok<IReadOnlyList<LocationResponse>>> ListAsync(ClaimsPrincipal user, LocationService locations, CancellationToken cancellationToken)
    {
        var rows = await locations.ListAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Ok(Tree(rows));
    }

    private static async Task<Results<Created<LocationResponse>, ProblemHttpResult>> CreateAsync(
        CreateLocationRequest request, ClaimsPrincipal user, LocationService locations, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors.Add(new FieldError("name", FieldErrorCodes.Required, "The name of the location is necessary."));
        }

        CheckFields(errors, name, request.Kind);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var location = await locations.CreateAsync(
            user.GetAccountId(),
            new NewLocation(name!, request.Kind, request.ParentId, request.Pickable ?? true, request.Locked ?? false, request.Position ?? 0),
            cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/collection/locations/{location.Id}", await ResponseAsync(user, locations, location.Id, cancellationToken));
    }

    private static async Task<Ok<LocationResponse>> GetAsync(Guid locationId, ClaimsPrincipal user, LocationService locations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ResponseAsync(user, locations, locationId, cancellationToken));

    private static async Task<Results<Ok<LocationResponse>, ProblemHttpResult>> UpdateAsync(
        Guid locationId, UpdateLocationRequest request, ClaimsPrincipal user, LocationService locations, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var name = request.Name?.Trim();
        if (request.Name is not null && string.IsNullOrEmpty(name))
        {
            errors.Add(new FieldError("name", FieldErrorCodes.Required, "The name of the location is necessary."));
        }

        CheckFields(errors, name, request.Kind);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        await locations.UpdateAsync(user.GetAccountId(), locationId, new LocationChanges(name, request.Kind, request.Pickable, request.Position), cancellationToken);
        return TypedResults.Ok(await ResponseAsync(user, locations, locationId, cancellationToken));
    }

    private static async Task<Ok<LocationResponse>> LockAsync(Guid locationId, ClaimsPrincipal user, LocationService locations, CancellationToken cancellationToken)
    {
        await locations.SetLockedAsync(user.GetAccountId(), locationId, true, cancellationToken);
        return TypedResults.Ok(await ResponseAsync(user, locations, locationId, cancellationToken));
    }

    private static async Task<Ok<LocationResponse>> UnlockAsync(Guid locationId, ClaimsPrincipal user, LocationService locations, CancellationToken cancellationToken)
    {
        await locations.SetLockedAsync(user.GetAccountId(), locationId, false, cancellationToken);
        return TypedResults.Ok(await ResponseAsync(user, locations, locationId, cancellationToken));
    }

    private static async Task<NoContent> DeleteAsync(Guid locationId, ClaimsPrincipal user, LocationService locations, CancellationToken cancellationToken)
    {
        await locations.DeleteAsync(user.GetAccountId(), locationId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static void CheckFields(List<FieldError> errors, string? name, string? kind)
    {
        if (name is { Length: > CollectionRules.MaxNameLength })
        {
            errors.Add(new FieldError("name", FieldErrorCodes.TooLong, $"The name must have {CollectionRules.MaxNameLength} characters or fewer."));
        }

        if (kind is not null && !CollectionRules.UserKinds.Contains(kind))
        {
            errors.Add(new FieldError("kind", FieldErrorCodes.NotAllowed, $"The kind must be one of these kinds: {string.Join(", ", CollectionRules.UserKinds)}."));
        }
    }

    /// <summary>
    /// Makes the response of one location, with its sections.
    /// </summary>
    private static async Task<LocationResponse> ResponseAsync(ClaimsPrincipal user, LocationService locations, Guid locationId, CancellationToken cancellationToken)
    {
        var rows = await locations.ListAsync(user.GetAccountId(), cancellationToken);
        var row = rows.FirstOrDefault(location => location.Id == locationId) ?? throw CollectionException.LocationNotFound();
        return ToResponse(row, rows);
    }

    /// <summary>
    /// Makes the tree of the locations: the locations with no parent, each with its sections.
    /// </summary>
    internal static IReadOnlyList<LocationResponse> Tree(IReadOnlyList<LocationSummaryRow> rows) =>
        [.. rows.Where(row => row.ParentId is null).Select(row => ToResponse(row, rows))];

    private static LocationResponse ToResponse(LocationSummaryRow row, IReadOnlyList<LocationSummaryRow> rows)
    {
        var sections = row.ParentId is null
            ? rows.Where(candidate => candidate.ParentId == row.Id).Select(section => ToResponse(section, rows)).ToList()
            : [];
        return new LocationResponse(
            row.Id,
            row.ParentId,
            row.Name,
            row.Kind,
            row.Locked,
            row.Pickable,
            row.DeckId,
            row.Position,
            row.CardCount,
            row.CardCount + sections.Sum(section => section.CardCount),
            Utc(row.CreatedAt),
            Utc(row.UpdatedAt),
            sections);
    }

    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
