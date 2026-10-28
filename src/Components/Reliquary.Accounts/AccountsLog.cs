using Microsoft.Extensions.Logging;

namespace Reliquary.Accounts;

/// <summary>
/// The log messages of the Accounts component. The messages follow the strict mode of the writing rules.
/// The messages never contain a password, a token or an email address.
/// </summary>
internal static partial class AccountsLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "The account {AccountId} was created with the method {Method}.")]
    public static partial void AccountCreated(this ILogger logger, Guid accountId, string method);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "A used refresh token of the account {AccountId} came back. The API revoked the session family {FamilyId}.")]
    public static partial void RefreshTokenReused(this ILogger logger, Guid accountId, Guid familyId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "The OAuth login {Provider} was linked to the account {AccountId}.")]
    public static partial void ExternalLoginLinked(this ILogger logger, string provider, Guid accountId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "The OAuth sign-in with {Provider} failed: {Reason}")]
    public static partial void ExternalSignInFailed(this ILogger logger, string provider, string reason);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error, Message = "The API could not send an email of the kind {Kind} to the account {AccountId}.")]
    public static partial void EmailFailed(this ILogger logger, Exception exception, string kind, Guid accountId);

    [LoggerMessage(EventId = 3100, Level = LogLevel.Warning, Message = "Redis is not available, so the API did not count the request of the API key {KeyId}.")]
    public static partial void QuotaUnavailable(this ILogger logger, Exception exception, Guid keyId);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "The job wrote {Count} usage counters to the database.")]
    public static partial void UsageFlushed(this ILogger logger, int count);

    [LoggerMessage(EventId = 3200, Level = LogLevel.Error, Message = "The export {ExportId} of the account {AccountId} failed.")]
    public static partial void ExportFailed(this ILogger logger, Exception exception, Guid exportId, Guid accountId);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Information, Message = "The worker deleted the account {AccountId} and its data.")]
    public static partial void AccountDeleted(this ILogger logger, Guid accountId);

    [LoggerMessage(EventId = 3300, Level = LogLevel.Information, Message = "The service account {ServiceAccountId} with the name {Name} was created.")]
    public static partial void ServiceAccountCreated(this ILogger logger, Guid serviceAccountId, string name);

    [LoggerMessage(EventId = 3301, Level = LogLevel.Information, Message = "The service account {ServiceAccountId} and its delegations were revoked.")]
    public static partial void ServiceAccountRevoked(this ILogger logger, Guid serviceAccountId);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Information, Message = "The account {AccountId} gave the delegation {DelegationId} to the service account {ServiceAccountId} with the scopes {Scopes}.")]
    public static partial void DelegationCreated(this ILogger logger, Guid accountId, Guid delegationId, Guid serviceAccountId, IReadOnlyList<string> scopes);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Information, Message = "The account {AccountId} revoked the delegation {DelegationId}.")]
    public static partial void DelegationRevoked(this ILogger logger, Guid accountId, Guid delegationId);
}
