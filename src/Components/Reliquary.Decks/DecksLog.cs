using Microsoft.Extensions.Logging;

namespace Reliquary.Decks;

/// <summary>
/// The log messages of the Decks component. The messages follow the strict mode of the writing rules.
/// </summary>
internal static partial class DecksLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning, Message = "The recommendation source {Source} failed for the commanders {Commanders}. Reason: {Reason}. The API keeps the failure in the cache for a short time.")]
    public static partial void RecommendationSourceFailed(this ILogger logger, string source, string commanders, string reason);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "The API sent {Limit} requests to the recommendation source {Source} in this minute, or Redis is not available. The API sends no more requests in this minute.")]
    public static partial void RecommendationRateLimit(this ILogger logger, string source, int limit);
}
