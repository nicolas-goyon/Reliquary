using Microsoft.Extensions.Logging;

namespace Reliquary.Collection;

/// <summary>
/// The log messages of the Collection component. The messages follow the strict mode of the writing rules.
/// </summary>
internal static partial class CollectionLog
{
    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "The Decks component released the location {LocationId} of the deck {DeckId}. {Cards} cards went to {Target}.")]
    public static partial void DeckLocationReleased(this ILogger logger, Guid locationId, Guid deckId, int cards, string target);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "The owner applied the inventory {InventoryId} of the location {LocationId}. The inventory wrote {Movements} movements.")]
    public static partial void InventoryApplied(this ILogger logger, Guid inventoryId, Guid locationId, int movements);
}
