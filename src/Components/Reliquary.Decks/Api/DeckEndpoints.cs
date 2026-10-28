using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Data;
using Reliquary.Decks.Exchange;
using Reliquary.Decks.Text;

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
            .WithSummary("Lists the decks of the user with filters by folder, format, color identity, deck tag and archive. The pinned decks come first, then the sort applies.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPost(string.Empty, CreateAsync)
            .WithName("CreateDeck")
            .WithSummary("Creates a deck, with its first lines.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapGet("/tags", ListDeckTagsAsync)
            .WithName("ListDeckTags")
            .WithSummary("Lists the deck tags of the user, with the number of decks of each tag.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPost("/group", ApplyGroupActionAsync)
            .WithName("ApplyDeckGroupAction")
            .WithSummary("Applies one action to several decks of the user: move, tag, untag, pin, unpin, archive, unarchive or delete.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/export", ExportDecksAsync)
            .WithName("ExportDecks")
            .WithSummary("Gets several decks of the user as one ZIP file, with one file for each deck.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPost("/import", ImportAsync)
            .WithName("ImportDeck")
            .WithSummary("Creates a deck from a file of another tool. The API makes no deck when a line has an error. See docs/development/deck-import-export.md.")
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

        owner.MapPost("/{deckId:guid}/lines", AddLinesAsync)
            .WithName("AddDeckLines")
            .WithSummary("Adds cards to a deck. A line with the same card, printing and finish in the same zone gets the quantity added.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPut("/{deckId:guid}/lines", ReplaceLinesAsync)
            .WithName("ReplaceDeckLines")
            .WithSummary("Replaces all the lines of a deck.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPatch("/{deckId:guid}/lines/{lineId:guid}", UpdateLineAsync)
            .WithName("UpdateDeckLine")
            .WithSummary("Changes the quantity, the zone, the printing, the finish or the note of a line. A quantity of 0 removes the line.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/{deckId:guid}/lines/group", ApplyLineGroupActionAsync)
            .WithName("ApplyDeckLineGroupAction")
            .WithSummary("Applies one action to several lines of a deck in one change: move, tag, untag, printing, quantity or remove. Lines that get the same zone, card, printing and finish become one line.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapPost("/{deckId:guid}/copy", CopyAsync)
            .WithName("CopyDeck")
            .WithSummary("Copies a deck into a new deck of the user: one of their decks, or an unlisted or public deck of another user.")
            .RequireScope(Scopes.DecksWrite);

        decks.MapGet("/{deckId:guid}/legality", GetLegalityAsync)
            .WithName("GetDeckLegality")
            .WithSummary("Checks the legality of a deck in its format: deck size, copies, banned cards, commanders and color identity.")
            .RequireScope(Scopes.DecksRead);

        decks.MapGet("/{deckId:guid}/export", ExportAsync)
            .WithName("ExportDeck")
            .WithSummary("Gets a deck as a file of another tool: text, moxfield, mtga, mtgo, dek or archidekt.")
            .RequireScope(Scopes.DecksRead);

        decks.MapGet("/{deckId:guid}/analysis", GetAnalysisAsync)
            .WithName("GetDeckAnalysis")
            .WithSummary("Analyzes a deck: mana curve, distributions, mana sources, draw probabilities, combos of Commander Spellbook, and for Commander the category needs and the indicative bracket.")
            .RequireScope(Scopes.DecksRead);

        decks.MapGet("/{deckId:guid}/text", GetTextAsync)
            .WithName("GetDeckText")
            .WithSummary("Gets the lines of a deck in the deck text format. See docs/development/deck-text-format.md.")
            .RequireScope(Scopes.DecksRead);

        owner.MapPut("/{deckId:guid}/text", SetTextAsync)
            .WithName("SetDeckText")
            .WithSummary("Replaces the lines of a deck from a text in the deck text format. The API changes nothing when a line has an error.")
            .RequireScope(Scopes.DecksWrite);

        owner.MapDelete("/{deckId:guid}/lines/{lineId:guid}", DeleteLineAsync)
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

    private static async Task<Ok<IReadOnlyList<DeckTagSummaryResponse>>> ListDeckTagsAsync(ClaimsPrincipal user, DeckService decks, CancellationToken cancellationToken)
    {
        var tags = await decks.ListDeckTagsAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<DeckTagSummaryResponse>>([.. tags.Select(tag => new DeckTagSummaryResponse(tag.Name, tag.DeckCount))]);
    }

    private static async Task<Results<Ok<DeckGroupResponse>, ProblemHttpResult>> ApplyGroupActionAsync(
        DeckGroupRequest request, ClaimsPrincipal user, DeckService decks, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var deckIds = ValidateDeckIds(errors, request.DeckIds);
        if (string.IsNullOrEmpty(request.Action))
        {
            errors.Add(new FieldError("action", FieldErrorCodes.Required, "The action is necessary."));
        }
        else
        {
            AddChoiceError(errors, "action", request.Action, GroupActions);
        }

        var tag = request.Tag?.Trim();
        if (request.Action is "tag" or "untag")
        {
            ValidateDeckTag(errors, "tag", tag);
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        if (request.Action == "delete")
        {
            foreach (var deckId in deckIds)
            {
                await decks.DeleteAsync(user.GetAccountId(), deckId, cancellationToken);
            }

            return TypedResults.Ok(new DeckGroupResponse(deckIds.Count));
        }

        var count = await decks.ApplyGroupActionAsync(user.GetAccountId(), deckIds, new DeckGroupAction(request.Action!, request.FolderId, tag), cancellationToken);
        return TypedResults.Ok(new DeckGroupResponse(count));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportDecksAsync(
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

    private static HashSet<Guid> ValidateDeckIds(List<FieldError> errors, IReadOnlyList<Guid>? deckIds)
    {
        if (deckIds is not { Count: > 0 })
        {
            errors.Add(new FieldError("deckIds", FieldErrorCodes.Required, "Give one deck or more."));
            return [];
        }

        if (deckIds.Count > MaxGroupDecks)
        {
            errors.Add(new FieldError("deckIds", FieldErrorCodes.TooLong, $"Give {MaxGroupDecks} decks or fewer."));
        }

        return [.. deckIds];
    }

    private static void ValidateDeckTag(List<FieldError> errors, string field, string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            errors.Add(new FieldError(field, FieldErrorCodes.Required, "The deck tag is necessary."));
        }
        else if (tag.Length > DeckRules.MaxLabelLength)
        {
            errors.Add(new FieldError(field, FieldErrorCodes.TooLong, $"A deck tag must have {DeckRules.MaxLabelLength} characters or fewer."));
        }
    }

    private static void AddChoiceError(List<FieldError> errors, string field, string? value, IReadOnlyList<string> choices)
    {
        if (value is not null && !choices.Contains(value))
        {
            errors.Add(new FieldError(field, FieldErrorCodes.NotAllowed, $"Use one of these values: {string.Join(", ", choices)}."));
        }
    }

    /// <summary>
    /// Reads a color identity: the letters W, U, B, R and G in any order and case, or C for colorless.
    /// </summary>
    /// <returns>The letters in the order WUBRG, an empty text for colorless, or <see langword="null"/> for a text that is not valid.</returns>
    internal static string? ReadColors(string colors)
    {
        var letters = colors.Trim().ToUpperInvariant();
        if (letters == "C")
        {
            return string.Empty;
        }

        return letters.Length > 0 && letters.All(DeckRules.ColorOrder.Contains) && letters.Distinct().Count() == letters.Length
            ? string.Concat(DeckRules.ColorOrder.Where(letters.Contains))
            : null;
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
        ValidateTags(errors, string.Empty, request.Tags, request.GlobalTags, request.Categories);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var view = await decks.UpdateLineAsync(
            user.GetAccountId(),
            deckId,
            lineId,
            new LineChanges(
                request.Quantity,
                request.Zone,
                request.PrintingId,
                request.ClearPrinting == true,
                request.Finish,
                request.ClearFinish == true,
                request.Note,
                Clean(request.Tags),
                Clean(request.GlobalTags),
                request.Categories),
            cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static async Task<Results<Ok<DeckResponse>, ProblemHttpResult>> ApplyLineGroupActionAsync(
        Guid deckId, LineGroupRequest request, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.LineIds is not { Count: > 0 })
        {
            errors.Add(new FieldError("lineIds", FieldErrorCodes.Required, "Give one line or more."));
        }
        else if (request.LineIds.Count > DeckRules.MaxLines)
        {
            errors.Add(new FieldError("lineIds", FieldErrorCodes.TooLong, $"Give {DeckRules.MaxLines} lines or fewer."));
        }

        if (string.IsNullOrEmpty(request.Action))
        {
            errors.Add(new FieldError("action", FieldErrorCodes.Required, "The action is necessary."));
        }
        else
        {
            AddChoiceError(errors, "action", request.Action, LineGroupActions);
        }

        ValidateLineFields(errors, string.Empty, request.Zone, request.Finish, null);
        switch (request.Action)
        {
            case "move" when request.Zone is null:
                errors.Add(new FieldError("zone", FieldErrorCodes.Required, "The zone is necessary for the action move."));
                break;
            case "tag" or "untag":
                ValidateTags(errors, string.Empty, request.Tags, request.GlobalTags, request.Categories);
                if ((request.Tags?.Count ?? 0) + (request.GlobalTags?.Count ?? 0) + (request.Categories?.Count ?? 0) == 0)
                {
                    errors.Add(new FieldError("tags", FieldErrorCodes.Required, "Give one tag, global tag or category or more."));
                }

                break;
            case "printing" when request.ClearPrinting != true && request.Finish is null:
                errors.Add(new FieldError("finish", FieldErrorCodes.Required, "Give a finish, or set clearPrinting to true."));
                break;
            case "quantity" when request.QuantityDelta is null or 0:
                errors.Add(new FieldError("quantityDelta", FieldErrorCodes.Required, "Give a number of cards that is not 0."));
                break;
            case "quantity" when request.QuantityDelta is < -DeckRules.MaxQuantity or > DeckRules.MaxQuantity:
                errors.Add(new FieldError("quantityDelta", FieldErrorCodes.OutOfRange, $"The number of cards must be from -{DeckRules.MaxQuantity} to {DeckRules.MaxQuantity}."));
                break;
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var action = new LineGroupAction(
            request.Action!,
            request.Zone,
            Clean(request.Tags) ?? [],
            Clean(request.GlobalTags) ?? [],
            [.. (request.Categories ?? []).Distinct()],
            request.QuantityDelta ?? 0,
            request.Finish,
            request.ClearPrinting == true);
        var view = await decks.ApplyLineGroupActionAsync(user.GetAccountId(), deckId, request.LineIds!.ToHashSet(), action, request.ExpectedVersion, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
    }

    private static async Task<Ok<DeckResponse>> DeleteLineAsync(Guid deckId, Guid lineId, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var view = await decks.DeleteLineAsync(user.GetAccountId(), deckId, lineId, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(view, catalog, cancellationToken));
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

    private static async Task<Ok<DeckLegalityResponse>> GetLegalityAsync(Guid deckId, ClaimsPrincipal user, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(user.FindAccountId(), deckId, cancellationToken);
        var cards = await catalog.GetCardsAsync([.. view.Lines.Select(line => line.OracleId).Distinct()], cancellationToken);
        var problems = Legality.DeckLegality.Check(view.Deck.Format, view.Lines.Select(line => (line.Zone, line.OracleId, line.Quantity)), cards);
        return TypedResults.Ok(new DeckLegalityResponse(view.Deck.Format, problems.Count == 0, problems));
    }

    private static async Task<Results<Created<ImportDeckResponse>, ProblemHttpResult>> ImportAsync(
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

    private static async Task<Results<Ok<ExportDeckResponse>, ProblemHttpResult>> ExportAsync(Guid deckId, string? format, ClaimsPrincipal user, DeckExchangeService exchange, CancellationToken cancellationToken)
    {
        if (format is null || !DeckFormats.All.TryGetValue(format, out var deckFormat))
        {
            return ApiProblems.Validation(new FieldError("format", FieldErrorCodes.NotAllowed, $"Give one of these file formats: {string.Join(", ", DeckFormats.All.Keys)}."));
        }

        var file = await exchange.ExportAsync(user.FindAccountId(), deckId, deckFormat, cancellationToken);
        return TypedResults.Ok(new ExportDeckResponse(file.FileName, file.ContentType, file.Content));
    }

    private static async Task<Ok<Analysis.DeckAnalysis>> GetAnalysisAsync(
        Guid deckId,
        ClaimsPrincipal user,
        DeckService decks,
        ICardCatalog catalog,
        IComboCatalog combos,
        CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(user.FindAccountId(), deckId, cancellationToken);
        var deckCards = view.Lines.Where(line => Analysis.DeckAnalyzer.Zones.Contains(line.Zone)).Select(line => line.OracleId).Distinct().ToList();
        var search = await combos.FindCombosAsync(deckCards, cancellationToken);

        // The facts of the cards of the combos give the names of the missing cards.
        var oracleIds = view.Lines.Select(line => line.OracleId).Concat(search.Combos.SelectMany(combo => combo.Cards).Select(card => card.OracleId)).Distinct().ToList();
        var cards = await catalog.GetCardsAsync(oracleIds, cancellationToken);
        var lines = view.Lines.Select(line => new Analysis.AnalysisLine(line.Zone, line.OracleId, line.Quantity, line.Categories));
        return TypedResults.Ok(Analysis.DeckAnalyzer.Analyze(view.Deck.Format, lines, cards, search.Available ? search : null));
    }

    private static async Task<Ok<DeckTextResponse>> GetTextAsync(Guid deckId, ClaimsPrincipal user, DeckTextService texts, CancellationToken cancellationToken)
    {
        var (text, version) = await texts.GetAsync(user.FindAccountId(), deckId, cancellationToken);
        return TypedResults.Ok(new DeckTextResponse(text, version));
    }

    private static async Task<Results<Ok<DeckResponse>, NoContent, ProblemHttpResult>> SetTextAsync(
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

    private const int MaxTextLength = 200_000;


    private const int MaxColumnHeaderLength = 100;

    private const int MaxGroupDecks = 500;

    private static readonly string[] ArchivedChoices = ["exclude", "include", "only"];

    private static readonly string[] SortChoices = ["updated", "created", "name", "format"];

    private static readonly string[] GroupActions = ["move", "tag", "untag", "pin", "unpin", "archive", "unarchive", "delete"];

    private static readonly string[] LineGroupActions = ["move", "tag", "untag", "printing", "quantity", "remove"];

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

    private static void ValidateTags(List<FieldError> errors, string prefix, IReadOnlyList<string>? tags, IReadOnlyList<string>? globalTags, IReadOnlyList<string>? categories)
    {
        foreach (var (field, list) in new[] { ("tags", tags), ("globalTags", globalTags) })
        {
            if (list is null)
            {
                continue;
            }

            if (list.Count > DeckRules.MaxTagsPerLine)
            {
                errors.Add(new FieldError($"{prefix}{field}", FieldErrorCodes.TooLong, $"A line can have {DeckRules.MaxTagsPerLine} tags or fewer."));
            }

            for (var index = 0; index < list.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(list[index]))
                {
                    errors.Add(new FieldError($"{prefix}{field}[{index}]", FieldErrorCodes.Required, "A tag must contain a character that is not a space."));
                }
                else if (list[index].Trim().Length > DeckRules.MaxTagLength || list[index].Contains('#', StringComparison.Ordinal))
                {
                    errors.Add(new FieldError($"{prefix}{field}[{index}]", FieldErrorCodes.InvalidFormat, $"A tag must have {DeckRules.MaxTagLength} characters or fewer, and no \"#\"."));
                }
            }
        }

        for (var index = 0; index < (categories?.Count ?? 0); index++)
        {
            if (!DeckRules.Categories.Contains(categories![index]))
            {
                errors.Add(new FieldError($"{prefix}categories[{index}]", FieldErrorCodes.NotAllowed, $"The category must be one of these categories: {string.Join(", ", DeckRules.Categories)}."));
            }
        }
    }

    private static List<string>? Clean(IReadOnlyList<string>? tags) =>
        tags?.Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
            ValidateTags(errors, prefix, line.Tags, line.GlobalTags, line.Categories);
            if (oracleId is { } id)
            {
                inputs.Add(new LineInput(
                    id,
                    line.PrintingId,
                    line.Finish,
                    line.Quantity ?? 1,
                    line.Zone ?? "main",
                    line.Note?.Trim() ?? string.Empty,
                    Clean(line.Tags) ?? [],
                    Clean(line.GlobalTags) ?? [],
                    line.Categories ?? []));
            }
        }

        return inputs;
    }

    /// <summary>
    /// Makes the response of a deck, with the data of its cards and printings.
    /// </summary>
    internal static async Task<DeckResponse> ToResponseAsync(DeckView view, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var (cards, printings) = await CardDataAsync(catalog, view.Lines.Select(line => line.OracleId), view.Lines.Select(line => line.PrintingId), cancellationToken);
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
            [.. view.Lines.Select(line => new DeckLineResponse(line.Id, line.OracleId, line.PrintingId, line.Finish, line.Quantity, line.Zone, line.Note, line.TagIds, line.Categories))],
            cards,
            printings,
            view.Tags.ToDictionary(tag => tag.Id, tag => new DeckTagResponse(tag.Name, tag.IsGlobal)),
            view.IsOwner ? deck.Tags : [],
            deck.ColorIdentity,
            deck.VariantOf,
            deck.BaseVersionId,
            deck.HeadVersionId,
            deck.AssembledVersionId,
            view.Hidden);
    }

    /// <summary>
    /// Gets the data of the cards and of the printings of some lines: the printings of the lines and the default printings of the cards.
    /// </summary>
    internal static async Task<(Dictionary<Guid, DeckCardResponse> Cards, Dictionary<Guid, DeckPrintingResponse> Printings)> CardDataAsync(
        ICardCatalog catalog, IEnumerable<Guid> oracleIds, IEnumerable<Guid?> printingIds, CancellationToken cancellationToken)
    {
        var cards = await catalog.GetCardsAsync([.. oracleIds.Distinct()], cancellationToken);
        var allPrintings = printingIds.OfType<Guid>()
            .Concat(cards.Values.Where(card => card.DefaultPrintingId is not null).Select(card => card.DefaultPrintingId!.Value))
            .Distinct().ToList();
        var printings = await catalog.GetPrintingsAsync(allPrintings, cancellationToken);
        return (
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

    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

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
