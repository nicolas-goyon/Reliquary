namespace Reliquary.BuildingBlocks.Decks;

/// <summary>
/// Gives the decks of the online games to the Playtest component. The Decks component implements it.
/// </summary>
/// <remarks>
/// The Playtest component uses this interface, and never the tables of the Decks component.
/// See docs/decisions/0047-trusted-server-for-online-games.md.
/// </remarks>
public interface IGameDeckSource
{
    /// <summary>
    /// Gets a deck for a game, when the viewer can see the deck.
    /// The owner sees each deck. The other users and the guests see the unlisted and public decks that an administrator did not hide.
    /// </summary>
    /// <param name="deckId">The ID of the deck.</param>
    /// <param name="viewerId">The account of the viewer, or <see langword="null"/> for a guest.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The deck, or <see langword="null"/> when the deck does not exist or the viewer cannot see it.</returns>
    Task<GameDeck?> GetDeckAsync(Guid deckId, Guid? viewerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a deck text with the deck text format of the deck import. See docs/development/deck-text-format.md.
    /// </summary>
    /// <param name="text">The deck text.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The deck. Its name is empty and its format is <see langword="null"/>.</returns>
    /// <exception cref="GameDeckTextException">The text has lines that the API cannot use.</exception>
    Task<GameDeck> ParseDeckTextAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// A deck for a game: the cards of the library and the cards that start in the command zone.
/// The sideboard and the other zones of the deck are not in the game.
/// </summary>
/// <param name="Name">The name of the deck. A deck from a text has an empty name.</param>
/// <param name="Format">The format of the deck, for example <c>commander</c>, or <see langword="null"/> for a deck from a text.</param>
/// <param name="Library">The cards of the main zone of the deck.</param>
/// <param name="Command">The cards of the commander zone and of the companion zone of the deck.</param>
public sealed record GameDeck(string Name, string? Format, IReadOnlyList<GameDeckCard> Library, IReadOnlyList<GameDeckCard> Command)
{
    /// <summary>Gets the number of cards of the library and of the command zone.</summary>
    public int CardCount => Library.Sum(card => card.Quantity) + Command.Sum(card => card.Quantity);
}

/// <summary>
/// Copies of one card of a game deck. The fields are the fields of a card of the table engine.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The Oracle name of the card.</param>
/// <param name="ImageUri">The address of the image of the printing of the line, or of the default printing. <see langword="null"/> when the card has no image.</param>
/// <param name="IsLand">Whether the card is a land.</param>
/// <param name="ManaValue">The mana value of the rules.</param>
/// <param name="Quantity">The number of copies, 1 or more.</param>
public sealed record GameDeckCard(Guid OracleId, string Name, string? ImageUri, bool IsLand, decimal ManaValue, int Quantity);

/// <summary>
/// A deck text with lines that the API cannot use.
/// </summary>
/// <param name="lineErrors">The lines with an error.</param>
public sealed class GameDeckTextException(IReadOnlyList<GameDeckTextError> lineErrors)
    : Exception("The deck text has lines that the API cannot use. Correct the lines in the lineErrors list.")
{
    /// <summary>Gets the lines with an error, in the order of the text.</summary>
    public IReadOnlyList<GameDeckTextError> LineErrors { get; } = lineErrors;
}

/// <summary>
/// One line of a deck text that the API cannot use.
/// </summary>
/// <param name="Line">The number of the line. The first line has the number 1.</param>
/// <param name="Code">The stable reason code, for example <c>card_not_found</c>.</param>
/// <param name="Text">The text of the line.</param>
public sealed record GameDeckTextError(int Line, string Code, string Text);
