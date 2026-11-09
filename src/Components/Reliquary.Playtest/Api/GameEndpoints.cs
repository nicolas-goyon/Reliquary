using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Decks;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Playtest.Sessions;
using StackExchange.Redis;

namespace Reliquary.Playtest.Api;

/// <summary>
/// The endpoints of the online games: create, join, lobby, invite link, kick and close. See docs/architecture/online-games.md.
/// </summary>
internal static class GameEndpoints
{
    /// <summary>The header of the game token.</summary>
    public const string TokenHeader = "X-Game-Token";

    private const int MaxFormatLength = 32;

    public static void Map(IEndpointRouteBuilder api)
    {
        var games = api.MapGroup("/playtest/games").WithTags("Online games").AddEndpointFilter<GameExceptionFilter>();

        games.MapPost(string.Empty, CreateAsync)
            .WithName("CreateOnlineGame")
            .WithSummary("Creates an online game. The host gets the seat 0, a game token and an invite link. Only an account can create a game.")
            .RequireAuthorization(AuthPolicies.User)
            .RequireScope(Scopes.DecksRead);

        games.MapPost("/{gameId:guid}/join", JoinAsync)
            .WithName("JoinOnlineGame")
            .WithSummary("Joins an online game with the secret of the invite link. An account or a guest gets a seat and a game token. An account that has a seat gets its seat again.")
            .RequireScope(Scopes.DecksRead);

        games.MapGet("/{gameId:guid}", GetAsync)
            .WithName("GetOnlineGame")
            .WithSummary("Gets the lobby of an online game. Send the game token in the header X-Game-Token.");

        games.MapPost("/{gameId:guid}/invite", NewInviteAsync)
            .WithName("CreateOnlineGameInvite")
            .WithSummary("Makes a new invite link. The old link stops being valid. Only the host can do it, before the start. Send the game token in the header X-Game-Token.");

        games.MapDelete("/{gameId:guid}/seats/{seat:int}", KickAsync)
            .WithName("KickOnlineGamePlayer")
            .WithSummary("Removes a player from the game. The game token of the player stops being valid. Only the host can do it, before the start. Send the game token in the header X-Game-Token.");

        games.MapDelete("/{gameId:guid}", CloseAsync)
            .WithName("CloseOnlineGame")
            .WithSummary("Closes an online game and deletes all its data. Only the host can do it. Send the game token in the header X-Game-Token.");
    }

