using System.Globalization;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Reliquary.Playtest.Sessions;

/// <summary>
/// The result of the check of the limits of a new game.
/// </summary>
internal enum CreateLimitResult
{
    /// <summary>The account can create the game. The limits counted it.</summary>
    Allowed,

    /// <summary>The account has the maximum number of open games.</summary>
    TooManyOpenGames,

    /// <summary>The account created the maximum number of games today.</summary>
    TooManyGamesToday,
}

/// <summary>
/// Gives the key of a game in the game store.
/// </summary>
internal static class GameKeys
{
    /// <summary>
    /// Gets the ID of a game in the Redis keys.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <returns>The ID with 32 hexadecimal characters.</returns>
    public static string Id(Guid gameId) => gameId.ToString("N");
}

/// <summary>
/// Counts the new games and the join requests in Redis, so all the API replicas share the limits.
/// </summary>
internal sealed class GameLimits(Lazy<IConnectionMultiplexer> redis, IOptions<GameOptions> options, TimeProvider time)
{
    /// <summary>The start of all the Redis keys of the limits.</summary>
    public const string KeyPrefix = "reliquary:playtest:limits:";

    // KEYS: 1 the set of the open games of the host, 2 the counter of the day.
    // ARGV: 1 the game ID, 2 the maximum of open games, 3 the maximum per day, 4 the lifetime of the set in seconds,
    // 5 the start of the state keys of the games, 6 the end of the state keys.
    // A game is open while its state key exists, so the script removes the games that ended or expired.
    // The script reads state keys that it does not get in KEYS. This is correct on one Redis server, not on a Redis cluster.
    // Returns 1 allowed, 2 too many open games, 3 too many games today.
    private const string CreateScript = """
        for _, game in ipairs(redis.call('SMEMBERS', KEYS[1])) do
          if redis.call('EXISTS', ARGV[5] .. game .. ARGV[6]) == 0 then
            redis.call('SREM', KEYS[1], game)
          end
        end
        if redis.call('SCARD', KEYS[1]) >= tonumber(ARGV[2]) then
          return 2
        end
        local today = tonumber(redis.call('GET', KEYS[2]) or '0')
        if today >= tonumber(ARGV[3]) then
          return 3
        end
        redis.call('INCR', KEYS[2])
        redis.call('EXPIRE', KEYS[2], 172800)
        redis.call('SADD', KEYS[1], ARGV[1])
        redis.call('EXPIRE', KEYS[1], ARGV[4])
        return 1
        """;

    private const string CountScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
          redis.call('EXPIRE', KEYS[1], 120)
        end
        return count
        """;

    /// <summary>
    /// Checks the limits of a new game of a host, and counts the game when the host can create it.
    /// </summary>
    /// <param name="accountId">The host.</param>
    /// <param name="gameId">The ID of the new game.</param>
    /// <returns>The result.</returns>
    public async Task<CreateLimitResult> TryCreateAsync(Guid accountId, Guid gameId)
    {
        var settings = options.Value;
        var day = time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var stateKey = GameStore<OnlineGame>.StateKey("GAME");
        var split = stateKey.IndexOf("GAME", StringComparison.Ordinal);
        var result = (long)await redis.Value.GetDatabase().ScriptEvaluateAsync(
            CreateScript,
            [$"{KeyPrefix}open:{accountId:N}", $"{KeyPrefix}created:{day}:{accountId:N}"],
            [
                GameKeys.Id(gameId),
                settings.OpenGamesPerAccount,
                settings.GamesPerAccountPerDay,
                (long)settings.GameLifetime.TotalSeconds,
                stateKey[..split],
                stateKey[(split + 4)..],
            ]);
        return result switch
        {
            1 => CreateLimitResult.Allowed,
            2 => CreateLimitResult.TooManyOpenGames,
            _ => CreateLimitResult.TooManyGamesToday,
        };
    }

    /// <summary>
    /// Removes a game from the open games of its host.
    /// </summary>
    /// <param name="accountId">The host.</param>
    /// <param name="gameId">The game.</param>
    /// <returns>A task that completes when the game is not in the set.</returns>
    public Task CloseAsync(Guid accountId, Guid gameId) =>
        redis.Value.GetDatabase().SetRemoveAsync($"{KeyPrefix}open:{accountId:N}", GameKeys.Id(gameId));

    /// <summary>
    /// Counts one join request of an IP address in the current minute.
    /// </summary>
    /// <param name="address">The IP address of the client.</param>
    /// <returns><see langword="true"/> when the request can continue.</returns>
    public async Task<bool> TryCountJoinAsync(string address)
    {
        var minute = time.GetUtcNow().ToUnixTimeSeconds() / 60;
        var count = (long)await redis.Value.GetDatabase().ScriptEvaluateAsync(CountScript, [$"{KeyPrefix}join:{minute}:{address}"]);
        return count <= options.Value.JoinsPerIpPerMinute;
    }
}
