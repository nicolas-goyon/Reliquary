namespace Reliquary.Accounts;

/// <summary>
/// The stable error codes of the Accounts endpoints. docs/architecture/api-conventions.md lists all the codes.
/// </summary>
public static class AccountErrorCodes
{
    /// <summary>The email address or the password is not correct.</summary>
    public const string InvalidCredentials = "invalid_credentials";

    /// <summary>The user must open the link of the email before the first sign-in.</summary>
    public const string EmailNotChecked = "email_not_checked";

    /// <summary>The link of the email is not valid: the user opened it before, it expired, or it is not complete.</summary>
    public const string InvalidToken = "invalid_token";

    /// <summary>The refresh cookie is missing, expired or revoked. The user must sign in again.</summary>
    public const string SessionExpired = "session_expired";

    /// <summary>The account has the maximum number of API keys.</summary>
    public const string ApiKeyLimit = "api_key_limit";

    /// <summary>The OAuth provider is not enabled on the instance.</summary>
    public const string ProviderNotEnabled = "provider_not_enabled";

    /// <summary>The OAuth provider gave no email address.</summary>
    public const string ExternalEmailMissing = "external_email_missing";

    /// <summary>The OAuth provider did not check the email address, so Reliquary cannot link it to an account.</summary>
    public const string ExternalEmailNotVerified = "external_email_not_verified";

    /// <summary>The OAuth sign-in failed.</summary>
    public const string ExternalSignInFailed = "external_sign_in_failed";

    /// <summary>The request has an API key or an access token, but it is not valid.</summary>
    public const string InvalidCredentialsSent = "invalid_authentication";

    /// <summary>The delegation token does not have the scope of the endpoint, or the endpoint does not accept delegation tokens.</summary>
    public const string ScopeMissing = "scope_missing";

    /// <summary>The account has the maximum number of active delegations.</summary>
    public const string DelegationLimit = "delegation_limit";

    /// <summary>A service account with the same name exists.</summary>
    public const string ServiceAccountNameTaken = "service_account_name_taken";
}
