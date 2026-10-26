namespace Reliquary.Accounts;

/// <summary>
/// The settings of the authentication. The section is <c>Auth</c>.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Auth";

    /// <summary>Gets or sets the issuer of the access tokens.</summary>
    public string Issuer { get; set; } = "reliquary";

    /// <summary>Gets or sets the audience of the access tokens.</summary>
    public string Audience { get; set; } = "reliquary";

    /// <summary>
    /// Gets or sets the key that signs the access tokens, in base64, with 32 bytes or more.
    /// All the API replicas must use the same key. Keep it secret.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the lifetime of an access token.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets the lifetime of a refresh token, so the maximum time without a visit.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Gets or sets whether a user must check the email address before the first sign-in with a password.</summary>
    public bool RequireCheckedEmail { get; set; } = true;

    /// <summary>Gets or sets the lifetime of the link that checks an email address.</summary>
    public TimeSpan EmailCheckLifetime { get; set; } = TimeSpan.FromHours(48);

    /// <summary>Gets or sets the lifetime of the link that resets a password.</summary>
    public TimeSpan PasswordResetLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets the address of the web interface, for the links in the emails, for example <c>https://reliquary.example.org</c>.</summary>
    public Uri PublicUrl { get; set; } = new("http://localhost:5173");

    /// <summary>Gets or sets the email addresses of the administrators of the instance.</summary>
    public List<string> AdministratorEmails { get; set; } = [];

    /// <summary>Gets or sets the maximum number of requests to the sign-in, sign-up and email endpoints, for each client address and minute.</summary>
    public int RequestsPerMinute { get; set; } = 10;

    /// <summary>
    /// Gets or sets the OAuth providers, by name: <c>Google</c>, <c>GitHub</c> or <c>Discord</c>.
    /// A provider is enabled when it has a client ID and a client secret.
    /// </summary>
    public Dictionary<string, OAuthProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The settings of one OAuth provider.
/// </summary>
public sealed class OAuthProviderOptions
{
    /// <summary>Gets or sets the client ID that the provider gave to the instance.</summary>
    public string? ClientId { get; set; }

    /// <summary>Gets or sets the client secret that the provider gave to the instance. Keep it secret.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Gets whether the provider has a client ID and a client secret.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// The settings of the daily quotas of the API keys. The section is <c>Quotas</c>.
/// </summary>
public sealed class QuotaOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Quotas";

    /// <summary>Gets or sets the maximum number of requests of one API key in one day (UTC).</summary>
    public long RequestsPerKeyPerDay { get; set; } = 10_000;

    /// <summary>Gets or sets the maximum number of requests of all the API keys of one account in one day (UTC).</summary>
    public long RequestsPerAccountPerDay { get; set; } = 10_000;
}
