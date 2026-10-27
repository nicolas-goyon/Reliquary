namespace Reliquary.Cards.Api;

/// <summary>
/// An Oracle card, as the public API returns it.
/// </summary>
public sealed record CardResponse
{
    /// <summary>Gets the Oracle identifier of the card.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the English name of the card. A card with two faces has the form "A // B".</summary>
    public required string Name { get; init; }

    /// <summary>Gets the layout of the card, for example <c>normal</c>, <c>transform</c> or <c>split</c>.</summary>
    public required string Layout { get; init; }

    /// <summary>Gets the mana cost of the card.</summary>
    public string? ManaCost { get; init; }

    /// <summary>Gets the mana value of the card.</summary>
    public required decimal ManaValue { get; init; }

    /// <summary>Gets the type line of the card.</summary>
    public string? TypeLine { get; init; }

    /// <summary>Gets the Oracle text of the card.</summary>
    public string? OracleText { get; init; }

    /// <summary>Gets the colors of the card.</summary>
    public required IReadOnlyList<string> Colors { get; init; }

    /// <summary>Gets the color identity of the card.</summary>
    public required IReadOnlyList<string> ColorIdentity { get; init; }

    /// <summary>Gets the keywords of the card.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>Gets the colors of mana that the card can produce.</summary>
    public required IReadOnlyList<string> ProducedMana { get; init; }

    /// <summary>Gets the power of the card.</summary>
    public string? Power { get; init; }

    /// <summary>Gets the toughness of the card.</summary>
    public string? Toughness { get; init; }

    /// <summary>Gets the loyalty of the card.</summary>
    public string? Loyalty { get; init; }

    /// <summary>Gets the defense of the card.</summary>
    public string? Defense { get; init; }

    /// <summary>Gets the legality of the card in each format: <c>legal</c>, <c>not_legal</c>, <c>banned</c> or <c>restricted</c>.</summary>
    public required IReadOnlyDictionary<string, string> Legalities { get; init; }

    /// <summary>Gets whether the card is on the Reserved List.</summary>
    public required bool Reserved { get; init; }

    /// <summary>Gets whether the card is a Game Changer of the Commander brackets.</summary>
    public required bool GameChanger { get; init; }

    /// <summary>Gets the rank of the card on EDHREC.</summary>
    public int? EdhrecRank { get; init; }

    /// <summary>Gets whether the card is an extra card: a token, an emblem, an art card, a plane or another card that is not for a deck.</summary>
    public required bool Extra { get; init; }

    /// <summary>Gets the faces of the card. A card with one face has one face.</summary>
    public required IReadOnlyList<CardFaceResponse> Faces { get; init; }

    /// <summary>Gets the default printing of the card.</summary>
    public PrintingResponse? DefaultPrinting { get; init; }

    /// <summary>Gets the other names of the card: face names, localized names and alternate names.</summary>
    public required IReadOnlyList<CardNameResponse> Names { get; init; }

    /// <summary>Gets the related cards: tokens, meld parts and combo pieces.</summary>
    public required IReadOnlyList<RelatedCardResponse> RelatedCards { get; init; }
}

/// <summary>A face or a half of a card.</summary>
/// <param name="Name">The name of the face.</param>
/// <param name="ManaCost">The mana cost of the face.</param>
/// <param name="ManaValue">The mana value of the face.</param>
/// <param name="TypeLine">The type line of the face.</param>
/// <param name="OracleText">The Oracle text of the face.</param>
/// <param name="Colors">The colors of the face.</param>
/// <param name="Power">The power of the face.</param>
/// <param name="Toughness">The toughness of the face.</param>
/// <param name="Loyalty">The loyalty of the face.</param>
/// <param name="Defense">The defense of the face.</param>
public sealed record CardFaceResponse(
    string Name,
    string? ManaCost,
    decimal ManaValue,
    string? TypeLine,
    string? OracleText,
    IReadOnlyList<string> Colors,
    string? Power,
    string? Toughness,
    string? Loyalty,
    string? Defense);

/// <summary>A name of a card.</summary>
/// <param name="Name">The name.</param>
/// <param name="Lang">The language of the name.</param>
/// <param name="Kind">The kind of the name: <c>oracle</c>, <c>face</c>, <c>printed</c> (localized) or <c>flavor</c> (alternate name).</param>
public sealed record CardNameResponse(string Name, string Lang, string Kind);

