using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints of the decks and of their lines.
/// </summary>
internal static class DeckEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var decks = api.MapGroup("/decks").WithTags("Decks").AddEndpointFilter<DeckExceptionFilter>();
        var owner = decks.MapGroup(string.Empty).RequireAuthorization(AuthPolicies.User);

        owner.MapGet(string.Empty, ListAsync)
            .WithName("ListDecks")
            .WithSummary("Lists the decks of the user, the most recent change first.");

        owner.MapPost(string.Empty, CreateAsync)
            .WithName("CreateDeck")
            .WithSummary("Creates a deck, with its first lines.");

        decks.MapGet("/{deckId:guid}", GetAsync)
            .WithName("GetDeck")
            .WithSummary("Gets a deck with its lines and the data of its cards. The owner gets each deck. The other users and the visitors get the unlisted and public decks.");

        owner.MapPatch("/{deckId:guid}", UpdateAsync)
            .WithName("UpdateDeck")
            .WithSummary("Changes the name, the format, the visibility, the folder or the description of a deck.");

        owner.MapDelete("/{deckId:guid}", DeleteAsync)
            .WithName("DeleteDeck")
            .WithSummary("Deletes a deck and its lines.");

        owner.MapPost("/{deckId:guid}/lines", AddLinesAsync)
            .WithName("AddDeckLines")
            .WithSummary("Adds cards to a deck. A line with the same card, printing and finish in the same zone gets the quantity added.");

        owner.MapPut("/{deckId:guid}/lines", ReplaceLinesAsync)
            .WithName("ReplaceDeckLines")
            .WithSummary("Replaces all the lines of a deck.");

        owner.MapPatch("/{deckId:guid}/lines/{lineId:guid}", UpdateLineAsync)
            .WithName("UpdateDeckLine")
            .WithSummary("Changes the quantity, the zone, the printing, the finish or the note of a line. A quantity of 0 removes the line.");

        owner.MapDelete("/{deckId:guid}/lines/{lineId:guid}", DeleteLineAsync)
            .WithName("DeleteDeckLine")
            .WithSummary("Removes a line from a deck.");
    }

    private static async Task<Results<Ok<Page<DeckSummaryResponse>>, ProblemHttpResult>> ListAsync(
        ClaimsPrincipal user, DeckService decks, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (!PageRequest.TryCreate(limit, cursor, out var page, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        (DateTime, Guid)? after = null;
        if (page.Cursor is { } value)
        {
            if (!Cursors.TryDecode<DeckCursor>(value, out var position))
            {
                return ApiProblems.Validation(Cursors.InvalidCursor());
            }

            after = (position!.UpdatedAt, position.Id);
        }

        var rows = await decks.ListAsync(user.GetAccountId(), after, page.Limit + 1, cancellationToken);
        var items = rows.Take(page.Limit).Select(row => new DeckSummaryResponse(
            row.Id,
            row.Name,
            row.Format,
            row.Visibility,
            row.FolderId,
            row.Pinned,
            row.ArchivedAt is not null,
            row.CardCount,
            row.CommanderIds,
            Utc(row.CreatedAt),
            Utc(row.UpdatedAt))).ToList();
        var next = rows.Count > page.Limit ? Cursors.Encode(new DeckCursor(rows[page.Limit - 1].UpdatedAt, rows[page.Limit - 1].Id)) : null;
        return TypedResults.Ok(new Page<DeckSummaryResponse>(items, next));
    }

    private static async Task<Results<Created<DeckResponse>, ProblemHttpResult>> CreateAsync(
        CreateDeckRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors.Add(new FieldError("name", FieldErrorCodes.Required, "The name of the deck is necessary."));
        }

        ValidateDeckFields(errors, name, request.Format, request.Visibility, request.Description);
        var lines = await ReadLinesAsync(errors, request.Lines ?? [], catalog, cancellationToken);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var deck = await decks.CreateAsync(
            user.GetAccountId(),
            new NewDeck(name!, request.Format ?? "commander", request.Visibility ?? "private", request.FolderId, request.Description ?? string.Empty, null),
            lines,
            cancellationToken);
        var view = await decks.GetAsync(user.GetAccountId(), deck.Id, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/decks/{deck.Id}", await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static async Task<Ok<DeckResponse>> GetAsync(Guid deckId, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, HttpResponse response, CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(user.FindAccountId(), deckId, cancellationToken);
        response.Headers.ETag = $"\"{view.Deck.Version}\"";
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> UpdateAsync(
        Guid deckId, UpdateDeckRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new FieldError("name", FieldErrorCodes.TooShort, "The name of the deck must contain a character that is not a space."));
        }

        ValidateDeckFields(errors, request.Name?.Trim(), request.Format, request.Visibility, request.Description);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var view = await decks.UpdateAsync(
            user.GetAccountId(),
            deckId,
            new DeckChanges(request.Name, request.Format, request.Visibility, request.FolderId, request.ClearFolder == true, request.Description, request.Pinned, request.Archived, request.ExpectedVersion),
            cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static async Task<NoContent> DeleteAsync(Guid deckId, ClaimsPrincipal user, DeckService decks, CancellationToken cancellationToken)
    {
        await decks.DeleteAsync(user.GetAccountId(), deckId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> AddLinesAsync(
        Guid deckId, AddLinesRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Lines is not { Count: > 0 })
        {
            errors.Add(new FieldError("lines", FieldErrorCodes.Required, "Give one line or more."));
        }

        var lines = await ReadLinesAsync(errors, request.Lines ?? [], catalog, cancellationToken);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var view = await decks.AddLinesAsync(user.GetAccountId(), deckId, lines, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> ReplaceLinesAsync(
        Guid deckId, ReplaceLinesRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Lines is null)
        {
            errors.Add(new FieldError("lines", FieldErrorCodes.Required, "Give the list of the lines. An empty list removes all the lines."));
        }

        var lines = await ReadLinesAsync(errors, request.Lines ?? [], catalog, cancellationToken);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var view = await decks.ReplaceLinesAsync(user.GetAccountId(), deckId, lines, request.ExpectedVersion, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> UpdateLineAsync(
        Guid deckId, Guid lineId, UpdateLineRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Quantity is < 0 or > DeckRules.MaxQuantity)
        {
            errors.Add(new FieldError("quantity", FieldErrorCodes.OutOfRange, $"The quantity must be from 0 to {DeckRules.MaxQuantity}."));
        }

        ValidateLineFields(errors, string.Empty, request.Zone, request.Finish, request.Note);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var view = await decks.UpdateLineAsync(
            user.GetAccountId(),
            deckId,
            lineId,
            new LineChanges(request.Quantity, request.Zone, request.PrintingId, request.ClearPrinting == true, request.Finish, request.ClearFinish == true, request.Note),
            cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static async Task<Ok<DeckResponse>> DeleteLineAsync(Guid deckId, Guid lineId, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var view = await decks.DeleteLineAsync(user.GetAccountId(), deckId, lineId, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static void ValidateDeckFields(List<FieldError> errors, string? name, string? format, string? visibility, string? description)
    {
        if (name is { Length: > DeckRules.MaxNameLength })
        {
            errors.Add(new FieldError("name", FieldErrorCodes.TooLong, $"The name of the deck must have {DeckRules.MaxNameLength} characters or fewer."));
        }

        if (format is not null && !DeckRules.Formats.Contains(format))
        {
            errors.Add(new FieldError("format", FieldErrorCodes.NotAllowed, $"The format is not known. Use one of these formats: {string.Join(", ", DeckRules.Formats)}."));
        }

        if (visibility is not null && !DeckRules.Visibilities.Contains(visibility))
        {
            errors.Add(new FieldError("visibility", FieldErrorCodes.NotAllowed, "The visibility must be \"private\", \"unlisted\" or \"public\"."));
        }

        if (description is { Length: > DeckRules.MaxDescriptionLength })
        {
            errors.Add(new FieldError("description", FieldErrorCodes.TooLong, $"The description must have {DeckRules.MaxDescriptionLength} characters or fewer."));
        }
    }

    private static void ValidateLineFields(List<FieldError> errors, string prefix, string? zone, string? finish, string? note)
    {
        if (zone is not null && !DeckRules.Zones.Contains(zone))
        {
            errors.Add(new FieldError($"{prefix}zone", FieldErrorCodes.NotAllowed, $"The zone must be one of these zones: {string.Join(", ", DeckRules.Zones)}."));
        }

        if (finish is not null && !DeckRules.Finishes.Contains(finish))
        {
            errors.Add(new FieldError($"{prefix}finish", FieldErrorCodes.NotAllowed, "The finish must be \"nonfoil\", \"foil\" or \"etched\"."));
        }

        if (note is { Length: > DeckRules.MaxNoteLength })
        {
            errors.Add(new FieldError($"{prefix}note", FieldErrorCodes.TooLong, $"The note must have {DeckRules.MaxNoteLength} characters or fewer."));
        }
    }

    /// <summary>
    /// Checks the line requests, and finds the card of each line that gives a name.
    /// </summary>
    private static async Task<IReadOnlyList<LineInput>> ReadLinesAsync(List<FieldError> errors, IReadOnlyList<LineRequest> requests, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        if (requests.Count > DeckRules.MaxLines)
        {
            errors.Add(new FieldError("lines", FieldErrorCodes.TooLong, $"Give {DeckRules.MaxLines} lines or fewer."));
            return [];
        }

        var names = requests.Where(line => line.OracleId is null && !string.IsNullOrWhiteSpace(line.Name)).Select(line => line.Name!).ToList();
        var resolved = names.Count > 0 ? await catalog.ResolveNamesAsync(names, cancellationToken) : new Dictionary<string, CardNameMatch>();
        var inputs = new List<LineInput>();
        for (var index = 0; index < requests.Count; index++)
        {
            var line = requests[index];
            var prefix = $"lines[{index}].";
            var oracleId = line.OracleId;
            if (oracleId is null)
            {
                if (string.IsNullOrWhiteSpace(line.Name))
                {
                    errors.Add(new FieldError($"{prefix}oracleId", FieldErrorCodes.Required, "Give the Oracle ID or the name of the card."));
                }
                else if (resolved.TryGetValue(line.Name, out var match) && match.OracleId is { } found)
                {
                    oracleId = found;
                }
                else
                {
                    errors.Add(new FieldError($"{prefix}name", DeckFieldErrorCodes.CardNotFound, $"No card has the name \"{line.Name}\"."));
                }
            }

            if (line.Quantity is < 1 or > DeckRules.MaxQuantity)
            {
                errors.Add(new FieldError($"{prefix}quantity", FieldErrorCodes.OutOfRange, $"The quantity must be from 1 to {DeckRules.MaxQuantity}."));
            }

            ValidateLineFields(errors, prefix, line.Zone, line.Finish, line.Note);
            if (oracleId is { } id)
            {
                inputs.Add(new LineInput(id, line.PrintingId, line.Finish, line.Quantity ?? 1, line.Zone ?? "main", line.Note?.Trim() ?? string.Empty));
            }
        }

        return inputs;
    }

    /// <summary>
    /// Makes the response of a deck, with the data of its cards and printings.
    /// </summary>
    internal static async Task<DeckResponse> ToResponseAsync(DeckView view, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var cards = await catalog.GetCardsAsync([.. view.Lines.Select(line => line.OracleId).Distinct()], cancellationToken);
        var printingIds = view.Lines.Where(line => line.PrintingId is not null).Select(line => line.PrintingId!.Value)
            .Concat(cards.Values.Where(card => card.DefaultPrintingId is not null).Select(card => card.DefaultPrintingId!.Value))
            .Distinct().ToList();
        var printings = await catalog.GetPrintingsAsync(printingIds, cancellationToken);
        var deck = view.Deck;
        var counts = DeckRules.Zones.ToDictionary(zone => zone, zone => view.Lines.Where(line => line.Zone == zone).Sum(line => line.Quantity));
        return new DeckResponse(
            deck.Id,
            deck.OwnerId,
            deck.Name,
            deck.Format,
            deck.Visibility,
            view.IsOwner ? deck.FolderId : null,
            deck.Description,
            deck.SourceDeckId,
            deck.Pinned,
            deck.ArchivedAt is not null,
            deck.Version,
            Utc(deck.CreatedAt),
            Utc(deck.UpdatedAt),
            view.IsOwner,
            counts,
            [.. view.Lines.Select(line => new DeckLineResponse(line.Id, line.OracleId, line.PrintingId, line.Finish, line.Quantity, line.Zone, line.Note))],
            cards.ToDictionary(pair => pair.Key, pair => new DeckCardResponse(
                pair.Value.Name,
                pair.Value.Layout,
                pair.Value.ManaValue,
                pair.Value.Analysis.CurveManaValue,
                pair.Value.Analysis.PrimaryManaCost,
                pair.Value.TypeLine,
                pair.Value.ColorIdentity,
                pair.Value.Analysis.CardTypes,
                pair.Value.Analysis.IsLand,
                pair.Value.DefaultPrintingId,
                [.. pair.Value.Faces.Select(face => new DeckCardFaceResponse(face.Name, face.ManaCost, face.TypeLine))])),
            printings.ToDictionary(pair => pair.Key, pair => new DeckPrintingResponse(pair.Value.SetCode, pair.Value.CollectorNumber, pair.Value.Lang, pair.Value.ImageUri)));
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record DeckCursor(DateTime UpdatedAt, Guid Id);
}

/// <summary>
/// The field error codes of the Decks endpoints.
/// </summary>
public static class DeckFieldErrorCodes
{
    /// <summary>No card has the name of the line.</summary>
    public const string CardNotFound = "card_not_found";
}

/// <summary>
/// Converts a <see cref="DeckException"/> into a problem response.
/// </summary>
internal sealed class DeckExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (DeckException exception)
        {
            var extensions = exception.Errors.Count > 0
                ? new Dictionary<string, object?> { [ApiProblems.ErrorsMember] = exception.Errors }
                : null;
            return ApiProblems.Problem(exception.Status, exception.Code, exception.Message, extensions);
        }
    }
}
