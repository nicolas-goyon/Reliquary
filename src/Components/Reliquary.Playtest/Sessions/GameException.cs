using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Decks;

namespace Reliquary.Playtest.Sessions;

/// <summary>
/// The error codes of the online games. See docs/architecture/online-games.md.
/// </summary>
public static class PlaytestErrorCodes
{
    /// <summary>The game does not exist, it ended, the invite secret is not correct, or the invite link expired.</summary>
    public const string InviteInvalid = "game_invite_invalid";

    /// <summary>The game token is missing, is not valid, expired, or a newer token or a kick replaced it.</summary>
    public const string TokenInvalid = "game_token_invalid";

    /// <summary>All the seats of the game have a player.</summary>
    public const string GameFull = "game_full";

    /// <summary>The game started. New players cannot join it.</summary>
    public const string GameStarted = "game_started";

    /// <summary>The account has the maximum number of open games.</summary>
    public const string OpenGameLimit = "game_open_limit";

    /// <summary>The deck text has lines that the API cannot use. It is the same code as the deck text of the Decks endpoints.</summary>
    public const string DeckTextInvalid = "deck_text_invalid";
}

/// <summary>
/// An error of an online game operation, with the status and the stable code of the API response.
/// </summary>
internal sealed class GameException(int status, string code, string message, IDictionary<string, object?>? extensions = null) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    public IDictionary<string, object?>? Extensions { get; } = extensions;

    public static GameException InviteInvalid() => new(
        StatusCodes.Status404NotFound,
        PlaytestErrorCodes.InviteInvalid,
        "The invite link is not valid: the game does not exist, the game ended, or the link expired. Ask the host for a new link.");

    public static GameException TokenInvalid() => new(
        StatusCodes.Status401Unauthorized,
        PlaytestErrorCodes.TokenInvalid,
        "The game token is missing or not valid. Join the game again to get a new token.");

    public static GameException HostOnly() => new(
        StatusCodes.Status403Forbidden,
        ErrorCodes.Forbidden,
        "Only the host of the game can do this operation.");

    public static GameException Validation(params FieldError[] errors) => new(
        StatusCodes.Status400BadRequest,
        ErrorCodes.ValidationFailed,
        "One or more fields of the request are not valid. Correct the fields in the errors list.",
        new Dictionary<string, object?> { [ApiProblems.ErrorsMember] = errors });

    public static GameException DeckText(GameDeckTextException exception) => new(
        StatusCodes.Status400BadRequest,
        PlaytestErrorCodes.DeckTextInvalid,
        exception.Message,
        new Dictionary<string, object?> { ["lineErrors"] = exception.LineErrors });
}
