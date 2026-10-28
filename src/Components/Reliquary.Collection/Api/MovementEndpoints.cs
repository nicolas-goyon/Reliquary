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

/// <summary>The origin or the destination of a movement.</summary>
/// <param name="Kind">The kind: <c>location</c>, <c>purchase</c>, <c>sale</c>, <c>gift</c>, <c>trade</c> or <c>nowhere</c>.</param>
/// <param name="LocationId">The location when the kind is <c>location</c>.</param>
public sealed record EndpointRequest(string? Kind, Guid? LocationId);

/// <summary>A card of a movement.</summary>
/// <param name="EntryId">The entry of the origin location, when the origin is a location.</param>
/// <param name="Quantity">The number of copies. The default is 1, or all the copies of the entry when the origin is a location.</param>
/// <param name="OracleId">The Oracle card, when the origin is not a location.</param>
/// <param name="Name">A name of the card, when the origin is not a location.</param>
/// <param name="PrintingId">The printing, when the origin is not a location.</param>
/// <param name="Language">The language code, when the origin is not a location.</param>
/// <param name="Finish">The finish. An advanced field.</param>
/// <param name="Condition">The condition. An advanced field.</param>
/// <param name="PurchasePrice">The purchase price of one copy. An advanced field.</param>
/// <param name="PurchaseCurrency">The currency of the purchase price.</param>
/// <param name="AcquiredOn">The date when the owner got the copies. An advanced field.</param>
/// <param name="Note">A note. An advanced field.</param>
public sealed record MovementCardRequest(
    Guid? EntryId,
    int? Quantity,
    Guid? OracleId = null,
    string? Name = null,
    Guid? PrintingId = null,
    string? Language = null,
    string? Finish = null,
    string? Condition = null,
    decimal? PurchasePrice = null,
    string? PurchaseCurrency = null,
    DateOnly? AcquiredOn = null,
    string? Note = null)
{
    /// <summary>Gets the card as a new card of a location.</summary>
    public CardRequest ToCard() => new(OracleId, Name, PrintingId, Language, Quantity, Finish, Condition, PurchasePrice, PurchaseCurrency, AcquiredOn, Note);
}

/// <summary>A group of movements with one origin and one destination.</summary>
/// <param name="Origin">The origin. A location, or <c>purchase</c>, <c>gift</c>, <c>trade</c> or <c>nowhere</c>.</param>
/// <param name="Destination">The destination. A location, or <c>sale</c>, <c>gift</c>, <c>trade</c> or <c>nowhere</c>.</param>
/// <param name="Cards">The cards. With an origin location, give the entries. With another origin, give the cards.</param>
public sealed record MovementRequest(EndpointRequest? Origin, EndpointRequest? Destination, IReadOnlyList<MovementCardRequest>? Cards);

/// <summary>The origin or the destination of a movement in the history.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="LocationId">The location, or <see langword="null"/>.</param>
/// <param name="LocationName">The name of the location, or <see langword="null"/> when the location does not exist any more.</param>
public sealed record EndpointResponse(string Kind, Guid? LocationId, string? LocationName);

/// <summary>A movement of the history. A disabled advanced field is not in the response.</summary>
/// <param name="Id">The ID of the movement.</param>
/// <param name="BatchId">The batch ID. The movements of one request have the same batch ID.</param>
/// <param name="Kind">The kind: <c>add</c>, <c>remove</c> or <c>move</c>.</param>
/// <param name="Origin">The origin.</param>
/// <param name="Destination">The destination.</param>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The Oracle name.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="Language">The language code.</param>
/// <param name="Quantity">The number of copies.</param>
/// <param name="InventoryId">The inventory that wrote the movement, or <see langword="null"/>.</param>
/// <param name="CreatedAt">The date of the movement.</param>
/// <param name="Finish">The finish.</param>
/// <param name="Condition">The condition.</param>
/// <param name="PurchasePrice">The purchase price.</param>
/// <param name="PurchaseCurrency">The currency of the purchase price.</param>
/// <param name="AcquiredOn">The date when the owner got the copies.</param>
/// <param name="Note">The note.</param>
public sealed record MovementResponse(
    Guid Id,
    Guid BatchId,
    string Kind,
    EndpointResponse Origin,
    EndpointResponse Destination,
    Guid OracleId,
    string Name,
    Guid? PrintingId,
    string Language,
    int Quantity,
    Guid? InventoryId,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Finish,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Condition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? PurchasePrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PurchaseCurrency,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? AcquiredOn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note);

