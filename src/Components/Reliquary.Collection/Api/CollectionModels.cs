using System.Text.Json.Serialization;

namespace Reliquary.Collection.Api;

/// <summary>The body of a new location.</summary>
/// <param name="Name">The name of the location.</param>
/// <param name="Kind">The kind: <c>rack</c>, <c>binder</c>, <c>box</c> or <c>cube</c>. A section gets the kind of its parent location.</param>
/// <param name="ParentId">The parent location of a section, or <see langword="null"/> for a location.</param>
/// <param name="Pickable">Whether the cards of the location are available cards. The default is <see langword="true"/>.</param>
/// <param name="Locked">Whether the content of the location cannot change. The default is <see langword="false"/>.</param>
/// <param name="Position">The position in the list. The default is 0.</param>
public sealed record CreateLocationRequest(string? Name, string? Kind, Guid? ParentId, bool? Pickable, bool? Locked, int? Position);

/// <summary>The changes of a location. A missing field does not change.</summary>
/// <param name="Name">The name.</param>
/// <param name="Kind">The kind of a location. The sections get the same kind.</param>
/// <param name="Pickable">Whether the cards of the location are available cards.</param>
/// <param name="Position">The position in the list.</param>
public sealed record UpdateLocationRequest(string? Name, string? Kind, bool? Pickable, int? Position);

/// <summary>A location of the collection.</summary>
/// <param name="Id">The ID of the location.</param>
/// <param name="ParentId">The parent location of a section, or <see langword="null"/>.</param>
/// <param name="Name">The name.</param>
/// <param name="Kind">The kind: <c>rack</c>, <c>binder</c>, <c>box</c>, <c>deck</c> or <c>cube</c>.</param>
/// <param name="Locked">Whether the location is locked. The sections of a locked location are also locked, with no change of their own flag.</param>
/// <param name="Pickable">Whether the location is pickable. The cards of a section are available cards only when the section and its parent are pickable.</param>
/// <param name="DeckId">The deck of an assembled deck location, or <see langword="null"/>.</param>
/// <param name="Position">The position in the list.</param>
/// <param name="CardCount">The number of cards in the location, without its sections.</param>
/// <param name="TotalCardCount">The number of cards in the location and in its sections.</param>
/// <param name="CreatedAt">The date of creation.</param>
/// <param name="UpdatedAt">The date of the last change of the location.</param>
/// <param name="Sections">The sections of a location. A section has no sections.</param>
public sealed record LocationResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Kind,
    bool Locked,
    bool Pickable,
    Guid? DeckId,
    int Position,
    long CardCount,
    long TotalCardCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<LocationResponse> Sections);

/// <summary>A card of a request: a new entry, a card of a movement or a counted card of an inventory.</summary>
/// <param name="OracleId">The Oracle card. Give the card or its name.</param>
/// <param name="Name">A name of the card: Oracle name, face name, localized name or alternate name.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="Language">The language code. The default is the language of the printing, or <c>en</c>.</param>
/// <param name="Quantity">The number of copies, from 1 to 100000. The default is 1.</param>
/// <param name="Finish">The finish: <c>nonfoil</c>, <c>foil</c> or <c>etched</c>. An advanced field.</param>
/// <param name="Condition">The condition, for example <c>near_mint</c>. An advanced field.</param>
/// <param name="PurchasePrice">The purchase price of one copy. An advanced field, with <paramref name="PurchaseCurrency"/>.</param>
/// <param name="PurchaseCurrency">The currency of the purchase price, for example <c>EUR</c>.</param>
/// <param name="AcquiredOn">The date when the owner got the copies. An advanced field.</param>
/// <param name="Note">A note. An advanced field.</param>
public sealed record CardRequest(
    Guid? OracleId,
    string? Name,
    Guid? PrintingId,
    string? Language,
    int? Quantity,
    string? Finish = null,
    string? Condition = null,
    decimal? PurchasePrice = null,
    string? PurchaseCurrency = null,
    DateOnly? AcquiredOn = null,
    string? Note = null);

/// <summary>The cards to add to a location.</summary>
/// <param name="Entries">The cards. A card with the same fields as an entry of the location adds to its quantity.</param>
/// <param name="Origin">Where the cards come from: <c>purchase</c>, <c>gift</c>, <c>trade</c> or <c>nowhere</c>. The default is <c>nowhere</c>.</param>
public sealed record AddEntriesRequest(IReadOnlyList<CardRequest>? Entries, string? Origin);

/// <summary>The changes of an entry. A missing field does not change.</summary>
/// <param name="Quantity">The number of copies. 0 removes the entry.</param>
/// <param name="PrintingId">The printing.</param>
/// <param name="ClearPrinting">Set to <see langword="true"/> to remove the printing.</param>
/// <param name="Language">The language code.</param>
/// <param name="Finish">The finish. An empty text removes the finish.</param>
/// <param name="Condition">The condition. An empty text removes the condition.</param>
/// <param name="PurchasePrice">The purchase price, with <paramref name="PurchaseCurrency"/>.</param>
/// <param name="PurchaseCurrency">The currency of the purchase price.</param>
/// <param name="ClearPurchasePrice">Set to <see langword="true"/> to remove the purchase price.</param>
/// <param name="AcquiredOn">The date when the owner got the copies.</param>
/// <param name="ClearAcquiredOn">Set to <see langword="true"/> to remove the date.</param>
/// <param name="Note">The note. An empty text removes the note.</param>
public sealed record UpdateEntryRequest(
    int? Quantity,
    Guid? PrintingId = null,
    bool? ClearPrinting = null,
    string? Language = null,
    string? Finish = null,
    string? Condition = null,
    decimal? PurchasePrice = null,
    string? PurchaseCurrency = null,
    bool? ClearPurchasePrice = null,
    DateOnly? AcquiredOn = null,
    bool? ClearAcquiredOn = null,
    string? Note = null);

