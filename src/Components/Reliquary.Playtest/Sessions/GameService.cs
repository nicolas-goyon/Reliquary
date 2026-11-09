using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Decks;
using Reliquary.Playtest.Engine;

namespace Reliquary.Playtest.Sessions;

/// <summary>
/// The deck of a request: the ID of a deck, or a deck text. One of the two is necessary.
/// </summary>
/// <param name="DeckId">The ID of a deck that the player can see, or <see langword="null"/>.</param>
/// <param name="DeckText">A deck text, or <see langword="null"/>.</param>
internal sealed record DeckChoice(Guid? DeckId, string? DeckText);

/// <summary>
/// A player who gets a seat: the account (or <see langword="null"/> for a guest), the nickname and the deck.
/// </summary>
internal sealed record SeatRequest(Guid? AccountId, string Nickname, DeckChoice Deck);

/// <summary>
/// A seat and its new game token.
/// </summary>
/// <param name="Game">The game after the change.</param>
/// <param name="Seat">The seat of the player.</param>
/// <param name="Token">The game token.</param>
/// <param name="TokenExpiresAt">The time when the token stops being valid.</param>
internal sealed record SeatGrant(OnlineGame Game, int Seat, string Token, DateTimeOffset TokenExpiresAt);

/// <summary>
/// Creates the online games, gives the seats and the game tokens, and keeps the lobby. See docs/architecture/online-games.md.
/// </summary>
/// <remarks>
/// Each change reads the game, changes it and writes it with a compare-and-set on the sequence number. After a conflict, the service reads the game again.
/// </remarks>
internal sealed class GameService(
    GameStore<OnlineGame> store,
    GameLimits limits,
    GameTokens tokens,
    IGameDeckSource decks,
    IOptions<GameOptions> options,
    TimeProvider time)
{
    /// <summary>The maximum length of a nickname.</summary>
    public const int MaxNicknameLength = 24;

    /// <summary>The number of attempts of a change after conflicts.</summary>
    private const int MaxAttempts = 8;

    private GameOptions Settings => options.Value;

    /// <summary>
    /// Creates a game. The host gets the seat 0, an invite secret and a game token.
    /// </summary>
    /// <param name="host">The host and the deck of the host.</param>
    /// <param name="format">The format of the game, or <see langword="null"/> for the format of the deck of the host.</param>
    /// <param name="seatCount">The number of seats.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The seat of the host, and the invite secret.</returns>
    public async Task<(SeatGrant Grant, string InviteSecret)> CreateAsync(SeatRequest host, string? format, int seatCount, CancellationToken cancellationToken)
    {
        var hostId = host.AccountId ?? throw new ArgumentException("Only an account can create a game.", nameof(host));
        var (deck, deckFormat) = await ResolveDeckAsync(host.AccountId, host.Deck, cancellationToken);
        var gameId = Ids.New();
        switch (await limits.TryCreateAsync(hostId, gameId))
        {
            case CreateLimitResult.TooManyOpenGames:
                throw new GameException(
                    StatusCodes.Status409Conflict,
                    PlaytestErrorCodes.OpenGameLimit,
                    $"You have {Settings.OpenGamesPerAccount} open games. Close a game, then try again.");
            case CreateLimitResult.TooManyGamesToday:
                throw new GameException(
                    StatusCodes.Status429TooManyRequests,
                    ErrorCodes.RateLimited,
                    $"You created {Settings.GamesPerAccountPerDay} games today. Wait until 00:00 UTC, then try again.");
        }

        var now = time.GetUtcNow();
        var endsAt = now + Settings.GameLifetime;
        var (secret, hash) = NewInvite();
        var (token, tokenId, tokenExpiresAt) = NewToken(gameId, OnlineGame.HostSeat, endsAt);
        var game = new OnlineGame(
            gameId,
            hostId,
            format ?? deckFormat ?? "commander",
            seatCount,
            now,
            endsAt,
            GameStatuses.Lobby,
            hash,
            Min(now + Settings.InviteLifetime, endsAt),
            [new GameSeat(OnlineGame.HostSeat, hostId, host.Nickname, tokenId, deck, now)]);
        if (!await store.CreateAsync(GameKeys.Id(gameId), game, Change(OnlineGame.HostSeat, "create")))
        {
            throw new InvalidOperationException($"A game with the new ID {gameId} exists.");
        }

        return (new SeatGrant(game, OnlineGame.HostSeat, token, tokenExpiresAt), secret);
    }

    /// <summary>
    /// Gives a seat of a game to a player with the invite secret. An account that has a seat gets its seat again, with a new token.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="inviteSecret">The secret of the invite link.</param>
    /// <param name="player">The player and the deck.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The seat and its game token.</returns>
    public async Task<SeatGrant> JoinAsync(Guid gameId, string inviteSecret, SeatRequest player, CancellationToken cancellationToken)
    {
        // The service checks the invite before it reads the deck, so a wrong secret cannot use the card catalog.
        var first = await ReadAsync(gameId) ?? throw GameException.InviteInvalid();
        CheckInvite(first.State, inviteSecret);
        var (deck, _) = await ResolveDeckAsync(player.AccountId, player.Deck, cancellationToken);

        SeatGrant? grant = null;
        await ChangeAsync(gameId, first, game =>
        {
            CheckInvite(game, inviteSecret);
            var seat = (player.AccountId is { } accountId ? game.FindAccountSeat(accountId)?.Seat : null)
                ?? Enumerable.Range(0, game.SeatCount).Cast<int?>().FirstOrDefault(number => game.FindSeat(number!.Value) is null)
                ?? throw new GameException(StatusCodes.Status409Conflict, PlaytestErrorCodes.GameFull, "All the seats of the game have a player. Ask the host to remove a player.");
            var (token, tokenId, expiresAt) = NewToken(gameId, seat, game.EndsAt);
            var changed = game.WithSeat(new GameSeat(seat, player.AccountId, player.Nickname, tokenId, deck, time.GetUtcNow()));
            grant = new SeatGrant(changed, seat, token, expiresAt);
            return (changed, Change(seat, "join"));
        });
        return grant!;
    }

    /// <summary>
    /// Gets a game for the holder of a game token.
    /// </summary>
    /// <param name="gameId">The game of the request.</param>
    /// <param name="token">The game token.</param>
    /// <returns>The game and the seat of the token.</returns>
    public async Task<(OnlineGame Game, int Seat)> GetAsync(Guid gameId, string? token)
    {
        var (stored, seat) = await AuthorizeAsync(gameId, token);
        return (stored.State, seat);
    }

    /// <summary>
    /// Makes a new invite secret. The old invite link stops being valid. Only the host can do it, before the start.
    /// </summary>
    /// <returns>The game and the new secret.</returns>
    public async Task<(OnlineGame Game, string InviteSecret)> NewInviteAsync(Guid gameId, string? token)
    {
        var (stored, seat) = await AuthorizeAsync(gameId, token);
        string? secret = null;
        var game = await ChangeAsync(gameId, stored, game =>
        {
            CheckHost(seat);
            CheckLobby(game);
            var (newSecret, hash) = NewInvite();
            secret = newSecret;
            var now = time.GetUtcNow();
            return (game with { InviteHash = hash, InviteExpiresAt = Min(now + Settings.InviteLifetime, game.EndsAt) }, Change(seat, "invite"));
        });
        return (game, secret!);
    }

    /// <summary>
    /// Removes a player from a seat. The game token of the seat stops being valid. Only the host can do it, and the host cannot remove the host.
    /// </summary>
    public async Task<OnlineGame> KickAsync(Guid gameId, string? token, int kickedSeat)
    {
        var (stored, seat) = await AuthorizeAsync(gameId, token);
        return await ChangeAsync(gameId, stored, game =>
        {
            CheckHost(seat);
            CheckLobby(game);
            if (kickedSeat == OnlineGame.HostSeat || game.FindSeat(kickedSeat) is null)
            {
                throw ApiNotFound("The seat has no player, or it is the seat of the host.");
            }

            return (game with { Seats = [.. game.Seats.Where(item => item.Seat != kickedSeat)] }, Change(seat, "kick"));
        });
    }

    /// <summary>
    /// Closes a game. All its data goes out of Redis. Only the host can do it.
    /// </summary>
    public async Task CloseAsync(Guid gameId, string? token)
    {
        var (stored, seat) = await AuthorizeAsync(gameId, token);
        CheckHost(seat);
        await DeleteAsync(stored.State);
    }

    /// <summary>
    /// Reads a game. A game after its hard limit closes: the service deletes it.
    /// </summary>
    private async Task<StoredGame<OnlineGame>?> ReadAsync(Guid gameId)
    {
        var stored = await store.GetAsync(GameKeys.Id(gameId));
        if (stored is not null && stored.State.IsOver(time.GetUtcNow()))
        {
            await DeleteAsync(stored.State);
            return null;
        }

        return stored;
    }

    private async Task DeleteAsync(OnlineGame game)
    {
        await store.DeleteAsync(GameKeys.Id(game.Id));
        await limits.CloseAsync(game.HostAccountId, game.Id);
    }

    private async Task<(StoredGame<OnlineGame> Game, int Seat)> AuthorizeAsync(Guid gameId, string? token)
    {
        var claims = await tokens.ReadAsync(token);
        if (claims is null || claims.GameId != gameId)
        {
            throw GameException.TokenInvalid();
        }

        var stored = await ReadAsync(gameId) ?? throw ApiNotFound("The game does not exist or it ended.");
        if (stored.State.FindSeat(claims.Seat)?.TokenId != claims.TokenId)
        {
            throw GameException.TokenInvalid();
        }

        return (stored, claims.Seat);
    }

    /// <summary>
    /// Applies a change to a game with a compare-and-set. After a conflict, the service reads the game again and applies the change again.
    /// </summary>
    private async Task<OnlineGame> ChangeAsync(Guid gameId, StoredGame<OnlineGame> first, Func<OnlineGame, (OnlineGame Game, GameChange Change)> change)
    {
        var current = first;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var (game, entry) = change(current.State);
            var result = await store.WriteAsync(GameKeys.Id(gameId), current.Seq, game, entry);
            switch (result.Status)
            {
                case GameWriteStatus.Written:
                    return game;
                case GameWriteStatus.NotFound:
                    throw ApiNotFound("The game does not exist or it ended.");
            }

            current = await ReadAsync(gameId) ?? throw ApiNotFound("The game does not exist or it ended.");
        }

        throw new GameException(StatusCodes.Status503ServiceUnavailable, ErrorCodes.ServiceUnavailable, "The game changed too many times during the request. Try again.");
    }

    private async Task<(SeatDeck Deck, string? Format)> ResolveDeckAsync(Guid? accountId, DeckChoice choice, CancellationToken cancellationToken)
    {
        GameDeck deck;
        if (choice.DeckId is { } deckId)
        {
            deck = await decks.GetDeckAsync(deckId, accountId, cancellationToken)
                ?? throw ApiNotFound("The deck does not exist, or you cannot see it. A guest can use only an unlisted or public deck.");
        }
        else
        {
            try
            {
                deck = await decks.ParseDeckTextAsync(choice.DeckText!, cancellationToken);
            }
            catch (GameDeckTextException exception)
            {
                throw GameException.DeckText(exception);
            }
        }

        var field = choice.DeckId is null ? "deckText" : "deckId";
        var count = deck.CardCount;
        if (count == 0 || count > Settings.MaxDeckCards)
        {
            throw GameException.Validation(new FieldError(field, FieldErrorCodes.OutOfRange, $"A deck for a game must have 1 to {Settings.MaxDeckCards} cards in the main zone and the command zone."));
        }

        return (new SeatDeck(deck.Name, Copies(deck.Library), Copies(deck.Command)), deck.Format);
    }

    /// <summary>
    /// Makes one card for each copy, with a random ID. The ID does not tell the deck line or the order of the deck.
    /// </summary>
    private static List<SetupCard> Copies(IReadOnlyList<GameDeckCard> cards) =>
        [.. cards.SelectMany(card => Enumerable.Range(0, card.Quantity).Select(_ => new SetupCard(
            NewCardId(),
            card.OracleId.ToString(),
            card.Name,
            card.ImageUri,
            card.IsLand,
            (double)card.ManaValue)))];

    /// <summary>Gives a random card ID: 96 random bits, in base64url. Two cards of a game have the same ID with a probability of nearly zero.</summary>
    private static string NewCardId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(12));

    private (string Token, string TokenId, DateTimeOffset ExpiresAt) NewToken(Guid gameId, int seat, DateTimeOffset endsAt)
    {
        var expiresAt = Min(time.GetUtcNow() + Settings.TokenLifetime, endsAt);
        var (token, tokenId) = tokens.Create(gameId, seat, expiresAt);
        return (token, tokenId, expiresAt);
    }

    private void CheckInvite(OnlineGame game, string secret)
    {
        if (time.GetUtcNow() >= game.InviteExpiresAt || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(game.InviteHash)))
        {
            throw GameException.InviteInvalid();
        }

        if (game.Status != GameStatuses.Lobby)
        {
            throw new GameException(StatusCodes.Status409Conflict, PlaytestErrorCodes.GameStarted, "The game started. New players cannot join it.");
        }
    }

    private static void CheckHost(int seat)
    {
        if (seat != OnlineGame.HostSeat)
        {
            throw GameException.HostOnly();
        }
    }

    private static void CheckLobby(OnlineGame game)
    {
        if (game.Status != GameStatuses.Lobby)
        {
            throw new GameException(StatusCodes.Status409Conflict, PlaytestErrorCodes.GameStarted, "The game started. The lobby cannot change.");
        }
    }

    /// <summary>Gives a new invite secret of 128 random bits in base64url, and its hash.</summary>
    private static (string Secret, string Hash) NewInvite()
    {
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        return (secret, Hash(secret));
    }

    private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static GameChange Change(int seat, string type) => new(seat, null, $$"""{"type":"{{type}}"}""");

    private static GameException ApiNotFound(string message) => new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message);

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;
}
