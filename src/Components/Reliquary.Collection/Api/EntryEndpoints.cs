using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Collection.Data;

namespace Reliquary.Collection.Api;

/// <summary>
/// The endpoints of the entries of the locations.
/// </summary>
internal static class EntryEndpoints
{
    public static void Map(RouteGroupBuilder collection)
    {
        collection.MapGet("/locations/{locationId:guid}/entries", ListAsync)
            .WithName("ListLocationEntries")
            .WithSummary("Lists the entries of a location by card name, with the entries of its sections when sections is true. The parameter q finds a part of the card name. The parameters language and set filter the entries.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapPost("/locations/{locationId:guid}/entries", AddAsync)
            .WithName("AddLocationEntries")
            .WithSummary("Adds cards to a location. A card with the same card, printing, language and advanced fields as an entry adds to the quantity of the entry. Error location_locked for a locked location.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapPatch("/entries/{entryId:guid}", UpdateAsync)
            .WithName("UpdateLocationEntry")
            .WithSummary("Changes the quantity or the fields of an entry. A quantity of 0 removes the entry. Error location_locked for a locked location.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapDelete("/entries/{entryId:guid}", DeleteAsync)
            .WithName("DeleteLocationEntry")
            .WithSummary("Removes all the copies of an entry from the collection. The parameter destination gives sale, gift, trade or nowhere (the default).")
            .RequireScope(Scopes.CollectionWrite);
    }

    private static async Task<Results<Ok<Page<EntryResponse>>, ProblemHttpResult>> ListAsync(
        Guid locationId,
        ClaimsPrincipal user,
        LocationService locations,
        EntryService entries,
        SettingsService settings,
        ICardCatalog catalog,
        bool? sections,
        string? q,
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

        EntryCursor? position = null;
        if (page.Cursor is { } value && !Cursors.TryDecode(value, out position))
        {
            return ApiProblems.Validation(Cursors.InvalidCursor());
        }

        var ownerId = user.GetAccountId();
        var location = await locations.GetAsync(ownerId, locationId, cancellationToken);
        var locationIds = new List<Guid> { location.Id };
        if (sections == true)
        {
            locationIds.AddRange((await locations.ListAsync(ownerId, cancellationToken)).Where(row => row.ParentId == location.Id).Select(row => row.Id));
        }

        var rows = await entries.ListAsync(ownerId, new EntryPageQuery(locationIds, q, position?.Name, position?.Id, page.Limit + 1, language, set), cancellationToken);
        var items = await EntryResponses.BuildAsync(rows.Take(page.Limit).ToList(), catalog, await settings.GetAsync(ownerId, cancellationToken), cancellationToken);
        var next = rows.Count > page.Limit ? Cursors.Encode(new EntryCursor(rows[page.Limit - 1].CardName, rows[page.Limit - 1].Id)) : null;
        return TypedResults.Ok(new Page<EntryResponse>(items, next));
    }

    private static async Task<Results<Ok<EntriesChangeResponse>, ProblemHttpResult>> AddAsync(
        Guid locationId,
        AddEntriesRequest request,
        ClaimsPrincipal user,
        EntryService entries,
        SettingsService settings,
        CardResolver resolver,
        ICardCatalog catalog,
        CancellationToken cancellationToken)
    {
        var origin = request.Origin ?? CollectionRules.Endpoints.Nowhere;
        if (origin == CollectionRules.Endpoints.Location || !CollectionRules.Origins.Contains(origin))
        {
            return ApiProblems.Validation(new FieldError("origin", FieldErrorCodes.NotAllowed, "The origin must be purchase, gift, trade or nowhere. To move cards between locations, use the movements."));
        }

        var fields = await settings.GetAsync(user.GetAccountId(), cancellationToken);
        var cards = await resolver.ResolveAsync(request.Entries, "entries", fields, cancellationToken);
        var change = await entries.AddAsync(user.GetAccountId(), locationId, cards, new MovementEndpoint(origin), cancellationToken);
        return TypedResults.Ok(new EntriesChangeResponse(change.BatchId, await EntryResponses.BuildAsync(change.Entries, catalog, fields, cancellationToken)));
    }

    private static async Task<Ok<EntriesChangeResponse>> UpdateAsync(
        Guid entryId, UpdateEntryRequest request, ClaimsPrincipal user, EntryService entries, SettingsService settings, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var changes = new EntryChanges(
            request.Quantity,
            request.PrintingId,
            request.ClearPrinting == true,
            request.Language,
            request.Finish,
            request.Condition,
            request.PurchasePrice,
            request.PurchaseCurrency,
            request.ClearPurchasePrice == true,
            request.AcquiredOn,
            request.ClearAcquiredOn == true,
            request.Note);
        var change = await entries.UpdateAsync(user.GetAccountId(), entryId, changes, cancellationToken);
        var fields = await settings.GetAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Ok(new EntriesChangeResponse(change.BatchId, await EntryResponses.BuildAsync(change.Entries, catalog, fields, cancellationToken)));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid entryId, ClaimsPrincipal user, EntryService entries, string? destination, CancellationToken cancellationToken)
    {
        var kind = destination ?? CollectionRules.Endpoints.Nowhere;
        if (kind == CollectionRules.Endpoints.Location || !CollectionRules.Destinations.Contains(kind))
        {
            return ApiProblems.Validation(new FieldError("destination", FieldErrorCodes.NotAllowed, "The destination must be sale, gift, trade or nowhere. To move cards between locations, use the movements."));
        }

        await entries.DeleteAsync(user.GetAccountId(), entryId, new MovementEndpoint(kind), cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The position of the next page: the card name and the ID of the last entry.
    /// </summary>
    private sealed record EntryCursor(string Name, Guid Id);
}

/// <summary>
/// Makes the responses of the entries, with the data of their printings.
/// </summary>
internal static class EntryResponses
{
    public static async Task<IReadOnlyList<EntryResponse>> BuildAsync(IReadOnlyList<EntryRow> rows, ICardCatalog catalog, AdvancedFields settings, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var withoutPrinting = rows.Where(row => row.PrintingId is null).Select(row => row.OracleId).Distinct().ToList();
        var cards = withoutPrinting.Count > 0 ? await catalog.GetCardsAsync(withoutPrinting, cancellationToken) : new Dictionary<Guid, CardFacts>();
        var printingIds = rows.Where(row => row.PrintingId is not null).Select(row => row.PrintingId!.Value)
            .Concat(cards.Values.Where(card => card.DefaultPrintingId is not null).Select(card => card.DefaultPrintingId!.Value))
            .Distinct()
            .ToList();
        var printings = await catalog.GetPrintingsAsync(printingIds, cancellationToken);
        return [.. rows.Select(row =>
        {
            var printing = row.PrintingId is { } printingId ? printings.GetValueOrDefault(printingId) : null;
            var image = printing?.ImageUri;
            if (row.PrintingId is null && cards.TryGetValue(row.OracleId, out var card) && card.DefaultPrintingId is { } defaultId)
            {
                image = printings.GetValueOrDefault(defaultId)?.ImageUri;
            }

            var fields = settings.Output(row.Fields);
            return new EntryResponse(
                row.Id,
                row.LocationId,
                row.OracleId,
                row.CardName,
                row.PrintingId,
                row.SetCode,
                printing?.CollectorNumber,
                image,
                row.Language,
                row.Quantity,
                fields.Finish,
                fields.Condition,
                fields.PurchasePrice,
                fields.PurchasePrice is null ? null : fields.PurchaseCurrency,
                fields.AcquiredOn,
                fields.Note);
        })];
    }
}