/// <summary>An entry of a location: copies of one card with the same fields.</summary>
/// <param name="Id">The ID of the entry.</param>
/// <param name="LocationId">The location or the section of the entry.</param>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The Oracle name of the card.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="SetCode">The set code of the printing, or <see langword="null"/>.</param>
/// <param name="CollectorNumber">The collector number of the printing, or <see langword="null"/>.</param>
/// <param name="ImageUri">The image of the printing, or of the default printing of the card.</param>
/// <param name="Language">The language code.</param>
/// <param name="Quantity">The number of copies.</param>
/// <param name="Finish">The finish. Only when the field is enabled and has a value.</param>
/// <param name="Condition">The condition. Only when the field is enabled and has a value.</param>
/// <param name="PurchasePrice">The purchase price. Only when the field is enabled and has a value.</param>
/// <param name="PurchaseCurrency">The currency of the purchase price.</param>
/// <param name="AcquiredOn">The date when the owner got the copies. Only when the field is enabled and has a value.</param>
/// <param name="Note">The note. Only when the field is enabled and has a value.</param>
public sealed record EntryResponse(
    Guid Id,
    Guid LocationId,
    Guid OracleId,
    string Name,
    Guid? PrintingId,
    string? SetCode,
    string? CollectorNumber,
    string? ImageUri,
    string Language,
    int Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Finish,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Condition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? PurchasePrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PurchaseCurrency,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? AcquiredOn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note);

/// <summary>The result of a change of the entries.</summary>
/// <param name="BatchId">The batch ID of the movements of the change.</param>
/// <param name="Entries">The entries that the change created or changed. An entry with no copies is not in the list.</param>
public sealed record EntriesChangeResponse(Guid BatchId, IReadOnlyList<EntryResponse> Entries);

/// <summary>One Oracle card of the global view, with its copies in each location.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The Oracle name.</param>
/// <param name="Quantity">The number of copies in all the locations of the view.</param>
/// <param name="ImageUri">The image of the default printing of the card, or <see langword="null"/>.</param>
/// <param name="Locations">The copies in each location or section.</param>
public sealed record CardViewResponse(Guid OracleId, string Name, int Quantity, string? ImageUri, IReadOnlyList<CardViewLocationResponse> Locations);

/// <summary>The copies of one card in one location or section.</summary>
/// <param name="LocationId">The location or the section.</param>
/// <param name="LocationName">The name of the location. For a section, the name of its parent location.</param>
/// <param name="SectionName">The name of the section, or <see langword="null"/>.</param>
/// <param name="Kind">The kind of the location.</param>
/// <param name="Quantity">The number of copies.</param>
public sealed record CardViewLocationResponse(Guid LocationId, string LocationName, string? SectionName, string Kind, int Quantity);

/// <summary>The collection settings of the user.</summary>
/// <param name="AdvancedFields">
/// The enabled advanced fields: <c>finish</c>, <c>condition</c>, <c>purchasePrice</c>, <c>acquiredOn</c> and <c>note</c>.
/// The API ignores a disabled field in a request and never returns it.
/// </param>
public sealed record CollectionSettingsResponse(IReadOnlyList<string> AdvancedFields);

/// <summary>The new collection settings of the user.</summary>
/// <param name="AdvancedFields">The advanced fields to enable. The other advanced fields become disabled.</param>
public sealed record CollectionSettingsRequest(IReadOnlyList<string>? AdvancedFields);

/// <summary>A file to import into a location.</summary>
/// <param name="Format">The format of the file: <c>text</c>, <c>csv</c>, <c>dek</c>, <c>mtga</c>, <c>mtgo</c>, <c>moxfield</c>, <c>archidekt</c>, <c>deckbox</c>, <c>manabox</c> or <c>dragonshield</c>.</param>
/// <param name="Content">The content of the file.</param>
/// <param name="Origin">Where the cards come from: <c>purchase</c>, <c>gift</c>, <c>trade</c> or <c>nowhere</c>. The default is <c>nowhere</c>.</param>
/// <param name="Currency">The currency of a purchase price that has no currency in the file, for example <c>EUR</c>. The default is <c>USD</c>.</param>
public sealed record ImportEntriesRequest(string? Format, string? Content, string? Origin = null, string? Currency = null);

/// <summary>A line of an imported file that the import cannot use, or that the import changed.</summary>
/// <param name="Line">The number of the line. The first line has the number 1.</param>
/// <param name="Code">The reason code, for example <c>card_not_found</c>.</param>
/// <param name="Text">The text of the line.</param>
public sealed record ImportLineResponse(int Line, string Code, string Text);

/// <summary>The result of an import.</summary>
/// <param name="BatchId">The batch ID of the movements, or <see langword="null"/> when the import added no card.</param>
/// <param name="LineCount">The number of lines that the import added.</param>
/// <param name="CardCount">The number of copies that the import added.</param>
/// <param name="Errors">The lines that the import cannot use. The import ignored these lines.</param>
/// <param name="Warnings">The lines that the import added with a change, for example a card with no printing.</param>
public sealed record ImportEntriesResponse(Guid? BatchId, int LineCount, int CardCount, IReadOnlyList<ImportLineResponse> Errors, IReadOnlyList<ImportLineResponse> Warnings);

/// <summary>Cards of the collection as a file.</summary>
/// <param name="FileName">The name of the file.</param>
/// <param name="ContentType">The media type of the file.</param>
/// <param name="Content">The content of the file.</param>
public sealed record ExportCollectionResponse(string FileName, string ContentType, string Content);
