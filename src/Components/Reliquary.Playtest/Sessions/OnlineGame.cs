using Reliquary.Playtest.Engine;

namespace Reliquary.Playtest.Sessions;

/// <summary>
/// The statuses of an online game. A closed game has no status: the store deletes it.
/// </summary>
public static class GameStatuses
{
    /// <summary>The players join the game and choose their decks.</summary>
    public const string Lobby = "lobby";

    /// <summary>The game started. The table has the state of the game.</summary>
    public const string Started = "started";
}

/// <summary>
/// An online game, as the game store keeps it in Redis. See docs/architecture/online-games.md.
/// </summary>
/// <param name="Id">The ID of the game.</param>
/// <param name="HostAccountId">The account that created the game.</param>
/// <param name="Format">The format of the game, for example <c>commander</c>.</param>
/// <param name="SeatCount">The number of seats, from 2 to 4.</param>
/// <param name="CreatedAt">The time when the host created the game.</param>
/// <param name="EndsAt">The time when the game closes, also with actions. It is the hard limit of the lifetime of a game.</param>
/// <param name="Status">The status. See <see cref="GameStatuses"/>.</param>
/// <param name="InviteHash">The SHA-256 hash of the secret of the invite link, in hexadecimal. The store never keeps the secret.</param>
/// <param name="InviteExpiresAt">The time when the invite link stops being valid.</param>
/// <param name="Seats">The occupied seats, in the order of the seat numbers. The host has the seat 0.</param>
/// <param name="Table">The state of the table after the start, or <see langword="null"/> in the lobby.</param>
public sealed record OnlineGame(
    Guid Id,
    Guid HostAccountId,
    string Format,
    int SeatCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset EndsAt,
    string Status,
    string InviteHash,
    DateTimeOffset InviteExpiresAt,
    IReadOnlyList<GameSeat> Seats,
    TableState? Table = null)
{
    /// <summary>The seat of the host.</summary>
    public const int HostSeat = 0;

    /// <summary>
    /// Gets the ID of the player of a seat in the table, in the views and in the API: the number of the seat as a text.
    /// </summary>
    /// <param name="seat">The number of the seat.</param>
    /// <returns>The ID, for example <c>"0"</c>.</returns>
    public static string PlayerId(int seat) => seat.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the occupied seat with a number.
    /// </summary>
    /// <param name="seat">The number of the seat.</param>
    /// <returns>The seat, or <see langword="null"/> when nobody has the seat.</returns>
    public GameSeat? FindSeat(int seat) => Seats.FirstOrDefault(item => item.Seat == seat);

    /// <summary>
    /// Gets the seat of an account.
    /// </summary>
    /// <param name="accountId">The account.</param>
    /// <returns>The seat, or <see langword="null"/> when the account has no seat in the game.</returns>
    public GameSeat? FindAccountSeat(Guid accountId) => Seats.FirstOrDefault(item => item.AccountId == accountId);

    /// <summary>
    /// Tells whether the game passed its hard limit.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when the game must close.</returns>
    public bool IsOver(DateTimeOffset now) => now >= EndsAt;

    /// <summary>
    /// Gives a copy of the game with a seat that replaces the seat with the same number, or with a new seat.
    /// </summary>
    /// <param name="seat">The seat.</param>
    /// <returns>The new game.</returns>
    public OnlineGame WithSeat(GameSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return this with { Seats = [.. Seats.Where(item => item.Seat != seat.Seat).Append(seat).OrderBy(item => item.Seat)] };
    }
}

/// <summary>
/// An occupied seat of an online game.
/// </summary>
/// <param name="Seat">The number of the seat, from 0.</param>
/// <param name="AccountId">The account of the player, or <see langword="null"/> for a guest.</param>
/// <param name="Nickname">The name of the player in the game.</param>
/// <param name="TokenId">The ID (<c>jti</c>) of the only valid game token of the seat. A new token or a kick makes the old token not valid.</param>
/// <param name="Deck">The deck of the player.</param>
/// <param name="JoinedAt">The time of the last join.</param>
public sealed record GameSeat(int Seat, Guid? AccountId, string Nickname, string TokenId, SeatDeck Deck, DateTimeOffset JoinedAt)
{
    /// <summary>Gets a value that tells whether the player is a guest.</summary>
    public bool IsGuest => AccountId is null;
}

/// <summary>
/// The deck of a seat, with a random ID for each copy of a card. The IDs do not tell the deck line of a card.
/// </summary>
/// <param name="Name">The name of the deck, or an empty text for a deck from a text.</param>
/// <param name="Library">The cards of the library.</param>
/// <param name="Command">The cards that start in the command zone.</param>
public sealed record SeatDeck(string Name, IReadOnlyList<SetupCard> Library, IReadOnlyList<SetupCard> Command)
{
    /// <summary>Gets the number of cards.</summary>
    public int CardCount => Library.Count + Command.Count;
}
