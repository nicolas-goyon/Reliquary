using Microsoft.Extensions.Logging;

namespace Reliquary.Platform;

/// <summary>
/// The log messages of the platform. The messages follow the strict mode of the writing rules.
/// </summary>
internal static partial class PlatformLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "The runner applied {Count} scripts of the set {Owner}/{Schema} to the database {Database}.")]
    public static partial void MigrationSetApplied(this ILogger logger, int count, string owner, string schema, string database);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "The host applied {Count} migration scripts at start-up.")]
    public static partial void MigratedOnStartup(this ILogger logger, int count);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Error, Message = "The outbox publisher failed. It tries again after {Delay}.")]
    public static partial void OutboxPublisherFailed(this ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "A consumer failed to process the event {EventId} of the type {EventType}.")]
    public static partial void EventConsumerFailed(this ILogger logger, Exception exception, Guid eventId, string eventType);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "The job {Job} starts.")]
    public static partial void JobStarted(this ILogger logger, string job);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "The job {Job} completed.")]
    public static partial void JobCompleted(this ILogger logger, string job);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Error, Message = "The job {Job} failed.")]
    public static partial void JobFailed(this ILogger logger, Exception exception, string job);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Error, Message = "The runner could not check the job {Job}. It tries again after {Delay}.")]
    public static partial void JobCheckFailed(this ILogger logger, Exception exception, string job, TimeSpan delay);
}