/// <summary>A card related to another card.</summary>
/// <param name="PrintingId">The printing of the related card.</param>
/// <param name="Name">The name of the related card.</param>
/// <param name="Component">The relation: <c>token</c>, <c>meld_part</c>, <c>meld_result</c> or <c>combo_piece</c>.</param>
public sealed record RelatedCardResponse(Guid PrintingId, string Name, string Component);

/// <summary>
/// A printing of a card, as the public API returns it.
/// </summary>
public sealed record PrintingResponse
{
    /// <summary>Gets the identifier of the printing. It is the Scryfall identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the Oracle identifier of the card.</summary>
    public required Guid CardId { get; init; }

    /// <summary>Gets the code of the set.</summary>
    public required string SetCode { get; init; }

    /// <summary>Gets the name of the set.</summary>
    public required string SetName { get; init; }

    /// <summary>Gets the collector number.</summary>
    public required string CollectorNumber { get; init; }

    /// <summary>Gets the language of the printing.</summary>
    public required string Lang { get; init; }

    /// <summary>Gets the release date of the printing.</summary>
    public required DateOnly ReleasedAt { get; init; }

    /// <summary>Gets the rarity of the printing.</summary>
    public required string Rarity { get; init; }

    /// <summary>Gets the finishes: <c>nonfoil</c>, <c>foil</c> or <c>etched</c>.</summary>
    public required IReadOnlyList<string> Finishes { get; init; }

    /// <summary>Gets the name as printed, for a printing in another language.</summary>
    public string? PrintedName { get; init; }

    /// <summary>Gets the artist.</summary>
    public string? Artist { get; init; }

    /// <summary>Gets the addresses of the images on the Scryfall servers, by size.</summary>
    public required IReadOnlyDictionary<string, string> ImageUris { get; init; }

    /// <summary>Gets the faces of the printing, for a card with more than one face.</summary>
    public required IReadOnlyList<PrintingFaceResponse> Faces { get; init; }

    /// <summary>Gets the prices of Scryfall, by currency. Do not use them for a sale.</summary>
    public required IReadOnlyDictionary<string, string> Prices { get; init; }

    /// <summary>Gets the identifiers of the printing on other sites, for example Cardmarket or TCGplayer.</summary>
    public required IReadOnlyDictionary<string, string> ExternalIds { get; init; }
}

/// <summary>A face of a printing.</summary>
/// <param name="Name">The English name of the face.</param>
/// <param name="PrintedName">The name of the face as printed.</param>
/// <param name="ImageUris">The images of the face, by size.</param>
public sealed record PrintingFaceResponse(string Name, string? PrintedName, IReadOnlyDictionary<string, string> ImageUris);

/// <summary>A ruling about a card.</summary>
/// <param name="Source">The source: <c>wotc</c> or <c>scryfall</c>.</param>
/// <param name="PublishedAt">The publication date.</param>
/// <param name="Comment">The text of the ruling.</param>
public sealed record RulingResponse(string Source, DateOnly PublishedAt, string Comment);

/// <summary>A set of cards.</summary>
/// <param name="Code">The code of the set.</param>
/// <param name="Name">The name of the set.</param>
/// <param name="SetType">The type of the set, for example <c>expansion</c> or <c>commander</c>.</param>
/// <param name="ReleasedAt">The release date.</param>
/// <param name="CardCount">The number of cards.</param>
/// <param name="Digital">Whether the set exists only in digital form.</param>
/// <param name="IconSvgUri">The address of the icon of the set.</param>
public sealed record SetResponse(string Code, string Name, string SetType, DateOnly? ReleasedAt, int CardCount, bool Digital, string? IconSvgUri);

/// <summary>
/// A card that the autocomplete suggests.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The Oracle name of the card.</param>
/// <param name="MatchedName">The name that matches the text: the Oracle name, a face name, a localized name or an alternate name.</param>
/// <param name="Lang">The language of the matched name.</param>
public sealed record AutocompleteItem(Guid OracleId, string Name, string MatchedName, string Lang);
