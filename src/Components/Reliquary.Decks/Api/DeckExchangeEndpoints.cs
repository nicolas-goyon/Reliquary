using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Exchange;
using Reliquary.Decks.Text;
using static Reliquary.Decks.Api.DeckRequestRules;
using static Reliquary.Decks.Api.DeckResponses;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints that import and export decks: the files of other tools, the links and the deck text format.
/// </summary>
internal static class DeckExchangeEndpoints
{
    internal static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportDecksAsync(
        ExportDecksRequest request, ClaimsPrincipal user, DeckExchangeService exchange, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var deckIds = ValidateDeckIds(errors, request.DeckIds);
        if (request.Format is null || !DeckFormats.All.TryGetValue(request.Format, out var format))
        {
            errors.Add(new FieldError("format", FieldErrorCodes.NotAllowed, $"Give one of these file formats: {string.Join(", ", DeckFormats.All.Keys)}."));
            return ApiProblems.Validation(errors);
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var archive = await exchange.ExportArchiveAsync(user.GetAccountId(), deckIds, format, cancellationToken);
        return TypedResults.File(archive, "application/zip", "decks.zip");
    }

    internal static async Task<Results<Created<ImportDeckResponse>, ProblemHttpResult>> ImportAsync(
        ImportDeckRequest request, ClaimsPrincipal user, DeckExchangeService exchange, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrEmpty(request.Format))
        {
            errors.Add(new FieldError("format", FieldErrorCodes.Required, "The file format is necessary."));
        }
        else if (!DeckFormats.All.ContainsKey(request.Format))
        {
            errors.Add(new FieldError("format", FieldErrorCodes.NotAllowed, $"The file format is not known. Use one of these formats: {string.Join(", ", DeckFormats.All.Keys)}."));
        }

        if (request.Content is null)
        {
            errors.Add(new FieldError("content", FieldErrorCodes.Required, "The content of the file is necessary."));
        }
        else if (request.Content.Length > MaxTextLength)
        {
            errors.Add(new FieldError("content", FieldErrorCodes.TooLong, $"The file must have {MaxTextLength} characters or fewer."));
        }

        if (request.Columns is { Count: > 0 })
        {
            ValidateColumns(errors, request.Format, request.Columns);
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? "Imported deck" : request.Name.Trim();
        ValidateDeckFields(errors, name, request.DeckFormat, request.Visibility, null);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var format = DeckFormats.All[request.Format!];
        if (request.Columns is { Count: > 0 } && format is CsvDeckFormat csv)
        {
            format = csv.WithColumns(request.Columns);
        }

        var (deck, warnings) = await exchange.ImportAsync(
            user.GetAccountId(),
            format,
            request.Content!,
            new NewDeck(name, request.DeckFormat ?? "commander", request.Visibility ?? "private", null, string.Empty, null),
            cancellationToken);
        var view = await decks.GetAsync(user.GetAccountId(), deck.Id, cancellationToken);
        return TypedResults.Created(
            $"{ApiRoutes.Base}/decks/{deck.Id}",
            new ImportDeckResponse(await ToResponseAsync(view, catalog, cancellationToken), [.. warnings.Select(warning => new DeckTextLineError(warning.Line, warning.Code, warning.Text))]));
    }

    internal static async Task<Results<Created<ImportDeckResponse>, ProblemHttpResult>> ImportLinkAsync(
        ImportDeckLinkRequest request, ClaimsPrincipal user, DeckLinkReader reader, DeckExchangeService exchange, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var link = DeckLinks.Parse(request.Url);
        if (link is null)
        {
            errors.Add(new FieldError("url", string.IsNullOrWhiteSpace(request.Url) ? FieldErrorCodes.Required : FieldErrorCodes.InvalidFormat, "Give the address of a deck page on Moxfield or Archidekt."));
        }

        var requestedName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        ValidateDeckFields(errors, requestedName ?? "Imported deck", request.DeckFormat, request.Visibility, null);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var linked = await reader.ReadAsync(link!, cancellationToken);
        var remoteName = linked.Name.Trim();
        var name = requestedName ?? (remoteName.Length is > 0 and <= DeckRules.MaxNameLength ? remoteName : "Imported deck");
        var (deck, warnings) = await exchange.ImportAsync(
            user.GetAccountId(),
            DeckFormats.All["text"],
            linked.Text,
            new NewDeck(name, request.DeckFormat ?? "commander", request.Visibility ?? "private", null, string.Empty, null),
            cancellationToken);
        var view = await decks.GetAsync(user.GetAccountId(), deck.Id, cancellationToken);
        return TypedResults.Created(
            $"{ApiRoutes.Base}/decks/{deck.Id}",
            new ImportDeckResponse(await ToResponseAsync(view, catalog, cancellationToken), [.. warnings.Select(warning => new DeckTextLineError(warning.Line, warning.Code, warning.Text))]));
    }

    private static void ValidateColumns(List<FieldError> errors, string? format, IReadOnlyDictionary<string, string> columns)
    {
        if (format != CsvFormats.GenericName)
        {
            errors.Add(new FieldError("columns", FieldErrorCodes.NotAllowed, $"Only the format {CsvFormats.GenericName} accepts a column map."));
            return;
        }

        foreach (var (field, header) in columns)
        {
            if (!CsvFields.All.Contains(field))
            {
                errors.Add(new FieldError($"columns.{field}", FieldErrorCodes.NotAllowed, $"The field is not known. Use one of these fields: {string.Join(", ", CsvFields.All)}."));
            }
            else if (string.IsNullOrWhiteSpace(header) || header.Length > MaxColumnHeaderLength)
            {
                errors.Add(new FieldError($"columns.{field}", FieldErrorCodes.TooLong, $"The header must have 1 to {MaxColumnHeaderLength} characters."));
            }
        }
    }

    internal static async Task<Results<Ok<ExportDeckResponse>, ProblemHttpResult>> ExportAsync(Guid deckId, string? format, ClaimsPrincipal user, DeckExchangeService exchange, CancellationToken cancellationToken)
    {
        if (format is null || !DeckFormats.All.TryGetValue(format, out var deckFormat))
        {
            return ApiProblems.Validation(new FieldError("format", FieldErrorCodes.NotAllowed, $"Give one of these file formats: {string.Join(", ", DeckFormats.All.Keys)}."));
        }

        var file = await exchange.ExportAsync(user.FindAccountId(), deckId, deckFormat, cancellationToken);
        return TypedResults.Ok(new ExportDeckResponse(file.FileName, file.ContentType, file.Content));
    }

    internal static async Task<Ok<DeckTextResponse>> GetTextAsync(Guid deckId, ClaimsPrincipal user, DeckTextService texts, CancellationToken cancellationToken)
    {
        var (text, version) = await texts.GetAsync(user.FindAccountId(), deckId, cancellationToken);
        return TypedResults.Ok(new DeckTextResponse(text, version));
    }

    internal static async Task<Results<Ok<DeckResponse>, NoContent, ProblemHttpResult>> SetTextAsync(
        Guid deckId, SetDeckTextRequest request, ClaimsPrincipal user, DeckTextService texts, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        if (request.Text is null)
        {
            return ApiProblems.Validation(new FieldError("text", FieldErrorCodes.Required, "The text is necessary. An empty text removes all the lines."));
        }

        if (request.Text.Length > MaxTextLength)
        {
            return ApiProblems.Validation(new FieldError("text", FieldErrorCodes.TooLong, $"The text must have {MaxTextLength} characters or fewer."));
        }

        var view = await texts.SetAsync(user.GetAccountId(), deckId, request.Text, request.ExpectedVersion, request.DryRun == true, cancellationToken);
        return view is null ? TypedResults.NoContent() : TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private const int MaxColumnHeaderLength = 100;
}
