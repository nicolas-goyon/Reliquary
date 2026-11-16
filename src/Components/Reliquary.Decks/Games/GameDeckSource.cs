using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Decks;
using Reliquary.Decks.Text;

namespace Reliquary.Decks.Games;

/// <summary>
/// Gives the decks of the online games to the Playtest component. See docs/decisions/0047-trusted-server-for-online-games.md.
/// </summary>
internal sealed class GameDeckSource(DeckService decks, DeckTextService texts, ICardCatalog catalog) : IGameDeckSource
{
    /// <summary>The zone of the deck that gives the library.</summary>
    private const string LibraryZone = "main";

    /// <summary>The zones of the deck that give the command zone.</summary>
    private static readonly string[] CommandZones = ["commander", "companion"];

    /// <inheritdoc />
    public async Task<GameDeck?> GetDeckAsync(Guid deckId, Guid? viewerId, CancellationToken cancellationToken = default)
    {
        DeckView view;
        try
        {
            view = await decks.GetAsync(viewerId, deckId, cancellationToken);
        }
        catch (DeckException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }

        var lines = view.Lines.Select(line => new GameLine(line.OracleId, line.PrintingId, line.Zone, line.Quantity));
        return await ToGameDeckAsync(view.Deck.Name, view.Deck.Format, lines, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GameDeck> ParseDeckTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<LineInput> inputs;
        try
        {
            inputs = await texts.ReadAsync(text, cancellationToken);
        }
        catch (DeckTextException exception)
        {
            throw new GameDeckTextException([.. exception.LineErrors.Select(error => new GameDeckTextError(error.Line, error.Code, error.Text))]);
        }

        var lines = inputs.Select(input => new GameLine(input.OracleId, input.PrintingId, input.Zone, input.Quantity));
        return await ToGameDeckAsync(string.Empty, null, lines, cancellationToken);
    }

    private async Task<GameDeck> ToGameDeckAsync(string name, string? format, IEnumerable<GameLine> allLines, CancellationToken cancellationToken)
    {
        var lines = allLines.Where(line => line.Zone == LibraryZone || CommandZones.Contains(line.Zone)).ToList();
        var cards = await catalog.GetCardsAsync([.. lines.Select(line => line.OracleId).Distinct()], cancellationToken);
        var printingIds = lines.Select(line => line.PrintingId)
            .Concat(cards.Values.Select(card => card.DefaultPrintingId))
            .OfType<Guid>().Distinct().ToList();
        var printings = await catalog.GetPrintingsAsync(printingIds, cancellationToken);

        GameDeckCard ToCard(GameLine line)
        {
            var card = cards.GetValueOrDefault(line.OracleId);
            var printingId = line.PrintingId ?? card?.DefaultPrintingId;
            var imageUri = printingId is { } id && printings.TryGetValue(id, out var printing) ? printing.ImageUri : null;
            return new GameDeckCard(
                line.OracleId,
                card?.Name ?? line.OracleId.ToString(),
                imageUri,
                card?.Analysis.IsLand ?? false,
                card?.ManaValue ?? 0,
                line.Quantity);
        }

        return new GameDeck(
            name,
            format,
            [.. lines.Where(line => line.Zone == LibraryZone).Select(ToCard)],
            [.. lines.Where(line => CommandZones.Contains(line.Zone)).Select(ToCard)]);
    }

    private sealed record GameLine(Guid OracleId, Guid? PrintingId, string Zone, int Quantity);
}