    private static async Task<Results<Created<GameSeatResponse>, ProblemHttpResult>> CreateAsync(
        CreateGameRequest request, ClaimsPrincipal user, GameService games, IOptions<GameOptions> options, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var errors = new List<FieldError>();
        var format = request.Format?.Trim();
        if (format is not null && (format.Length is 0 or > MaxFormatLength || !format.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character))))
        {
            errors.Add(new FieldError("format", FieldErrorCodes.InvalidFormat, $"The format must have 1 to {MaxFormatLength} lowercase letters or digits, for example commander."));
        }

        var seats = request.Seats ?? settings.MaxSeats;
        if (seats < settings.MinSeats || seats > settings.MaxSeats)
        {
            errors.Add(new FieldError("seats", FieldErrorCodes.OutOfRange, $"The number of seats must be from {settings.MinSeats} to {settings.MaxSeats}."));
        }

        var nickname = ValidateNickname(errors, request.Nickname, user.Identity?.Name);
        var deck = ValidateDeck(errors, request.DeckId, request.DeckText, settings);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var (grant, secret) = await games.CreateAsync(new SeatRequest(user.GetAccountId(), nickname!, deck!), format, seats, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/playtest/games/{grant.Game.Id}", ToResponse(grant, secret));
    }

    private static async Task<Results<Ok<GameSeatResponse>, ProblemHttpResult>> JoinAsync(
        Guid gameId, JoinGameRequest request, ClaimsPrincipal user, HttpContext http, GameService games, GameLimits limits, IOptions<GameOptions> options, CancellationToken cancellationToken)
    {
        if (!await limits.TryCountJoinAsync(http.Connection.RemoteIpAddress?.ToString() ?? "unknown"))
        {
            http.Response.Headers.RetryAfter = "60";
            return ApiProblems.Problem(StatusCodes.Status429TooManyRequests, ErrorCodes.RateLimited, "The client sent too many join requests. Wait one minute, then try again.");
        }

        var settings = options.Value;
        var errors = new List<FieldError>();
        if (string.IsNullOrEmpty(request.InviteSecret) || request.InviteSecret.Length > 64)
        {
            errors.Add(new FieldError("inviteSecret", FieldErrorCodes.Required, "The secret of the invite link is necessary: the text after # in the link."));
        }

        var accountId = user.FindAccountId();
        var nickname = ValidateNickname(errors, request.Nickname, accountId is null ? null : user.Identity?.Name);
        var deck = ValidateDeck(errors, request.DeckId, request.DeckText, settings);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var grant = await games.JoinAsync(gameId, request.InviteSecret!, new SeatRequest(accountId, nickname!, deck!), cancellationToken);
        return TypedResults.Ok(ToResponse(grant, null));
    }

    private static async Task<Ok<GameLobbyResponse>> GetAsync(Guid gameId, HttpRequest request, GameService games)
    {
        var (game, seat) = await games.GetAsync(gameId, Token(request));
        return TypedResults.Ok(ToLobby(game, seat));
    }

    private static async Task<Ok<GameInviteResponse>> NewInviteAsync(Guid gameId, HttpRequest request, GameService games)
    {
        var (game, secret) = await games.NewInviteAsync(gameId, Token(request));
        return TypedResults.Ok(new GameInviteResponse(secret, game.InviteExpiresAt));
    }

    private static async Task<Ok<GameLobbyResponse>> KickAsync(Guid gameId, int seat, HttpRequest request, GameService games)
    {
        var game = await games.KickAsync(gameId, Token(request), seat);
        return TypedResults.Ok(ToLobby(game, OnlineGame.HostSeat));
    }

    private static async Task<NoContent> CloseAsync(Guid gameId, HttpRequest request, GameService games)
    {
        await games.CloseAsync(gameId, Token(request));
        return TypedResults.NoContent();
    }

    private static string? Token(HttpRequest request) => request.Headers[TokenHeader].ToString() is { Length: > 0 } token ? token : null;

    private static string? ValidateNickname(List<FieldError> errors, string? nickname, string? fallback)
    {
        var value = nickname?.Trim() ?? fallback?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            errors.Add(new FieldError("nickname", FieldErrorCodes.Required, "The nickname is necessary for a guest."));
            return null;
        }

        if (value.Any(char.IsControl))
        {
            errors.Add(new FieldError("nickname", FieldErrorCodes.InvalidFormat, "The nickname must not contain control characters."));
            return null;
        }

        if (nickname is null && value.Length > GameService.MaxNicknameLength)
        {
            // The display name of an account can be longer than a nickname.
            return value[..GameService.MaxNicknameLength].TrimEnd();
        }

        if (value.Length > GameService.MaxNicknameLength)
        {
            errors.Add(new FieldError("nickname", FieldErrorCodes.TooLong, $"The nickname must have {GameService.MaxNicknameLength} characters or fewer."));
            return null;
        }

        return value;
    }

    private static DeckChoice? ValidateDeck(List<FieldError> errors, Guid? deckId, string? deckText, GameOptions settings)
    {
        if ((deckId is null) == string.IsNullOrWhiteSpace(deckText))
        {
            errors.Add(new FieldError("deckId", FieldErrorCodes.Required, "Give a deck ID or a deck text, not the two."));
            return null;
        }

        if (deckText is not null && deckId is null)
        {
            if (Encoding.UTF8.GetByteCount(deckText) > settings.MaxDeckTextBytes)
            {
                errors.Add(new FieldError("deckText", FieldErrorCodes.TooLong, $"The deck text must have {settings.MaxDeckTextBytes} bytes or fewer."));
                return null;
            }

            if (deckText.Count(character => character == '\n') + 1 > settings.MaxDeckTextLines)
            {
                errors.Add(new FieldError("deckText", FieldErrorCodes.TooLong, $"The deck text must have {settings.MaxDeckTextLines} lines or fewer."));
                return null;
            }
        }

        return new DeckChoice(deckId, deckId is null ? deckText : null);
    }

    private static GameSeatResponse ToResponse(SeatGrant grant, string? inviteSecret) => new(
        grant.Game.Id,
        OnlineGame.PlayerId(grant.Seat),
        grant.Token,
        grant.TokenExpiresAt,
        inviteSecret,
        inviteSecret is null ? null : grant.Game.InviteExpiresAt,
        ToLobby(grant.Game, grant.Seat));

    private static GameLobbyResponse ToLobby(OnlineGame game, int seat) => new(
        [.. game.Seats.Select(item => new GameSeatSummary(
            OnlineGame.PlayerId(item.Seat),
            item.Nickname,
            Ready: true,
            item.IsGuest,
            item.Deck.CardCount,
            item.Seat == seat ? item.Deck.Name : null))],
        OnlineGame.PlayerId(OnlineGame.HostSeat),
        game.SeatCount,
        game.Status == GameStatuses.Started,
        game.Id,
        OnlineGame.PlayerId(seat),
        game.Format,
        game.CreatedAt,
        game.EndsAt,
        game.InviteExpiresAt);
}

/// <summary>
/// Converts a <see cref="GameException"/> into a problem response, and a failure of Redis into the status 503.
/// </summary>
internal sealed class GameExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (GameException exception)
        {
            if (exception.Status == StatusCodes.Status401Unauthorized)
            {
                context.HttpContext.Response.Headers.WWWAuthenticate = "GameToken";
            }

            var extensions = exception.Extensions;
            if (extensions is not null && extensions.TryGetValue("lineErrors", out var lines) && lines is IReadOnlyList<GameDeckTextError> errors)
            {
                extensions = new Dictionary<string, object?>(extensions)
                {
                    ["lineErrors"] = errors.Select(error => new GameDeckLineError(error.Line, error.Code, error.Text)).ToList(),
                };
            }

            return ApiProblems.Problem(exception.Status, exception.Code, exception.Message, extensions);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
            return ApiProblems.Problem(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.ServiceUnavailable,
                "The store of the online games is not available. Try again later.");
        }
    }
}
