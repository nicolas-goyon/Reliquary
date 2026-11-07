using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;

namespace Reliquary.Decks.Recommendations;

/// <summary>
/// The recommendations of one commander pair, as the cache keeps them.
/// </summary>
/// <param name="Available">Whether the source gave an answer. <see langword="false"/> keeps a failure for a short time.</param>
/// <param name="Cards">The recommended cards, from the highest score.</param>
internal sealed record CachedRecommendations(bool Available, IReadOnlyList<SourceCard> Cards);

/// <summary>
/// Keeps the recommendations of each commander pair, and counts the requests to the source for the rate limit.
/// </summary>
internal interface IRecommendationCache
{
    /// <summary>
    /// Gets the recommendations of a key.
    /// </summary>
    /// <returns>The recommendations, or <see langword="null"/> when the cache does not have the key.</returns>
    Task<CachedRecommendations?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps the recommendations of a key for a time.
    /// </summary>
    Task SetAsync(string key, CachedRecommendations value, TimeSpan duration, CancellationToken cancellationToken);

    /// <summary>
    /// Counts one request to the source in the current minute.
    /// </summary>
    /// <param name="limit">The maximum number of requests in one minute.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns><see langword="true"/> when the request can go to the source.</returns>
    Task<bool> TryCountRequestAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the recommendations in Redis, so all the API replicas share the cache and the rate limit.
/// </summary>
/// <remarks>
/// When Redis is not available, the cache has no entry and the rate limit refuses each request.
/// The API then sends no request to the source, so a failure of Redis cannot send too many requests.
/// </remarks>
internal sealed class RedisRecommendationCache(Lazy<IConnectionMultiplexer> redis, TimeProvider time) : IRecommendationCache
{
    /// <summary>The start of all the Redis keys of the recommendations.</summary>
    public const string KeyPrefix = "reliquary:recommendations:";

    private const string CountScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
          redis.call('EXPIRE', KEYS[1], 120)
        end
        return count
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CachedRecommendations?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var value = await redis.Value.GetDatabase().StringGetAsync(key);
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CachedRecommendations>(value.ToString(), Json);
        }
        catch (Exception exception) when (exception is RedisException or JsonException or TimeoutException)
        {
            return null;
        }
    }

    public async Task SetAsync(string key, CachedRecommendations value, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            await redis.Value.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value, Json), duration);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // The next request gets no entry and asks the source again, inside the rate limit.
        }
    }

    public async Task<bool> TryCountRequestAsync(int limit, CancellationToken cancellationToken)
    {
        var minute = time.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        try
        {
            var count = (long)(await redis.Value.GetDatabase().ScriptEvaluateAsync(CountScript, [(RedisKey)$"{KeyPrefix}rate:{minute}"]))!;
            return count <= limit;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            return false;
        }
    }
}
