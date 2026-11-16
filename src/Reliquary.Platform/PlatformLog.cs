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

    [LoggerMessage(EventId = 1400, Level = LogLevel.Error, Message = "The catalog sync refused the catalog index ({Status}). The instance keeps its last index. {Reason}")]
    public static partial void CatalogIndexRefused(this ILogger logger, string status, string reason);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "The catalog sync accepted the catalog index {Serial}, signed with the key {KeyId}.")]
    public static partial void CatalogIndexAccepted(this ILogger logger, long serial, string keyId);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Information, Message = "The catalog sync added the files of the plugin {PluginId} {Version}.")]
    public static partial void CatalogVersionAdded(this ILogger logger, string pluginId, string version);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Warning, Message = "The catalog sync did not add the plugin {PluginId} {Version}. It tries again at the next sync. {Reason}")]
    public static partial void CatalogVersionFailed(this ILogger logger, string pluginId, string version, string reason);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "The instance has no SMTP server, so the email goes to the log. To: {To}. Subject: {Subject}.\n{Body}")]
    public static partial void EmailWrittenToLog(this ILogger logger, string to, string subject, string body);
}
