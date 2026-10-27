using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Decks.Text;

/// <summary>
/// A deck text with lines that the API cannot use.
/// </summary>
internal sealed class DeckTextException(IReadOnlyList<(int Line, string Code, string Text)> errors)
    : Exception("The text has lines that the API cannot use. Correct the lines in the lineErrors list.")
{
    public IReadOnlyList<(int Line, string Code, string Text)> LineErrors { get; } = errors;
}

/// <summary>
/// Reads and writes the lines of a deck in the deck text format.
/// </summary>
internal sealed class DeckTextService(DeckService decks, ICardCatalog catalog)
{
    /// <summary>The reason code of a name that no card has.</summary>
    public const string CardNotFound = "card_not_found";

    /// <summary>The reason code of a set and collector number that give no printing of the card.</summary>
    public const string PrintingNotFound = "printing_not_found";

    public async Task<(string Text, int Version)> GetAsync(Guid? viewerId, Guid deckId, CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(viewerId, deckId, cancellationToken);
        return (DeckText.Write(await ToTextLinesAsync(view, cancellationToken)), view.Deck.Version);
    }

    /// <summary>
    /// Converts the lines of a deck into text lines, with the Oracle names of the cards, the printings and the tag names.
    /// </summary>
    public async Task<IReadOnlyList<DeckTextLine>> ToTextLinesAsync(DeckView view, CancellationToken cancellationToken)
    {
        var cards = await catalog.GetCardsAsync([.. view.Lines.Select(line => line.OracleId).Distinct()], cancellationToken);
        var printings = await catalog.GetPrintingsAsync([.. view.Lines.Where(line => line.PrintingId is not null).Select(line => line.PrintingId!.Value).Distinct()], cancellationToken);
        var tags = view.Tags.ToDictionary(tag => tag.Id);
        return [.. view.Lines.Select(line =>
        {
            var printing = line.PrintingId is { } id && printings.TryGetValue(id, out var found) ? found : null;
            var lineTags = line.TagIds.Where(tags.ContainsKey).Select(id => tags[id]).OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return new DeckTextLine(
                0,
                line.Quantity,
                cards.TryGetValue(line.OracleId, out var card) ? card.Name : line.OracleId.ToString(),
                printing?.SetCode,
                printing?.CollectorNumber,
                line.Finish is "foil" or "etched" ? line.Finish : null,
                [.. lineTags.Where(tag => !tag.IsGlobal).Select(tag => tag.Name)],
                [.. lineTags.Where(tag => tag.IsGlobal).Select(tag => tag.Name)],
                line.Note,
                line.Zone,
                [.. DeckRules.Categories.Where(line.Categories.Contains)]);
        })];
    }

    /// <summary>
    /// Reads a deck text and replaces the lines of the deck.
    /// </summary>
    /// <returns>The deck, or <see langword="null"/> for a dry run.</returns>
    /// <exception cref="DeckTextException">The text has lines that the API cannot use.</exception>
    public async Task<DeckView?> SetAsync(Guid ownerId, Guid deckId, string text, int? expectedVersion, bool dryRun, CancellationToken cancellationToken)
    {
        var inputs = await ReadAsync(text, cancellationToken);
        if (dryRun)
        {
            // The owner check of a dry run: the deck must exist and belong to the user.
            var view = await decks.GetAsync(ownerId, deckId, cancellationToken);
            return view.IsOwner ? null : throw DeckException.NotFound();
        }

        return await decks.ReplaceLinesAsync(ownerId, deckId, inputs, expectedVersion, cancellationToken);
    }

    /// <summary>
    /// Reads a deck text and finds the cards and the printings of its lines.
    /// </summary>
    /// <exception cref="DeckTextException">The text has lines that the API cannot use.</exception>
    public Task<IReadOnlyList<LineInput>> ReadAsync(string text, CancellationToken cancellationToken)
    {
        var sourceLines = text.Split('\n');
        return ResolveStrictAsync(DeckText.Parse(text), number => number <= sourceLines.Length ? sourceLines[number - 1].TrimEnd('\r') : string.Empty, cancellationToken);
    }

    private async Task<IReadOnlyList<LineInput>> ResolveStrictAsync(DeckTextResult parsed, Func<int, string> lineText, CancellationToken cancellationToken) =>
        (await ResolveAsync(parsed, lineText, keepUnknownPrintings: false, cancellationToken)).Lines;

    /// <summary>
    /// Finds the cards and the printings of parsed lines.
    /// </summary>
    /// <param name="parsed">The lines and the errors of a parser.</param>
    /// <param name="lineText">Gets the source text of a line number, for the errors.</param>
    /// <param name="keepUnknownPrintings">
    /// Whether a line with an unknown printing stays in the deck with no printing. The line then gives a warning, not an error.
    /// Other tools can use set codes that Scryfall does not use.
    /// </param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The lines for the deck, and the warnings.</returns>
    /// <exception cref="DeckTextException">A line has an error.</exception>
    public async Task<(IReadOnlyList<LineInput> Lines, IReadOnlyList<(int Line, string Code, string Text)> Warnings)> ResolveAsync(
        DeckTextResult parsed, Func<int, string> lineText, bool keepUnknownPrintings, CancellationToken cancellationToken)
    {
        var warnings = new List<(int Line, string Code, string Text)>();
        var errors = parsed.Errors.Select(error => (error.LineNumber, error.Code, error.Text)).ToList();
        var names = await catalog.ResolveNamesAsync([.. parsed.Lines.Select(line => line.Name).Distinct(StringComparer.Ordinal)], cancellationToken);
        var references = parsed.Lines.Where(line => line.SetCode is not null && line.CollectorNumber is not null)
            .Select(line => new PrintingReference(line.SetCode!, line.CollectorNumber!)).Distinct().ToList();
        var printings = await catalog.FindPrintingsAsync(references, cancellationToken);
        var inputs = new List<LineInput>();
        foreach (var line in parsed.Lines)
        {
            if (names.GetValueOrDefault(line.Name)?.OracleId is not { } oracleId)
            {
                errors.Add((line.LineNumber, CardNotFound, lineText(line.LineNumber)));
                continue;
            }

            Guid? printingId = null;
            if (line.SetCode is not null && line.CollectorNumber is not null)
            {
                if (printings.TryGetValue(new PrintingReference(line.SetCode, line.CollectorNumber), out var printing) && printing.OracleId == oracleId)
                {
                    printingId = printing.PrintingId;
                }
                else if (keepUnknownPrintings)
                {
                    warnings.Add((line.LineNumber, PrintingNotFound, lineText(line.LineNumber)));
                }
                else
                {
                    errors.Add((line.LineNumber, PrintingNotFound, lineText(line.LineNumber)));
                    continue;
                }
            }

            inputs.Add(new LineInput(oracleId, printingId, line.Finish, line.Quantity, line.Zone, line.Note, line.Tags, line.GlobalTags, line.Categories));
        }

        if (errors.Count > 0)
        {
            throw new DeckTextException([.. errors.OrderBy(error => error.Item1)]);
        }

        return (inputs, warnings);
    }
}
