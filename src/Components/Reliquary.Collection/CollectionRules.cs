using Reliquary.BuildingBlocks.Collection;

namespace Reliquary.Collection;

/// <summary>
/// The fixed values of the collection model. See docs/decisions/0034-collection-model.md.
/// </summary>
public static class CollectionRules
{
    /// <summary>The kinds of locations that a user can create. Only the Decks component creates a location of the kind <c>deck</c>.</summary>
    public static readonly IReadOnlyList<string> UserKinds = [LocationKinds.Rack, LocationKinds.Binder, LocationKinds.Box, LocationKinds.Cube];

    /// <summary>The finishes of a card.</summary>
    public static readonly IReadOnlyList<string> Finishes = ["nonfoil", "foil", "etched"];

    /// <summary>The conditions of a card, from the best to the worst. See <see cref="CardConditions"/>.</summary>
    public static readonly IReadOnlyList<string> Conditions = CardConditions.All;

    /// <summary>The language codes of the printings, as Scryfall gives them.</summary>
    public static readonly IReadOnlyList<string> Languages =
        ["en", "es", "fr", "de", "it", "pt", "ja", "ko", "ru", "zhs", "zht", "he", "la", "grc", "ar", "sa", "ph", "qya"];

    /// <summary>The advanced fields that the owner can enable, with their names in the API.</summary>
    public static readonly IReadOnlyList<string> AdvancedFields = [Fields.Finish, Fields.Condition, Fields.PurchasePrice, Fields.AcquiredOn, Fields.Note];

    /// <summary>The origins of a movement: a location, or a place outside the collection.</summary>
    public static readonly IReadOnlyList<string> Origins = [Endpoints.Location, Endpoints.Purchase, Endpoints.Gift, Endpoints.Trade, Endpoints.Nowhere];

    /// <summary>The destinations of a movement: a location, or a place outside the collection.</summary>
    public static readonly IReadOnlyList<string> Destinations = [Endpoints.Location, Endpoints.Sale, Endpoints.Gift, Endpoints.Trade, Endpoints.Nowhere];

    /// <summary>The maximum length of the name of a location.</summary>
    public const int MaxNameLength = 100;

    /// <summary>The maximum length of a note.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>The maximum number of copies in one entry.</summary>
    public const int MaxQuantity = 100_000;

    /// <summary>The maximum number of cards in one request.</summary>
    public const int MaxCardsPerRequest = 1000;

    /// <summary>The maximum number of characters in a file of an import.</summary>
    public const int MaxImportLength = 4_000_000;

    /// <summary>The maximum number of card lines in a file of an import.</summary>
    public const int MaxImportCards = 20_000;

    /// <summary>The maximum number of locations of an owner, with the sections.</summary>
    public const int MaxLocations = 1000;

    /// <summary>The maximum purchase price.</summary>
    public const decimal MaxPrice = 9_999_999_999.99m;

    /// <summary>The names of the advanced fields in the API and in the settings.</summary>
    public static class Fields
    {
        /// <summary>The finish: <c>nonfoil</c>, <c>foil</c> or <c>etched</c>.</summary>
        public const string Finish = "finish";

        /// <summary>The condition of the card.</summary>
        public const string Condition = "condition";

        /// <summary>The purchase price and its currency.</summary>
        public const string PurchasePrice = "purchasePrice";

        /// <summary>The date when the owner got the card.</summary>
        public const string AcquiredOn = "acquiredOn";

        /// <summary>A note of the owner.</summary>
        public const string Note = "note";
    }

    /// <summary>The kinds of the origin and the destination of a movement.</summary>
    public static class Endpoints
    {
        /// <summary>A location of the collection.</summary>
        public const string Location = "location";

        /// <summary>A purchase: the cards come into the collection.</summary>
        public const string Purchase = "purchase";

        /// <summary>A sale: the cards leave the collection.</summary>
        public const string Sale = "sale";

        /// <summary>A gift, received or given.</summary>
        public const string Gift = "gift";

        /// <summary>A trade with another person.</summary>
        public const string Trade = "trade";

        /// <summary>No known origin or destination, for example a correction.</summary>
        public const string Nowhere = "nowhere";
    }
}

/// <summary>
/// The error codes of the Collection endpoints.
/// </summary>
public static class CollectionErrorCodes
{
    /// <summary>The location is locked. Unlock it before a change of its content.</summary>
    public const string LocationLocked = LocationLockedException.Code;

    /// <summary>The location or one of its sections has cards. Remove or move the cards first.</summary>
    public const string LocationNotEmpty = "location_not_empty";

    /// <summary>The location belongs to an assembled deck. Only a change of the deck can change it.</summary>
    public const string DeckLocation = "deck_location";

    /// <summary>The origin location does not have enough copies of a card.</summary>
    public const string NotEnoughCards = "not_enough_cards";

    /// <summary>The inventory is applied. It cannot change.</summary>
    public const string InventoryApplied = "inventory_applied";
}

/// <summary>
/// The field error codes of the Collection endpoints.
/// </summary>
public static class CollectionFieldErrorCodes
{
    /// <summary>No card has the name of the entry.</summary>
    public const string CardNotFound = "card_not_found";
}
