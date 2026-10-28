using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Collection.Api;

/// <summary>
/// The endpoint of the global view of the collection.
/// </summary>
internal static class ViewEndpoints
{
    public static void Map(RouteGroupBuilder collection) =>
        collection.MapGet("/cards", ListCardsAsync)
            .WithName("ListCollectionCards")
            .WithSummary("Lists all the cards of the collection by name, grouped by Oracle card, with the copies in each location. "
                + "Filters: q (a part of the card name), locationId (a location and its sections), language and set.")
            .RequireScope(Scopes.CollectionRead);

    private static async Task<Results<Ok<Page<CardViewResponse>>, ProblemHttpResult>> ListCardsAsync(
        ClaimsPrincipal user,
        CollectionViews views,
        LocationService locations,
        ICardCatalog catalog,
        string? q,
        Guid? locationId,
        string? language,
        string? set,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!PageRequest.TryCreate(limit, cursor, out var page, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        CardCursor? position = null;
        if (page.Cursor is { } value && !Cursors.TryDecode(value, out position))
        {
            return ApiProblems.Validation(Cursors.InvalidCursor());
        }

        var ownerId = user.GetAccountId();
        IReadOnlyList<Guid>? locationIds = null;
        if (locationId is { } id)
        {
            var rows = await locations.ListAsync(ownerId, cancellationToken);
            if (!rows.Any(row => row.Id == id))
            {
                return ApiProblems.Validation(new FieldError("locationId", FieldErrorCodes.NotAllowed, "The location does not exist."));
            }

            locationIds = [.. rows.Where(row => row.Id == id || row.ParentId == id).Select(row => row.Id)];
        }

        var cards = await views.ListCardsAsync(
            ownerId,
            new CardViewQuery(locationIds, q, string.IsNullOrEmpty(language) ? null : language, string.IsNullOrEmpty(set) ? null : set, position?.Name, position?.Id, page.Limit + 1),
            cancellationToken);
        var shown = cards.Take(page.Limit).ToList();
        var facts = await catalog.GetCardsAsync([.. shown.Select(card => card.OracleId)], cancellationToken);
        var printings = await catalog.GetPrintingsAsync([.. facts.Values.Where(card => card.DefaultPrintingId is not null).Select(card => card.DefaultPrintingId!.Value)], cancellationToken);
        var items = shown.Select(card => new CardViewResponse(
            card.OracleId,
            card.Name,
            card.Quantity,
            facts.TryGetValue(card.OracleId, out var fact) && fact.DefaultPrintingId is { } printingId ? printings.GetValueOrDefault(printingId)?.ImageUri : null,
            [.. card.Locations.Select(location => new CardViewLocationResponse(location.LocationId, location.LocationName, location.SectionName, location.Kind, location.Quantity))])).ToList();
        var next = cards.Count > page.Limit ? Cursors.Encode(new CardCursor(shown[^1].Name, shown[^1].OracleId)) : null;
        return TypedResults.Ok(new Page<CardViewResponse>(items, next));
    }

    /// <summary>
    /// The position of the next page: the name and the Oracle ID of the last card.
    /// </summary>
    private sealed record CardCursor(string Name, Guid Id);
}
