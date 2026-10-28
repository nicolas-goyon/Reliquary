using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Collection.Data;

namespace Reliquary.Collection.Api;

/// <summary>A new inventory of a location.</summary>
/// <param name="Entries">The counted content of the location. An empty list tells that the location is empty.</param>
public sealed record StartInventoryRequest(IReadOnlyList<CardRequest>? Entries);

/// <summary>The answer for one difference of an inventory.</summary>
/// <param name="Key">The key of the difference.</param>
/// <param name="Kind">The origin of the added copies, or the destination of the removed copies.</param>
/// <param name="LocationId">The location when the kind is <c>location</c>.</param>
public sealed record InventoryAnswerRequest(string? Key, string? Kind, Guid? LocationId);

/// <summary>The answers of the owner for the differences of an inventory.</summary>
/// <param name="Answers">The answer for each difference.</param>
/// <param name="Origin">The origin of the added copies of all the differences with no answer.</param>
/// <param name="Destination">The destination of the removed copies of all the differences with no answer.</param>
public sealed record ApplyInventoryRequest(IReadOnlyList<InventoryAnswerRequest>? Answers, EndpointRequest? Origin, EndpointRequest? Destination);

/// <summary>Copies of one card in an inventory or in a difference. A disabled advanced field is not in the response.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The Oracle name.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="SetCode">The set code of the printing, or <see langword="null"/>.</param>
/// <param name="Language">The language code.</param>
/// <param name="Quantity">The number of copies.</param>
/// <param name="Finish">The finish.</param>
/// <param name="Condition">The condition.</param>
/// <param name="PurchasePrice">The purchase price.</param>
/// <param name="PurchaseCurrency">The currency of the purchase price.</param>
/// <param name="AcquiredOn">The date when the owner got the copies.</param>
/// <param name="Note">The note.</param>
public sealed record InventoryEntryResponse(
    Guid OracleId,
    string Name,
    Guid? PrintingId,
    string? SetCode,
    string Language,
    int Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Finish,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Condition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? PurchasePrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PurchaseCurrency,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? AcquiredOn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note);

/// <summary>A difference between the counted content and the location.</summary>
/// <param name="Key">The key of the difference, for the answers.</param>
/// <param name="Card">The card fields. The quantity is the change.</param>
/// <param name="Expected">The number of copies in the location now.</param>
/// <param name="Counted">The number of copies that the owner counted.</param>
public sealed record InventoryDifferenceResponse(string Key, InventoryEntryResponse Card, int Expected, int Counted);

/// <summary>An inventory of a location.</summary>
/// <param name="Id">The ID of the inventory.</param>
/// <param name="LocationId">The location.</param>
/// <param name="Status"><c>open</c> while the owner answers the differences, then <c>applied</c>.</param>
/// <param name="CreatedAt">The date of the count.</param>
/// <param name="AppliedAt">The date of the inventory: the date when the owner applied it.</param>
/// <param name="CardCount">The number of counted cards.</param>
/// <param name="Entries">The counted content. After the application, the content of the location at the date of the inventory.</param>
/// <param name="Differences">The differences with the location, while the inventory is open.</param>
public sealed record InventoryResponse(
    Guid Id,
    Guid LocationId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AppliedAt,
    long CardCount,
    IReadOnlyList<InventoryEntryResponse> Entries,
    IReadOnlyList<InventoryDifferenceResponse> Differences);

/// <summary>An inventory in the history of a location.</summary>
/// <param name="Id">The ID of the inventory.</param>
/// <param name="Status"><c>open</c> or <c>applied</c>.</param>
/// <param name="CreatedAt">The date of the count.</param>
/// <param name="AppliedAt">The date of the inventory, or <see langword="null"/> for an open inventory.</param>
/// <param name="CardCount">The number of counted cards.</param>
public sealed record InventorySummaryResponse(Guid Id, string Status, DateTimeOffset CreatedAt, DateTimeOffset? AppliedAt, long CardCount);

