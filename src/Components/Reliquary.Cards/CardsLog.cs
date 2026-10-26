using Microsoft.Extensions.Logging;

namespace Reliquary.Cards;

/// <summary>
/// The log messages of the Cards component. The messages follow the strict mode of the writing rules.
/// </summary>
internal static partial class CardsLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "The source {Source} did not change since the last import. The import skips it.")]
    public static partial void SourceUnchanged(this ILogger logger, string source);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "The import of the source {Source} starts.")]
    public static partial void SourceImportStarted(this ILogger logger, string source);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "The import of the source {Source} completed. Items: {Count}.")]
    public static partial void SourceImportCompleted(this ILogger logger, string source, int count);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error, Message = "The import of the source {Source} failed.")]
    public static partial void SourceImportFailed(this ILogger logger, Exception exception, string source);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Warning, Message = "A card search stopped after {Milliseconds} ms. The query has {Length} characters.")]
    public static partial void SearchTimedOut(this ILogger logger, int length, int milliseconds);
}
