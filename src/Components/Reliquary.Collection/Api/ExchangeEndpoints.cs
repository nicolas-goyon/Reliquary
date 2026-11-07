using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Collection.Data;
using Reliquary.Collection.Exchange;

namespace Reliquary.Collection.Api;

/// <summary>
/// The endpoints of the import and the export of the collection.
/// </summary>
internal static class ExchangeEndpoints
{
    private const string DefaultCurrency = "USD";

    public static void Map(RouteGroupBuilder collection)
    {
        collection.MapPost("/locations/{locationId:guid}/import", ImportAsync)
            .WithName("ImportLocationEntries")
            .WithSummary("Adds the cards of a file of another tool to a location. The import ignores the lines that it cannot use and gives them in errors, with their numbers. Error location_locked for a locked location.")
            .RequireScope(Scopes.CollectionWrite);

        collection.MapGet("/locations/{locationId:guid}/export", ExportLocationAsync)
            .WithName("ExportLocation")
            .WithSummary("Gets the cards of a location as a file of another tool. The cards of the sections are in the file when sections is true.")
            .RequireScope(Scopes.CollectionRead);

        collection.MapGet("/export", ExportCollectionAsync)
            .WithName("ExportCollection")
            .WithSummary("Gets the cards of all the locations as a file of another tool.")
            .RequireScope(Scopes.CollectionRead);
    }

    private static async Task<Results<Ok<ImportEntriesResponse>, ProblemHttpResult>> ImportAsync(
        Guid locationId, ImportEntriesRequest request, ClaimsPrincipal user, CollectionExchangeService exchange, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrEmpty(request.Format))
        {
            errors.Add(new FieldError("format", FieldErrorCodes.Required, "The file format is necessary."));
        }
        else if (!CollectionFormats.All.ContainsKey(request.Format))
        {
            errors.Add(new FieldError("format", FieldErrorCodes.NotAllowed, $"The file format is not known. Use one of these formats: {string.Join(", ", CollectionFormats.All.Keys)}."));
        }

        if (request.Content is null)
        {
            errors.Add(new FieldError("content", FieldErrorCodes.Required, "The content of the file is necessary."));
        }
        else if (request.Content.Length > CollectionRules.MaxImportLength)
        {
            errors.Add(new FieldError("content", FieldErrorCodes.TooLong, $"The file must have {CollectionRules.MaxImportLength} characters or fewer."));
        }

        var origin = request.Origin ?? CollectionRules.Endpoints.Nowhere;
        if (origin == CollectionRules.Endpoints.Location || !CollectionRules.Origins.Contains(origin))
        {
            errors.Add(new FieldError("origin", FieldErrorCodes.NotAllowed, "The origin must be purchase, gift, trade or nowhere."));
        }

        var currency = request.Currency is null ? DefaultCurrency : FileValues.ReadCurrency(request.Currency);
        if (currency is null)
        {
            errors.Add(new FieldError("currency", FieldErrorCodes.InvalidFormat, "Give the currency as a code of 3 letters, for example EUR or USD."));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var result = await exchange.ImportAsync(
            user.GetAccountId(), locationId, CollectionFormats.All[request.Format!], request.Content!, new MovementEndpoint(origin), currency!, cancellationToken);
        return TypedResults.Ok(new ImportEntriesResponse(
            result.BatchId,
            result.LineCount,
            result.CardCount,
            [.. result.Errors.Select(Line)],
            [.. result.Warnings.Select(Line)]));
    }

    private static Task<Results<Ok<ExportCollectionResponse>, ProblemHttpResult>> ExportLocationAsync(
        Guid locationId, string? format, bool? sections, ClaimsPrincipal user, CollectionExchangeService exchange, CancellationToken cancellationToken) =>
        ExportAsync(locationId, format, sections == true, user, exchange, cancellationToken);

    private static Task<Results<Ok<ExportCollectionResponse>, ProblemHttpResult>> ExportCollectionAsync(
        string? format, ClaimsPrincipal user, CollectionExchangeService exchange, CancellationToken cancellationToken) =>
        ExportAsync(null, format, true, user, exchange, cancellationToken);

    private static async Task<Results<Ok<ExportCollectionResponse>, ProblemHttpResult>> ExportAsync(
        Guid? locationId, string? format, bool sections, ClaimsPrincipal user, CollectionExchangeService exchange, CancellationToken cancellationToken)
    {
        if (format is null || !CollectionFormats.All.TryGetValue(format, out var fileFormat))
        {
            return ApiProblems.Validation(new FieldError("format", FieldErrorCodes.NotAllowed, $"Give one of these file formats: {string.Join(", ", CollectionFormats.All.Keys)}."));
        }

        var file = await exchange.ExportAsync(user.GetAccountId(), locationId, sections, fileFormat, cancellationToken);
        return TypedResults.Ok(new ExportCollectionResponse(file.FileName, file.ContentType, file.Content));
    }

    private static ImportLineResponse Line(FileLineError line) => new(line.Line, line.Code, line.Text);
}
