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
        var cards = await catalog.GetCardsAsync([.. view.Lines.Select(line => line.OracleId).Distinct()], cancellationToken);
        var printings = await catalog.GetPrintingsAsync([.. view.Lines.Where(line => line.PrintingId is not null).Select(line => line.PrintingId!.Value).Distinct()], cancellationToken);
        var tags = view.Tags.ToDictionary(tag => tag.Id);
        var lines = view.Lines.Select(line =>
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
        });
        return (DeckText.Write(lines), view.Deck.Version);
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
    public async Task<IReadOnlyList<LineInput>> ReadAsync(string text, CancellationToken cancellationToken)
    {
        var parsed = DeckText.Parse(text);
        var sourceLines = text.Split('\n');
        var errors = parsed.Errors.Select(error => (error.LineNumber, error.Code, error.Text)).ToList();
        var names = await catalog.ResolveNamesAsync([.. parsed.Lines.Select(line => line.Name).Distinct(StringComparer.Ordinal)], cancellationToken);
        var references = parsed.Lines.Where(line => line.SetCode is not null && line.CollectorNumber is not null)
            .Select(line => new PrintingReference(line.SetCode!, line.CollectorNumber!)).Distinct().ToList();
        var printings = await catalog.FindPrintingsAsync(references, cancellationToken);
        var inputs = new List<LineInput>();
        foreach (var line in parsed.Lines)
        {
            var lineText = sourceLines[line.LineNumber - 1].TrimEnd('\r');
            if (names.GetValueOrDefault(line.Name)?.OracleId is not { } oracleId)
            {
                errors.Add((line.LineNumber, CardNotFound, lineText));
                continue;
            }

            Guid? printingId = null;
            if (line.SetCode is not null && line.CollectorNumber is not null)
            {
                if (!printings.TryGetValue(new PrintingReference(line.SetCode, line.CollectorNumber), out var printing) || printing.OracleId != oracleId)
                {
                    errors.Add((line.LineNumber, PrintingNotFound, lineText));
                    continue;
                }

                printingId = printing.PrintingId;
            }

            inputs.Add(new LineInput(oracleId, printingId, line.Finish, line.Quantity, line.Zone, line.Note, line.Tags, line.GlobalTags, line.Categories));
        }

        if (errors.Count > 0)
        {
            throw new DeckTextException([.. errors.OrderBy(error => error.Item1)]);
        }

        return inputs;
    }
}
