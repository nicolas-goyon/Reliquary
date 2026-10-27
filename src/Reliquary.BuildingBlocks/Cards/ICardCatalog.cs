namespace Reliquary.BuildingBlocks.Cards;

/// <summary>
/// Gives the card reference data to the core components. The Cards component implements it.
/// </summary>
/// <remarks>
/// The other core components use this interface, and never the tables of the Cards component.
/// </remarks>
public interface ICardCatalog
{
    /// <summary>
    /// Gets the facts of Oracle cards.
    /// </summary>
    /// <param name="oracleIds">The IDs of the Oracle cards.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The facts of each card that exists, by ID. An unknown ID is not in the result.</returns>
    Task<IReadOnlyDictionary<Guid, CardFacts>> GetCardsAsync(IReadOnlyCollection<Guid> oracleIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the Oracle card of each name. The names can be Oracle names, face names, localized names or alternate names.
    /// </summary>
    /// <param name="names">The names, as the user typed them.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>One result for each distinct name, by the name as the caller gave it.</returns>
    Task<IReadOnlyDictionary<string, CardNameMatch>> ResolveNamesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds printings by their set code and collector number.
    /// </summary>
    /// <param name="references">The references of the printings.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The printing of each reference that exists.</returns>
    Task<IReadOnlyDictionary<PrintingReference, PrintingFacts>> FindPrintingsAsync(IReadOnlyCollection<PrintingReference> references, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets printings by their IDs.
    /// </summary>
    /// <param name="printingIds">The IDs of the printings.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The printing of each ID that exists.</returns>
    Task<IReadOnlyDictionary<Guid, PrintingFacts>> GetPrintingsAsync(IReadOnlyCollection<Guid> printingIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the Oracle tags of Scryfall Tagger that the import of the card data stored for Oracle cards. See ADR 0031.
    /// </summary>
    /// <param name="oracleIds">The IDs of the Oracle cards.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The tags of each card that has tags, by ID. A card with no tag is not in the result.</returns>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CardOracleTag>>> GetOracleTagsAsync(IReadOnlyCollection<Guid> oracleIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// One Oracle tag of Scryfall Tagger on an Oracle card.
/// </summary>
/// <param name="Slug">The slug of the tag, for example <c>mana-rock</c>.</param>
/// <param name="Weight">The weight of the link between the tag and the card, for example <c>median</c> or <c>very_strong</c>.</param>
/// <param name="Ancestors">The slugs of the parent tags of the tag, of their parent tags, and so on. The list does not contain the tag.</param>
public sealed record CardOracleTag(string Slug, string Weight, IReadOnlyList<string> Ancestors);

/// <summary>
/// The facts of an Oracle card that the other core components need.
/// </summary>
/// <param name="OracleId">The ID of the Oracle card.</param>
/// <param name="Name">The Oracle name.</param>
/// <param name="Layout">The Scryfall layout, for example <c>normal</c>, <c>split</c> or <c>modal_dfc</c>.</param>
/// <param name="ManaValue">The mana value of the rules.</param>
/// <param name="TypeLine">The full type line.</param>
/// <param name="ColorIdentity">The color identity, with the letters W, U, B, R and G.</param>
/// <param name="Legalities">The legality of the card in each format, for example <c>commander</c>: <c>legal</c>.</param>
/// <param name="Keywords">The keywords, for example <c>Flying</c>.</param>
/// <param name="ProducedMana">The colors of mana that the card can produce.</param>
/// <param name="IsExtra">Whether the card is a token, an emblem, a meld result or another card that is not a game card.</param>
/// <param name="GameChanger">Whether the card is on the Game Changers list of Commander.</param>
/// <param name="DefaultPrintingId">The printing that represents the card, or <see langword="null"/>.</param>
/// <param name="Faces">The faces of the card. A card with one face has one face.</param>
/// <param name="Analysis">The facts for the deck analysis. See ADR 0025.</param>
public sealed record CardFacts(
    Guid OracleId,
    string Name,
    string Layout,
    decimal ManaValue,
    string? TypeLine,
    IReadOnlyList<string> ColorIdentity,
    IReadOnlyDictionary<string, string> Legalities,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> ProducedMana,
    bool IsExtra,
    bool GameChanger,
    Guid? DefaultPrintingId,
    IReadOnlyList<CardFaceFacts> Faces,
    CardAnalysisFacts Analysis)
{
    /// <summary>
    /// Gets the legality of the card in a format.
    /// </summary>
    /// <param name="format">The format, for example <c>commander</c>.</param>
    /// <returns>The legality, for example <c>legal</c>, <c>banned</c> or <c>not_legal</c>.</returns>
    public string LegalityIn(string format) => Legalities.TryGetValue(format, out var legality) ? legality : "not_legal";
}

/// <summary>
/// One face or one half of a card.
/// </summary>
/// <param name="Index">The index of the face. The first face has the index 0.</param>
/// <param name="Name">The name of the face.</param>
/// <param name="ManaCost">The mana cost, for example <c>{1}{R}</c>, or <see langword="null"/>.</param>
/// <param name="ManaValue">The mana value of the face.</param>
/// <param name="TypeLine">The type line of the face.</param>
/// <param name="OracleText">The rules text of the face.</param>
/// <param name="Colors">The colors of the face.</param>
public sealed record CardFaceFacts(int Index, string Name, string? ManaCost, decimal ManaValue, string? TypeLine, string? OracleText, IReadOnlyList<string> Colors);

/// <summary>
/// The facts of a card for the deck analysis, from the rules of ADR 0025.
/// </summary>
/// <param name="CastableFaces">The indexes of the faces that a player can cast or play from the hand.</param>
/// <param name="PrimaryFace">The index of the face that the mana curve uses.</param>
/// <param name="CurveManaValue">The mana value in the mana curve.</param>
/// <param name="IsLand">Whether the first face is a land.</param>
/// <param name="IsModalLand">Whether the card is a modal double-faced card with a spell face and a land face.</param>
/// <param name="CardTypes">The card types of the castable faces, for example <c>Creature</c> and <c>Instant</c>.</param>
/// <param name="Colors">The colors of the castable faces.</param>
/// <param name="PrimaryManaCost">The mana cost of the primary face, for the mana requirements.</param>
public sealed record CardAnalysisFacts(
    IReadOnlyList<int> CastableFaces,
    int PrimaryFace,
    decimal CurveManaValue,
    bool IsLand,
    bool IsModalLand,
    IReadOnlyList<string> CardTypes,
    IReadOnlyList<string> Colors,
    string? PrimaryManaCost);

/// <summary>
/// The result of the resolution of one card name.
/// </summary>
/// <param name="Status">The result of the resolution.</param>
/// <param name="OracleId">The Oracle card, when the status is <see cref="CardNameStatus.Found"/>.</param>
/// <param name="Name">The Oracle name of the card, when the status is <see cref="CardNameStatus.Found"/>.</param>
public sealed record CardNameMatch(CardNameStatus Status, Guid? OracleId, string? Name);

/// <summary>
/// The result of the resolution of a card name.
/// </summary>
public enum CardNameStatus
{
    /// <summary>One Oracle card has this name.</summary>
    Found,

    /// <summary>No card has this name.</summary>
    NotFound,
}

/// <summary>
/// A reference to a printing: the set code and the collector number.
/// </summary>
/// <param name="SetCode">The set code, in lowercase, for example <c>m21</c>.</param>
/// <param name="CollectorNumber">The collector number, for example <c>123</c> or <c>123a</c>.</param>
/// <param name="Lang">The language, for example <c>en</c>, or <see langword="null"/> for the default language.</param>
public sealed record PrintingReference(string SetCode, string CollectorNumber, string? Lang = null);

/// <summary>
/// The facts of a printing.
/// </summary>
/// <param name="PrintingId">The ID of the printing.</param>
/// <param name="OracleId">The Oracle card of the printing.</param>
/// <param name="SetCode">The set code.</param>
/// <param name="CollectorNumber">The collector number.</param>
/// <param name="Lang">The language.</param>
/// <param name="Finishes">The finishes, for example <c>nonfoil</c> and <c>foil</c>.</param>
/// <param name="ImageUri">The address of the normal image of the first face, or <see langword="null"/>.</param>
public sealed record PrintingFacts(Guid PrintingId, Guid OracleId, string SetCode, string CollectorNumber, string Lang, IReadOnlyList<string> Finishes, string? ImageUri);