/// <summary>
/// The endpoints of the inventories.
/// </summary>
internal static class InventoryEndpoints
{
    public static void Map(RouteGroupBuilder collection)
    {
        collection.MapPost("/locations/{locationId:guid}/inventories", StartAsync)
            .WithName("StartInventory")
            .WithSummary("Starts an inventory of a location with the counted content, and gives the differences with the location. An open inventory of the location stops.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapGet("/locations/{locationId:guid}/inventories", ListAsync)
            .WithName("ListInventories")
            .WithSummary("Lists the inventories of a location, the most recent first.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapGet("/inventories/{inventoryId:guid}", GetAsync)
            .WithName("GetInventory")
            .WithSummary("Gets an inventory: the counted content and, while it is open, the differences with the location.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapPost("/inventories/{inventoryId:guid}/apply", ApplyAsync)
            .WithName("ApplyInventory")
            .WithSummary("Applies an inventory with the origin or the destination of each difference. The API writes the movements and keeps the content. "
                + "Errors: location_locked, not_enough_cards and inventory_applied.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapDelete("/inventories/{inventoryId:guid}", DeleteAsync)
            .WithName("DeleteInventory")
            .WithSummary("Stops an open inventory with no change. Error inventory_applied for an applied inventory.")
            .RequireScope(Scopes.CollectionWrite);
    }

    private static async Task<Results<Created<InventoryResponse>, ProblemHttpResult>> StartAsync(
        Guid locationId,
        StartInventoryRequest request,
        ClaimsPrincipal user,
        InventoryService inventories,
        SettingsService settings,
        CardResolver resolver,
        CancellationToken cancellationToken)
    {
        if (request.Entries is null)
        {
            return ApiProblems.Validation(new FieldError("entries", FieldErrorCodes.Required, "Give the counted cards. An empty list tells that the location is empty."));
        }

        var ownerId = user.GetAccountId();
        var fields = await settings.GetAsync(ownerId, cancellationToken);
        var counted = request.Entries.Count == 0 ? [] : await resolver.ResolveAsync(request.Entries, "entries", fields, cancellationToken);
        var id = await inventories.StartAsync(ownerId, locationId, counted, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/collection/inventories/{id}", ToResponse(await inventories.GetAsync(ownerId, id, cancellationToken), fields));
    }

    private static async Task<Ok<IReadOnlyList<InventorySummaryResponse>>> ListAsync(Guid locationId, ClaimsPrincipal user, InventoryService inventories, CancellationToken cancellationToken)
    {
        var rows = await inventories.ListAsync(user.GetAccountId(), locationId, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<InventorySummaryResponse>>([.. rows.Select(row => new InventorySummaryResponse(
            row.Id, row.Status, LocationEndpoints.Utc(row.CreatedAt), row.AppliedAt is { } applied ? LocationEndpoints.Utc(applied) : null, row.CardCount))]);
    }

    private static async Task<Ok<InventoryResponse>> GetAsync(Guid inventoryId, ClaimsPrincipal user, InventoryService inventories, SettingsService settings, CancellationToken cancellationToken)
    {
        var ownerId = user.GetAccountId();
        return TypedResults.Ok(ToResponse(await inventories.GetAsync(ownerId, inventoryId, cancellationToken), await settings.GetAsync(ownerId, cancellationToken)));
    }

    private static async Task<Results<Ok<InventoryResponse>, ProblemHttpResult>> ApplyAsync(
        Guid inventoryId, ApplyInventoryRequest request, ClaimsPrincipal user, InventoryService inventories, SettingsService settings, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var byKey = new Dictionary<string, MovementEndpoint>(StringComparer.Ordinal);
        var answers = request.Answers ?? [];
        for (var index = 0; index < answers.Count; index++)
        {
            var answer = answers[index];
            if (string.IsNullOrEmpty(answer.Key))
            {
                errors.Add(new FieldError($"answers[{index}].key", FieldErrorCodes.Required, "Give the key of the difference."));
                continue;
            }

            if (ReadEndpoint(errors, $"answers[{index}]", answer.Kind, answer.LocationId) is { } endpoint)
            {
                byKey[answer.Key] = endpoint;
            }
        }

        var origin = request.Origin is null ? null : ReadEndpoint(errors, "origin", request.Origin.Kind, request.Origin.LocationId);
        var destination = request.Destination is null ? null : ReadEndpoint(errors, "destination", request.Destination.Kind, request.Destination.LocationId);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var ownerId = user.GetAccountId();
        await inventories.ApplyAsync(ownerId, inventoryId, new InventoryAnswers(byKey, origin, destination), cancellationToken);
        return TypedResults.Ok(ToResponse(await inventories.GetAsync(ownerId, inventoryId, cancellationToken), await settings.GetAsync(ownerId, cancellationToken)));
    }

    private static async Task<NoContent> DeleteAsync(Guid inventoryId, ClaimsPrincipal user, InventoryService inventories, CancellationToken cancellationToken)
    {
        await inventories.DeleteAsync(user.GetAccountId(), inventoryId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static MovementEndpoint? ReadEndpoint(List<FieldError> errors, string field, string? kind, Guid? locationId)
    {
        var kinds = CollectionRules.Origins.Union(CollectionRules.Destinations).ToList();
        if (kind is null || !kinds.Contains(kind))
        {
            errors.Add(new FieldError($"{field}.kind", FieldErrorCodes.NotAllowed, $"The kind must be one of these values: {string.Join(", ", kinds)}."));
            return null;
        }

        if (kind == CollectionRules.Endpoints.Location && locationId is null)
        {
            errors.Add(new FieldError($"{field}.locationId", FieldErrorCodes.Required, "Give the location."));
            return null;
        }

        return new MovementEndpoint(kind, kind == CollectionRules.Endpoints.Location ? locationId : null);
    }

    private static InventoryResponse ToResponse(InventoryView view, AdvancedFields settings)
    {
        var inventory = view.Inventory;
        return new InventoryResponse(
            inventory.Id,
            inventory.LocationId,
            inventory.Status,
            LocationEndpoints.Utc(inventory.CreatedAt),
            inventory.AppliedAt is { } applied ? LocationEndpoints.Utc(applied) : null,
            inventory.CardCount,
            [.. view.Entries.Select(entry => ToEntry(entry.Line, entry.Quantity, settings))],
            [.. view.Differences.Select(difference => new InventoryDifferenceResponse(
                difference.Key, ToEntry(difference.Line, difference.Delta, settings), difference.Expected, difference.Counted))]);
    }

    private static InventoryEntryResponse ToEntry(CardLine line, int quantity, AdvancedFields settings)
    {
        var fields = settings.Output(line.Fields);
        return new InventoryEntryResponse(
            fields.OracleId,
            line.CardName,
            fields.PrintingId,
            line.SetCode,
            fields.Language,
            quantity,
            fields.Finish,
            fields.Condition,
            fields.PurchasePrice,
            fields.PurchasePrice is null ? null : fields.PurchaseCurrency,
            fields.AcquiredOn,
            fields.Note);
    }
}
