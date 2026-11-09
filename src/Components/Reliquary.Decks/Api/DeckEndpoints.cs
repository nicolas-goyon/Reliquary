using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Data;
using Reliquary.Decks.Text;
using static Reliquary.Decks.Api.DeckRequestRules;
using static Reliquary.Decks.Api.DeckResponses;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints of the decks: list, create, get, change, delete and copy. The other endpoint groups of the decks are in the files Deck*Endpoints.cs.
/// </summary>
internal static class DeckEndpoints
{
    /// <summary>
    /// Maps the endpoints under /decks. The handlers of the lines, the import and export, the checks and the groups of decks are in the files Deck*Endpoints.cs.
    /// The OpenAPI document lists the endpoints in the order of this method, so keep the order when you add an endpoint.
    /// </summary>
    public static void Map(IEndpointRouteBuilder api)
    {
        var decks = api.MapGroup("/decks").WithTags("Decks").AddEndpointFilter<DeckExceptionFilter>();
        var owner = decks.MapGroup(string.Empty).RequireAuthorization(AuthPolicies.User);

        owner.MapGet(string.Empty, ListAsync)
            .WithName("ListDecks")
            .WithSummary("Lists the decks of the user with filters by folder, format, color identity, deck tag and archive. The pinned decks come first, then the sort applies.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPost(string.Empty, CreateAsync)
            .WithName("CreateDeck")
            .WithSummary("Creates a deck, with its first lines.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapGet("/tags", DeckGroupEndpoints.ListDeckTagsAsync)
            .WithName("ListDeckTags")
            .WithSummary("Lists the deck tags of the user, with the number of decks of each tag.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPost("/group", DeckGroupEndpoints.ApplyGroupActionAsync)
            .WithName("ApplyDeckGroupAction")
            .WithSummary("Applies one action to several decks of the user: move, tag, untag, pin, unpin, archive, unarchive or delete.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/export", DeckExchangeEndpoints.ExportDecksAsync)
            .WithName("ExportDecks")
            .WithSummary("Gets several decks of the user as one ZIP file, with one file for each deck.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPost("/import", DeckExchangeEndpoints.ImportAsync)
            .WithName("ImportDeck")
            .WithSummary("Creates a deck from a file of another tool. The API makes no deck when a line has an error. See docs/development/deck-import-export.md.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/import-link", DeckExchangeEndpoints.ImportLinkAsync)
            .WithName("ImportDeckFromLink")
            .WithSummary("Creates a deck from the link of a public deck on Moxfield or Archidekt. The API makes no deck when a line has an error. Moxfield needs the setting Decks:LinkImport:MoxfieldUserAgent.")
            .RequireScope(Scopes.DecksWrite);

        decks.MapGet("/{deckId:guid}", GetAsync)
            .WithName("GetDeck")
            .WithSummary("Gets a deck with its lines and the data of its cards. The owner gets each deck. The other users and the visitors get the unlisted and public decks.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPatch("/{deckId:guid}", UpdateAsync)
            .WithName("UpdateDeck")
            .WithSummary("Changes the name, the format, the visibility, the folder or the description of a deck.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapDelete("/{deckId:guid}", DeleteAsync)
            .WithName("DeleteDeck")
            .WithSummary("Deletes a deck and its lines.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/{deckId:guid}/lines", DeckLineEndpoints.AddLinesAsync)
            .WithName("AddDeckLines")
            .WithSummary("Adds cards to a deck. A line with the same card, printing and finish in the same zone gets the quantity added.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPut("/{deckId:guid}/lines", DeckLineEndpoints.ReplaceLinesAsync)
            .WithName("ReplaceDeckLines")
            .WithSummary("Replaces all the lines of a deck.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPatch("/{deckId:guid}/lines/{lineId:guid}", DeckLineEndpoints.UpdateLineAsync)
            .WithName("UpdateDeckLine")
            .WithSummary("Changes the quantity, the zone, the printing, the finish or the note of a line. A quantity of 0 removes the line.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/{deckId:guid}/lines/group", DeckLineEndpoints.ApplyLineGroupActionAsync)
            .WithName("ApplyDeckLineGroupAction")
            .WithSummary("Applies one action to several lines of a deck in one change: move, tag, untag, printing, quantity or remove. Lines that get the same zone, card, printing and finish become one line.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/{deckId:guid}/copy", CopyAsync)
            .WithName("CopyDeck")
            .WithSummary("Copies a deck into a new deck of the user: one of their decks, or an unlisted or public deck of another user.")
            .RequireScope(Scopes.DecksWrite);

        decks.MapGet("/{deckId:guid}/legality", DeckCheckEndpoints.GetLegalityAsync)
            .WithName("GetDeckLegality")
            .WithSummary("Checks the legality of a deck in its format: deck size, copies, banned cards, commanders and color identity.")
            .RequireScope(Scopes.DecksRead);

        decks.MapGet("/{deckId:guid}/export", DeckExchangeEndpoints.ExportAsync)
            .WithName("ExportDeck")
            .WithSummary("Gets a deck as a file of another tool: text, moxfield, mtga, mtgo, dek or archidekt.")
            .RequireScope(Scopes.DecksRead);

        decks.MapGet("/{deckId:guid}/analysis", DeckCheckEndpoints.GetAnalysisAsync)
            .WithName("GetDeckAnalysis")
            .WithSummary("Analyzes a deck: mana curve, distributions, mana sources, draw probabilities, combos of Commander Spellbook, and for Commander the category needs and the indicative bracket.")
            .RequireScope(Scopes.DecksRead);

        decks.MapGet("/{deckId:guid}/text", DeckExchangeEndpoints.GetTextAsync)
            .WithName("GetDeckText")
            .WithSummary("Gets the lines of a deck in the deck text format. See docs/development/deck-text-format.md.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPut("/{deckId:guid}/text", DeckExchangeEndpoints.SetTextAsync)
            .WithName("SetDeckText")
            .WithSummary("Replaces the lines of a deck from a text in the deck text format. The API changes nothing when a line has an error.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapDelete("/{deckId:guid}/lines/{lineId:guid}", DeckLineEndpoints.DeleteLineAsync)
            .WithName("DeleteDeckLine")
            .WithSummary("Removes a line from a deck.")
            .RequireScope(Scopes.DecksWrite);
    }

    private static async Task<Results<Ok<Page<DeckSummaryResponse>>, ProblemHttpResult>> ListAsync(
        ClaimsPrincipal user,
        DeckService decks,
        int? limit,
        string? cursor,
        string? folderId,
        string? format,
        string? colors,
        string? tag,
        string? archived,
        string? sort,
        CancellationToken cancellationToken)
    {
        if (!PageRequest.TryCreate(limit, cursor, out var page, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        var offset = 0;
        if (page.Cursor is { } value)
        {
            if (!Cursors.TryDecode<DeckListCursor>(value, out var position) || position!.Offset < 0)
            {
                return ApiProblems.Validation(Cursors.InvalidCursor());
            }

            offset = position.Offset;
        }

        var errors = new List<FieldError>();
        Guid? folder = null;
        if (folderId is not null && folderId != "none")
        {
            if (Guid.TryParse(folderId, out var parsed))
            {
                folder = parsed;
            }
            else
            {
                errors.Add(new FieldError("folderId", FieldErrorCodes.InvalidFormat, "Give the ID of a folder, or none for the decks with no folder."));
            }
        }

        var colorIdentity = colors is null ? null : ReadColors(colors);
        if (colors is not null && colorIdentity is null)
        {
            errors.Add(new FieldError("colors", FieldErrorCodes.InvalidFormat, "Give the letters W, U, B, R and G, or C for colorless."));
        }

        AddChoiceError(errors, "archived", archived, ArchivedChoices);
        AddChoiceError(errors, "sort", sort, SortChoices);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var query = new DeckListQuery(folder, folderId == "none", format, colorIdentity, tag, archived ?? "exclude", sort ?? "updated", offset, page.Limit + 1);
        var rows = await decks.ListAsync(user.GetAccountId(), query, cancellationToken);
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
            Utc(row.UpdatedAt),
            row.Tags,
            row.ColorIdentity)).ToList();
        var next = rows.Count > page.Limit ? Cursors.Encode(new DeckListCursor(offset + page.Limit)) : null;
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
        Guid deckId, UpdateDeckRequest request, ClaimsPrincipal user, DeckService decks, Versions.AssembledDeckService assembled, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new FieldError("name", FieldErrorCodes.TooShort, "The name of the deck must contain a character that is not a space."));
        }

        ValidateDeckFields(errors, request.Name?.Trim(), request.Format, request.Visibility, request.Description);
        if (request.Tags is { } deckTags)
        {
            if (DeckRules.NormalizeDeckTags(deckTags).Length > DeckRules.MaxDeckTags)
            {
                errors.Add(new FieldError("tags", FieldErrorCodes.TooLong, $"A deck can have {DeckRules.MaxDeckTags} deck tags or fewer."));
            }

            for (var index = 0; index < deckTags.Count; index++)
            {
                if (deckTags[index] is { Length: > DeckRules.MaxLabelLength })
                {
                    errors.Add(new FieldError($"tags[{index}]", FieldErrorCodes.TooLong, $"A deck tag must have {DeckRules.MaxLabelLength} characters or fewer."));
                }
            }
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var view = await decks.UpdateAsync(
            user.GetAccountId(),
            deckId,
            new DeckChanges(request.Name, request.Format, request.Visibility, request.FolderId, request.ClearFolder == true, request.Description, request.Pinned, request.Archived, request.ExpectedVersion, request.Tags),
            cancellationToken);
        if (request.Name is not null)
        {
            await assembled.RenameLocationAsync(view.Deck, cancellationToken);
        }

        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static async Task<NoContent> DeleteAsync(Guid deckId, ClaimsPrincipal user, DeckService decks, CancellationToken cancellationToken)
    {
        await decks.DeleteAsync(user.GetAccountId(), deckId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<DeckResponse>, ProblemHttpResult>> CopyAsync(
        Guid deckId, CopyDeckRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new FieldError("name", FieldErrorCodes.TooShort, "The name of the deck must contain a character that is not a space."));
        }

        ValidateDeckFields(errors, request.Name?.Trim(), null, request.Visibility, null);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var copy = await decks.CopyAsync(user.GetAccountId(), deckId, request.Name?.Trim(), request.Visibility ?? "private", request.FolderId, cancellationToken);
        var view = await decks.GetAsync(user.GetAccountId(), copy.Id, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/decks/{copy.Id}", await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static readonly string[] ArchivedChoices = ["exclude", "include", "only"];

    private static readonly string[] SortChoices = ["updated", "created", "name", "format"];

    private sealed record DeckListCursor(int Offset);
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
        catch (DeckTextException exception)
        {
            return ApiProblems.Problem(
                StatusCodes.Status400BadRequest,
                DeckErrorCodes.TextInvalid,
                exception.Message,
                new Dictionary<string, object?> { ["lineErrors"] = exception.LineErrors.Select(error => new DeckTextLineError(error.Line, error.Code, error.Text)).ToList() });
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