/// <summary>The movements of one request.</summary>
/// <param name="BatchId">The batch ID of the movements.</param>
/// <param name="Movements">The movements.</param>
public sealed record MovementBatchResponse(Guid BatchId, IReadOnlyList<MovementResponse> Movements);

/// <summary>
/// The endpoints of the movements: add, remove and move cards, and the history.
/// </summary>
internal static class MovementEndpoints
{
    public static void Map(RouteGroupBuilder collection)
    {
        collection.MapPost("/movements", MoveAsync)
            .WithName("CreateMovements")
            .WithSummary("Adds, removes or moves cards in one request. All the movements get one batch ID. A move keeps the card fields. "
                + "Errors: location_locked, deck_location and not_enough_cards.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapGet("/movements", ListAsync)
            .WithName("ListMovements")
            .WithSummary("Lists the movements, the most recent first. Filters: locationId (a location and its sections) and batchId.")
            .RequireScope(Scopes.CollectionRead);
    }

    private static async Task<Results<Ok<MovementBatchResponse>, ProblemHttpResult>> MoveAsync(
        MovementRequest request,
        ClaimsPrincipal user,
        MovementService movements,
        SettingsService settings,
        CardResolver resolver,
        CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var origin = ReadEndpoint(errors, "origin", request.Origin, CollectionRules.Origins);
        var destination = ReadEndpoint(errors, "destination", request.Destination, CollectionRules.Destinations);
        if (origin is { IsLocation: false } && destination is { IsLocation: false })
        {
            errors.Add(new FieldError("destination", FieldErrorCodes.NotAllowed, "The origin or the destination must be a location."));
        }

        if (request.Cards is not { Count: > 0 })
        {
            errors.Add(new FieldError("cards", FieldErrorCodes.Required, "Give one card or more."));
        }
        else if (request.Cards.Count > CollectionRules.MaxCardsPerRequest)
        {
            errors.Add(new FieldError("cards", FieldErrorCodes.TooLong, $"Give {CollectionRules.MaxCardsPerRequest} cards or fewer."));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var ownerId = user.GetAccountId();
        var fields = await settings.GetAsync(ownerId, cancellationToken);
        IReadOnlyList<MovementRow> rows;
        if (origin!.IsLocation)
        {
            var cards = new List<EntryQuantity>();
            for (var index = 0; index < request.Cards!.Count; index++)
            {
                var card = request.Cards[index];
                if (card.EntryId is null)
                {
                    errors.Add(new FieldError($"cards[{index}].entryId", FieldErrorCodes.Required, "Give the entry of the origin location."));
                }
                else if (card.Quantity is < 1 or > CollectionRules.MaxQuantity)
                {
                    errors.Add(new FieldError($"cards[{index}].quantity", FieldErrorCodes.OutOfRange, $"The quantity must be from 1 to {CollectionRules.MaxQuantity}."));
                }
                else
                {
                    cards.Add(new EntryQuantity(card.EntryId.Value, card.Quantity));
                }
            }

            if (errors.Count > 0)
            {
                return ApiProblems.Validation(errors);
            }

            rows = await movements.TakeAsync(ownerId, origin.LocationId!.Value, destination!, cards, cancellationToken);
        }
        else
        {
            var cards = await resolver.ResolveAsync([.. request.Cards!.Select(card => card.ToCard())], "cards", fields, cancellationToken);
            rows = await movements.AddAsync(ownerId, origin, destination!.LocationId!.Value, cards, cancellationToken);
        }

        var batchId = rows.Count > 0 ? rows[0].BatchId : Guid.Empty;
        var history = rows.Count > 0 ? await movements.ListAsync(ownerId, new MovementQuery(null, batchId, null, CollectionRules.MaxCardsPerRequest * 2), cancellationToken) : [];
        return TypedResults.Ok(new MovementBatchResponse(batchId, [.. history.Reverse().Select(row => ToResponse(row, fields))]));
    }

    private static async Task<Results<Ok<Page<MovementResponse>>, ProblemHttpResult>> ListAsync(
        ClaimsPrincipal user,
        MovementService movements,
        LocationService locations,
        SettingsService settings,
        Guid? locationId,
        Guid? batchId,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!PageRequest.TryCreate(limit, cursor, out var page, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        MovementCursor? position = null;
        if (page.Cursor is { } value && !Cursors.TryDecode(value, out position))
        {
            return ApiProblems.Validation(Cursors.InvalidCursor());
        }

        var ownerId = user.GetAccountId();
        IReadOnlyList<Guid>? locationIds = null;
        if (locationId is { } id)
        {
            // A deleted location keeps its movements, so the filter accepts an unknown location.
            var rows = await locations.ListAsync(ownerId, cancellationToken);
            locationIds = [id, .. rows.Where(row => row.ParentId == id).Select(row => row.Id)];
        }

        var found = await movements.ListAsync(ownerId, new MovementQuery(locationIds, batchId, position is null ? null : (position.CreatedAt, position.Id), page.Limit + 1), cancellationToken);
        var fields = await settings.GetAsync(ownerId, cancellationToken);
        var items = found.Take(page.Limit).Select(row => ToResponse(row, fields)).ToList();
        var next = found.Count > page.Limit ? Cursors.Encode(new MovementCursor(found[page.Limit - 1].CreatedAt, found[page.Limit - 1].Id)) : null;
        return TypedResults.Ok(new Page<MovementResponse>(items, next));
    }

    private static MovementEndpoint? ReadEndpoint(List<FieldError> errors, string field, EndpointRequest? request, IReadOnlyList<string> kinds)
    {
        if (request?.Kind is not { } kind)
        {
            errors.Add(new FieldError($"{field}.kind", FieldErrorCodes.Required, $"The {field} is necessary."));
            return null;
        }

        if (!kinds.Contains(kind))
        {
            errors.Add(new FieldError($"{field}.kind", FieldErrorCodes.NotAllowed, $"The {field} must be one of these values: {string.Join(", ", kinds)}."));
            return null;
        }

        if (kind == CollectionRules.Endpoints.Location && request.LocationId is null)
        {
            errors.Add(new FieldError($"{field}.locationId", FieldErrorCodes.Required, "Give the location."));
            return null;
        }

        return new MovementEndpoint(kind, kind == CollectionRules.Endpoints.Location ? request.LocationId : null);
    }

    internal static MovementResponse ToResponse(MovementRow row, AdvancedFields settings)
    {
        var fields = settings.Output(row.Fields);
        return new MovementResponse(
            row.Id,
            row.BatchId,
            row.Kind,
            new EndpointResponse(row.OriginKind, row.OriginLocationId, row.OriginName),
            new EndpointResponse(row.DestinationKind, row.DestinationLocationId, row.DestinationName),
            row.OracleId,
            row.CardName,
            row.PrintingId,
            row.Language,
            row.Quantity,
            row.InventoryId,
            LocationEndpoints.Utc(row.CreatedAt),
            fields.Finish,
            fields.Condition,
            fields.PurchasePrice,
            fields.PurchasePrice is null ? null : fields.PurchaseCurrency,
            fields.AcquiredOn,
            fields.Note);
    }

    /// <summary>
    /// The position of the next page: the date and the ID of the last movement.
    /// </summary>
    private sealed record MovementCursor(DateTime CreatedAt, Guid Id);
}
