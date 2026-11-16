namespace Reliquary.Playtest.Api;

/// <summary>
/// The request to create an online game.
/// </summary>
/// <param name="DeckId">The ID of a deck that the host can see. Give the deck ID or the deck text.</param>
/// <param name="DeckText">A deck text in the format of the deck import. Give the deck ID or the deck text.</param>
/// <param name="Seats">The number of seats, from 2 to 4 with the default settings. The default is 4.</param>
/// <param name="Format">The format of the game, for example <c>commander</c>. The default is the format of the deck, or <c>commander</c> for a deck text.</param>
/// <param name="Nickname">The name of the host in the game. The default is the display name of the account.</param>
public sealed record CreateGameRequest(Guid? DeckId, string? DeckText, int? Seats, string? Format, string? Nickname);

/// <summary>
/// The request to join an online game with an invite link.
/// </summary>
/// <param name="InviteSecret">The secret of the invite link: the text after <c>#</c> in the link.</param>
/// <param name="DeckId">The ID of a deck that the player can see. A guest can use an unlisted or public deck. Give the deck ID or the deck text.</param>
/// <param name="DeckText">A deck text in the format of the deck import. Give the deck ID or the deck text.</param>
/// <param name="Nickname">The name of the player in the game. A guest must give it. The default for an account is its display name.</param>
public sealed record JoinGameRequest(string? InviteSecret, Guid? DeckId, string? DeckText, string? Nickname);

/// <summary>
/// A seat and its game token: the response to a new game and to a join.
/// </summary>
/// <param name="GameId">The ID of the game.</param>
/// <param name="Seat">The seat of the caller. It is also the ID of the player in the table and in the views: <c>"0"</c> to <c>"3"</c>.</param>
/// <param name="GameToken">
/// The game token of the seat. Send it in the header <c>X-Game-Token</c> to the game endpoints, and as <c>access_token</c> to the hub.
/// It gives access to this seat of this game only. A new join to the same seat or a kick makes it not valid.
/// </param>
/// <param name="TokenExpiresAt">The time when the game token stops being valid.</param>
/// <param name="InviteSecret">The secret of the invite link, only for the host of a new game. Else <see langword="null"/>.</param>
/// <param name="InviteExpiresAt">The time when the invite link stops being valid, only for the host of a new game. Else <see langword="null"/>.</param>
/// <param name="Lobby">The lobby of the game.</param>
public sealed record GameSeatResponse(
    Guid GameId,
    string Seat,
    string GameToken,
    DateTimeOffset TokenExpiresAt,
    string? InviteSecret,
    DateTimeOffset? InviteExpiresAt,
    GameLobbyResponse Lobby);

/// <summary>
/// A new invite link of a game.
/// </summary>
/// <param name="InviteSecret">The secret. The API keeps only its hash, so it cannot give the secret again. Make a new link when you lose it.</param>
/// <param name="InviteExpiresAt">The time when the link stops being valid. The start of the game also stops the link.</param>
public sealed record GameInviteResponse(string InviteSecret, DateTimeOffset InviteExpiresAt);

/// <summary>
/// The lobby of an online game, as one seat sees it. The first fields are the fields of the hub message <c>Lobby</c>.
/// </summary>
/// <param name="Seats">The occupied seats.</param>
/// <param name="HostSeat">The seat of the host.</param>
/// <param name="Size">The number of seats of the game.</param>
/// <param name="Started">Whether the game started.</param>
/// <param name="GameId">The ID of the game.</param>
/// <param name="YourSeat">The seat of the caller.</param>
/// <param name="Format">The format of the game.</param>
/// <param name="CreatedAt">The time when the host created the game.</param>
/// <param name="EndsAt">The time when the game closes, also with actions.</param>
/// <param name="InviteExpiresAt">The time when the invite link stops being valid.</param>
public sealed record GameLobbyResponse(
    IReadOnlyList<GameSeatSummary> Seats,
    string HostSeat,
    int Size,
    bool Started,
    Guid GameId,
    string YourSeat,
    string Format,
    DateTimeOffset CreatedAt,
    DateTimeOffset EndsAt,
    DateTimeOffset InviteExpiresAt);

/// <summary>
/// An occupied seat in the lobby. The lobby never gives the cards of the deck of a player.
/// </summary>
/// <param name="Seat">The seat: <c>"0"</c> to <c>"3"</c>.</param>
/// <param name="Nickname">The name of the player.</param>
/// <param name="Ready">Whether the player has a deck. A player gets a seat only with a deck, so the value is always true.</param>
/// <param name="Guest">Whether the player is a guest.</param>
/// <param name="CardCount">The number of cards of the deck.</param>
/// <param name="DeckName">The name of the deck, only for the seat of the caller. Else <see langword="null"/>.</param>
public sealed record GameSeatSummary(string Seat, string Nickname, bool Ready, bool Guest, int CardCount, string? DeckName);

/// <summary>
/// One line of a deck text that the API cannot use.
/// </summary>
/// <param name="Line">The number of the line. The first line has the number 1.</param>
/// <param name="Code">The reason code, for example <c>card_not_found</c>.</param>
/// <param name="Text">The text of the line.</param>
public sealed record GameDeckLineError(int Line, string Code, string Text);
