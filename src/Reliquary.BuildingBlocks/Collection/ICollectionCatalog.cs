namespace Reliquary.BuildingBlocks.Collection;

/// <summary>
/// The kinds of locations. See docs/decisions/0034-collection-model.md.
/// </summary>
public static class LocationKinds
{
    /// <summary>A rack.</summary>
    public const string Rack = "rack";

    /// <summary>A binder.</summary>
    public const string Binder = "binder";

    /// <summary>A storage box.</summary>
    public const string Box = "box";

    /// <summary>The location of an assembled deck.</summary>
    public const string Deck = "deck";

    /// <summary>A cube.</summary>
    public const string Cube = "cube";

    /// <summary>Gets all the kinds.</summary>
    public static IReadOnlyList<string> All { get; } = [Rack, Binder, Box, Deck, Cube];
}

/// <summary>
/// Copies of one card in one location entry of the collection.
/// </summary>
/// <param name="LocationId">The location, or the section, of the entry.</param>
/// <param name="LocationName">The name of the location. For a section, the name of its parent location.</param>
/// <param name="SectionName">The name of the section, or <see langword="null"/> when the entry is not in a section.</param>
/// <param name="LocationKind">The kind of the location. See <see cref="LocationKinds"/>.</param>
/// <param name="Locked">Whether the location is locked: its content cannot change until the user unlocks it.</param>
/// <param name="Pickable">Whether the cards of the location can complete a deck.</param>
/// <param name="DeckId">The deck of an assembled deck location, or <see langword="null"/>.</param>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/> when the owner did not give it.</param>
/// <param name="Language">The language code, for example <c>en</c> or <c>fr</c>.</param>
/// <param name="Finish">The finish (<c>nonfoil</c>, <c>foil</c> or <c>etched</c>), or <see langword="null"/> when the field is not enabled.</param>
/// <param name="Condition">The condition, or <see langword="null"/> when the field is not enabled.</param>
/// <param name="Quantity">The number of copies.</param>
public sealed record CollectionCopy(
    Guid LocationId,
    string LocationName,
    string? SectionName,
    string LocationKind,
    bool Locked,
    bool Pickable,
    Guid? DeckId,
    Guid OracleId,
    Guid? PrintingId,
    string Language,
    string? Finish,
    string? Condition,
    int Quantity)
{
    /// <summary>
    /// Gets a value that tells whether the copies can complete a deck: the location is pickable and is not an assembled deck.
    /// </summary>
    public bool Available => Pickable && LocationKind != LocationKinds.Deck;
}

/// <summary>
/// Gives the cards of the collection of an owner to the other core components.
/// The Collection component implements it. See docs/decisions/0034-collection-model.md.
/// </summary>
public interface ICollectionCatalog
{
    /// <summary>
    /// Gets all the copies of some Oracle cards in the collection of an owner, in all the locations.
    /// </summary>
    /// <param name="ownerId">The owner of the collection.</param>
    /// <param name="oracleIds">The Oracle cards.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The copies. A card that the owner does not have is not in the result.</returns>
    Task<IReadOnlyList<CollectionCopy>> GetCopiesAsync(Guid ownerId, IReadOnlyCollection<Guid> oracleIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// One card of an assembled deck, for the location of the deck.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="Language">The language code. Use the language of the printing, or <c>en</c>.</param>
/// <param name="Finish">The finish, or <see langword="null"/>.</param>
/// <param name="Quantity">The number of copies.</param>
public sealed record AssembledDeckCard(Guid OracleId, Guid? PrintingId, string Language, string? Finish, int Quantity);

/// <summary>
/// Lets the Decks component manage the location of an assembled deck. The Collection component implements it.
/// </summary>
public interface IAssembledDeckLocations
{
    /// <summary>
    /// Creates the location of an assembled deck, or renames it when it exists.
    /// </summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="deckId">The deck.</param>
    /// <param name="deckName">The name of the deck, for the name of the location.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The ID of the location.</returns>
    Task<Guid> EnsureLocationAsync(Guid ownerId, Guid deckId, string deckName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the content of the location of an assembled deck. The method records the differences as movements from or to "nowhere".
    /// </summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="deckId">The deck. Its location must exist.</param>
    /// <param name="cards">The cards of the assembled version.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the location has the cards.</returns>
    Task SetContentAsync(Guid ownerId, Guid deckId, IReadOnlyList<AssembledDeckCard> cards, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the location of a deck that is not assembled any more.
    /// </summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="deckId">The deck.</param>
    /// <param name="targetLocationId">
    /// The location that gets the cards of the deck, or <see langword="null"/> to remove the cards from the collection.
    /// </param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the deck has no location.</returns>
    Task ReleaseAsync(Guid ownerId, Guid deckId, Guid? targetLocationId, CancellationToken cancellationToken = default);
}
