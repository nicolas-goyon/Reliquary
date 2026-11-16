using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Decks.Api;

/// <summary>
/// Makes the responses of the deck endpoints from the decks and the data of their cards.
/// </summary>
internal static class DeckResponses
{
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
            [.. view.Lines.Select(line => new DeckLineResponse(
                line.Id, line.OracleId, line.PrintingId, line.Finish, line.Quantity, line.Zone, line.Note, line.TagIds, line.Categories, Purchases.PurchasePreferences.ReadOrNull(line.PurchasePreferences)))],
            cards,
            printings,
            view.Tags.ToDictionary(tag => tag.Id, tag => new DeckTagResponse(tag.Name, tag.IsGlobal)),
            view.IsOwner ? deck.Tags : [],
            deck.ColorIdentity,
            deck.VariantOf,
            deck.BaseVersionId,
            deck.HeadVersionId,
            deck.AssembledVersionId,
            view.Hidden,
            deck.Archetype);
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
}
