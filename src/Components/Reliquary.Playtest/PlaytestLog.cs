using Microsoft.Extensions.Logging;

namespace Reliquary.Playtest;

/// <summary>
/// The log messages of the Playtest component. The messages follow the strict mode of the writing rules.
/// </summary>
internal static partial class PlaytestLog
{
    [LoggerMessage(EventId = 6000, Level = LogLevel.Warning, Message = "The table hub cannot read or write the online games in Redis. The hub refused the call.")]
    public static partial void StoreUnavailable(this ILogger logger, Exception exception);
}
