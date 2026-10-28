namespace Reliquary.Decks.Exploration;

/// <summary>A user as an author of public decks.</summary>
/// <param name="Id">The account ID of the author.</param>
/// <param name="DisplayName">The display name.</param>
public sealed record AuthorSummaryResponse(Guid Id, string DisplayName);

/// <summary>A commander of a public deck.</summary>
/// <param name="OracleId">The Oracle ID of the card.</param>
/// <param name="Name">The name of the card.</param>
/// <param name="ImageUri">The image of the default printing, or <see langword="null"/>.</param>
public sealed record PublicDeckCommanderResponse(Guid OracleId, string Name, string? ImageUri);

/// <summary>A public deck in the search results.</summary>
/// <param name="Id">The deck ID. The deck page is <c>GET /decks/{id}</c>.</param>
/// <param name="Name">The name.</param>
/// <param name="Format">The format.</param>
/// <param name="ColorIdentity">The color identity, with the letters W, U, B, R and G. An empty text is colorless.</param>
/// <param name="Commanders">The commanders.</param>
/// <param name="Author">The author.</param>
/// <param name="CardCount">The number of cards in the commander, companion and main zones.</param>
/// <param name="CopyCount">The number of copies by other users.</param>
/// <param name="FavoriteCount">The number of users with the deck in their favorites.</param>
/// <param name="UpdatedAt">The time of the last change.</param>
public sealed record PublicDeckResponse(
    Guid Id,
    string Name,
    string Format,
    string ColorIdentity,
    IReadOnlyList<PublicDeckCommanderResponse> Commanders,
    AuthorSummaryResponse Author,
    int CardCount,
    int CopyCount,
    int FavoriteCount,
    DateTimeOffset UpdatedAt);

/// <summary>The profile of an author.</summary>
/// <param name="Id">The account ID of the author.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="DeckCount">The number of public decks.</param>
/// <param name="FollowerCount">The number of followers.</param>
/// <param name="Following">Whether the user follows the author. It is <see langword="false"/> for a visitor.</param>
/// <param name="Hidden">Whether an administrator hid the profile. Only the author sees a hidden profile.</param>
public sealed record AuthorResponse(Guid Id, string DisplayName, int DeckCount, int FollowerCount, bool Following, bool Hidden = false);

/// <summary>The social data of a public deck for the user.</summary>
/// <param name="Public">Whether the deck is in the read model of the public decks.</param>
/// <param name="Favorite">Whether the deck is a favorite of the user.</param>
/// <param name="FavoriteCount">The number of users with the deck in their favorites.</param>
/// <param name="CopyCount">The number of copies by other users.</param>
/// <param name="Author">The author.</param>
public sealed record PublicDeckSocialResponse(bool Public, bool Favorite, int FavoriteCount, int CopyCount, AuthorSummaryResponse? Author);
