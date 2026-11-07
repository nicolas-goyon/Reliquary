namespace Reliquary.Collection.Data;

/// <summary>
/// A location as the table <c>content.locations</c> stores it, with the facts of its parent location.
/// </summary>
internal sealed class LocationRow
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public bool Locked { get; set; }

    public bool Pickable { get; set; } = true;

    public Guid? DeckId { get; set; }

    public int Position { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Gets or sets the name of the parent location, or <see langword="null"/> for a location with no parent.</summary>
    public string? ParentName { get; set; }

    /// <summary>Gets or sets a value that tells whether the parent location is locked.</summary>
    public bool ParentLocked { get; set; }

    /// <summary>Gets or sets a value that tells whether the parent location is pickable. A location with no parent gets <see langword="true"/>.</summary>
    public bool ParentPickable { get; set; } = true;

    /// <summary>Gets a value that tells whether the content cannot change: the location or its parent is locked.</summary>
    public bool EffectiveLocked => Locked || ParentLocked;

    /// <summary>Gets a value that tells whether the cards are available cards: the location and its parent are pickable, and the location is not a deck.</summary>
    public bool EffectivePickable => Pickable && ParentPickable;

    /// <summary>Gets the name to show in the messages: "Rack 1 / Box A" for a section.</summary>
    public string FullName => ParentName is null ? Name : $"{ParentName} / {Name}";
}

/// <summary>
/// A location with the number of cards in it, for the list of the locations.
/// </summary>
internal sealed class LocationSummaryRow
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public bool Locked { get; set; }

    public bool Pickable { get; set; }

    public Guid? DeckId { get; set; }

    public int Position { get; set; }

    public long CardCount { get; set; }

    public long EntryCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// The fields that identify the copies of a card in a location. Two entries of one location never have the same fields.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="Language">The language code.</param>
/// <param name="Finish">The finish, or <see langword="null"/>.</param>
/// <param name="Condition">The condition, or <see langword="null"/>.</param>
/// <param name="PurchasePrice">The purchase price of one copy, or <see langword="null"/>.</param>
/// <param name="PurchaseCurrency">The currency of the purchase price, or <see langword="null"/>.</param>
/// <param name="AcquiredOn">The date when the owner got the copies, or <see langword="null"/>.</param>
/// <param name="Note">The note, or <see langword="null"/>.</param>
internal sealed record CardFields(
    Guid OracleId,
    Guid? PrintingId,
    string Language,
    string? Finish,
    string? Condition,
    decimal? PurchasePrice,
    string? PurchaseCurrency,
    DateOnly? AcquiredOn,
    string? Note)
{
    /// <summary>Gets the date as a value of a row.</summary>
    public DateTime? AcquiredOnValue => AcquiredOn?.ToDateTime(TimeOnly.MinValue);

    /// <summary>Gets the date as a text parameter of a query, for example <c>2026-10-28</c>.</summary>
    public string? AcquiredOnText => AcquiredOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets a stable text that identifies the fields, for example as the key of a difference of an inventory.
    /// </summary>
    public string Key => string.Join(
        '|',
        OracleId,
        PrintingId?.ToString() ?? string.Empty,
        Language,
        Finish ?? string.Empty,
        Condition ?? string.Empty,
        PurchasePrice?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        PurchaseCurrency ?? string.Empty,
        AcquiredOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        Note ?? string.Empty);
}

/// <summary>
/// The card fields of an entry, with the copies of the card name and the set code.
/// </summary>
/// <param name="Fields">The card fields.</param>
/// <param name="CardName">The Oracle name of the card.</param>
/// <param name="SetCode">The set code of the printing, or <see langword="null"/>.</param>
internal sealed record CardLine(CardFields Fields, string CardName, string? SetCode);

/// <summary>
/// An entry as the table <c>content.location_entries</c> stores it.
/// </summary>
internal sealed class EntryRow
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid LocationId { get; set; }

    public Guid OracleId { get; set; }

    public Guid? PrintingId { get; set; }

    public string CardName { get; set; } = string.Empty;

    public string? SetCode { get; set; }

    public string Language { get; set; } = "en";

    public string? Finish { get; set; }

    public string? Condition { get; set; }

    public decimal? PurchasePrice { get; set; }

    public string? PurchaseCurrency { get; set; }

    public DateTime? AcquiredOn { get; set; }

    /// <summary>Gets the date as a text parameter of a query.</summary>
    public string? AcquiredOnText => AcquiredOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public string? Note { get; set; }

    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Gets the card fields of the entry.</summary>
    public CardFields Fields => new(
        OracleId,
        PrintingId,
        Language,
        Finish,
        Condition,
        PurchasePrice,
        PurchaseCurrency,
        AcquiredOn is { } date ? DateOnly.FromDateTime(date) : null,
        Note);

    /// <summary>Gets the card fields with the copies of the name and the set code.</summary>
    public CardLine Line => new(Fields, CardName, SetCode);
}

/// <summary>
/// A movement as the table <c>content.movements</c> stores it.
/// </summary>
internal sealed class MovementRow
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid BatchId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string OriginKind { get; set; } = string.Empty;

    public Guid? OriginLocationId { get; set; }

    public string DestinationKind { get; set; } = string.Empty;

    public Guid? DestinationLocationId { get; set; }

    public Guid OracleId { get; set; }

    public Guid? PrintingId { get; set; }

    public string CardName { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public string? Finish { get; set; }

    public string? Condition { get; set; }

    public decimal? PurchasePrice { get; set; }

    public string? PurchaseCurrency { get; set; }

    public DateTime? AcquiredOn { get; set; }

    /// <summary>Gets the date as a text parameter of a query.</summary>
    public string? AcquiredOnText => AcquiredOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public string? Note { get; set; }

    public int Quantity { get; set; }

    public Guid? InventoryId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the name of the origin location when it exists, for the history.</summary>
    public string? OriginName { get; set; }

    /// <summary>Gets or sets the name of the destination location when it exists, for the history.</summary>
    public string? DestinationName { get; set; }

    /// <summary>Gets the card fields of the movement.</summary>
    public CardFields Fields => new(
        OracleId,
        PrintingId,
        Language,
        Finish,
        Condition,
        PurchasePrice,
        PurchaseCurrency,
        AcquiredOn is { } date ? DateOnly.FromDateTime(date) : null,
        Note);
}

/// <summary>
/// The origin or the destination of a movement: a location, or a place outside the collection.
/// </summary>
/// <param name="Kind">The kind. See <see cref="CollectionRules.Endpoints"/>.</param>
/// <param name="LocationId">The location when the kind is <c>location</c>.</param>
internal sealed record MovementEndpoint(string Kind, Guid? LocationId = null)
{
    public static MovementEndpoint Nowhere { get; } = new(CollectionRules.Endpoints.Nowhere);

    public static MovementEndpoint At(Guid locationId) => new(CollectionRules.Endpoints.Location, locationId);

    public bool IsLocation => Kind == CollectionRules.Endpoints.Location;
}
