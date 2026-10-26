using Reliquary.Cards.Search;

namespace Reliquary.Cards.Api;

/// <summary>
/// One page of the results of a card search.
/// </summary>
/// <param name="Items">The results of the page.</param>
/// <param name="NextCursor">The cursor of the next page, or <see langword="null"/> on the last page.</param>
/// <param name="Total">The number of results of the search, on all the pages.</param>
/// <param name="Warnings">The terms of the query that the search ignored.</param>
public sealed record CardSearchResponse(IReadOnlyList<CardSearchItem> Items, string? NextCursor, long Total, IReadOnlyList<SearchWarning> Warnings);

/// <summary>
/// One result of a card search: an Oracle card and the printing that the search chose for it.
/// </summary>
/// <param name="Card">The Oracle card.</param>
/// <param name="Printing">The printing of the card that matches the query.</param>
public sealed record CardSearchItem(CardSummaryResponse Card, PrintingSummaryResponse Printing);

/// <summary>
/// A short form of an Oracle card, for the lists.
/// </summary>
public sealed record CardSummaryResponse
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

    /// <summary>Gets the power of the card.</summary>
    public string? Power { get; init; }

    /// <summary>Gets the toughness of the card.</summary>
    public string? Toughness { get; init; }

    /// <summary>Gets the loyalty of the card.</summary>
    public string? Loyalty { get; init; }

    /// <summary>Gets the defense of the card.</summary>
    public string? Defense { get; init; }

    /// <summary>Gets the faces of the card. A card with one face has one face.</summary>
    public required IReadOnlyList<CardFaceResponse> Faces { get; init; }
}

/// <summary>
/// A short form of a printing, for the lists.
/// </summary>
public sealed record PrintingSummaryResponse
{
    /// <summary>Gets the identifier of the printing. It is the Scryfall identifier.</summary>
    public required Guid Id { get; init; }

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

    /// <summary>Gets the name as printed, for a printing in another language.</summary>
    public string? PrintedName { get; init; }

    /// <summary>Gets the addresses of the images on the Scryfall servers, by size.</summary>
    public required IReadOnlyDictionary<string, string> ImageUris { get; init; }

    /// <summary>Gets the faces of the printing, for a card with more than one face.</summary>
    public required IReadOnlyList<PrintingFaceResponse> Faces { get; init; }
}
