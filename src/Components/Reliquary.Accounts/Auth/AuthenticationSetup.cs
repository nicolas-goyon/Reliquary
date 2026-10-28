using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.Accounts.Data;
using Reliquary.Accounts.Security;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.Auth;

/// <summary>
/// The names of the authentication schemes.
/// </summary>
internal static class AuthSchemes
{
    /// <summary>The default scheme. It sends an API key to <see cref="ApiKey"/> and an access token to <see cref="Bearer"/>.</summary>
    public const string Default = "Reliquary";

    /// <summary>The access tokens of the web sessions.</summary>
    public const string Bearer = "Reliquary.Bearer";

    /// <summary>The personal API keys.</summary>
    public const string ApiKey = "Reliquary.ApiKey";

    /// <summary>The short cookie that keeps the result of an OAuth provider until the API creates the web session.</summary>
    public const string External = "Reliquary.External";
}

/// <summary>
/// The OAuth providers that the instance enables.
/// </summary>
/// <param name="Names">The names of the providers, in lowercase, for example <c>github</c>.</param>
internal sealed record EnabledProviders(IReadOnlyList<string> Names);

/// <summary>
/// Adds the authentication and the authorization of the API.
/// </summary>
internal static class AuthenticationSetup
{
    /// <summary>The claim that tells if the OAuth provider checked the email address.</summary>
    public const string EmailVerifiedClaim = "urn:reliquary:email_verified";

    /// <summary>The supported OAuth providers, by configuration name.</summary>
    public static readonly IReadOnlyDictionary<string, string> SupportedProviders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Google"] = "google",
        ["GitHub"] = "github",
        ["Discord"] = "discord",
    };

    public static void AddAccountsAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();

        var authentication = services
            .AddAuthentication(AuthSchemes.Default)
            .AddPolicyScheme(AuthSchemes.Default, "Reliquary", options => options.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString().StartsWith("Bearer " + ApiKeyFormat.Start, StringComparison.Ordinal)
                    ? AuthSchemes.ApiKey
                    : AuthSchemes.Bearer)
            .AddJwtBearer(AuthSchemes.Bearer, options => options.MapInboundClaims = false)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, null)
            .AddCookie(AuthSchemes.External, options =>
            {
                options.Cookie.Name = "reliquary_external";
                options.Cookie.Path = ApiRoutes.Base + "/auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.SlidingExpiration = false;
            });

        // The options of the access tokens read the settings when the API validates a token, so a host with no signing key can start.
        services.AddOptions<JwtBearerOptions>(AuthSchemes.Bearer)
            .Configure<IOptions<AuthOptions>>((options, auth) =>
            {
                var parameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidIssuer = auth.Value.Issuer,
                    ValidAudience = auth.Value.Audience,
                    ValidAlgorithms = [Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ReliquaryClaims.DisplayName,
                    RoleClaimType = ReliquaryClaims.Role,
                    IssuerSigningKeyResolver = (_, _, _, _) => [AccessTokens.ValidationParameters(auth.Value).IssuerSigningKey],
                };
                options.TokenValidationParameters = parameters;
            });

        var enabled = new List<string>();
        foreach (var (configurationName, scheme) in SupportedProviders)
        {
            if (!settings.Providers.TryGetValue(configurationName, out var provider) || !provider.IsConfigured)
            {
                continue;
            }

            enabled.Add(scheme);
            var callbackPath = $"{ApiRoutes.Base}/auth/external/{scheme}/callback";
            switch (scheme)
            {
                case "google":
                    authentication.AddGoogle(scheme, options =>
                    {
                        Configure(options, provider, callbackPath);
                        options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "email_verified");
                    });
                    break;
                case "github":
                    authentication.AddGitHub(scheme, options =>
                    {
                        Configure(options, provider, callbackPath);
                        options.Scope.Add("user:email");
                    });
                    break;
                case "discord":
                    authentication.AddDiscord(scheme, options =>
                    {
                        Configure(options, provider, callbackPath);
                        options.Scope.Add("email");
                        options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "verified");
                    });
                    break;
            }
        }

        services.AddSingleton(new EnabledProviders(enabled));

        services.AddAuthorizationBuilder()
            // A service account has no account ID, so it is not a user.
            .AddPolicy(AuthPolicies.User, policy => policy.RequireAuthenticatedUser().RequireClaim(ReliquaryClaims.AccountId))
            .AddPolicy(AuthPolicies.ServiceAccount, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ReliquaryClaims.AuthMethod, AuthMethods.ServiceAccount)
                .RequireClaim(ReliquaryClaims.ServiceAccountId))
            .AddPolicy(AuthPolicies.Session, policy => policy.RequireAuthenticatedUser().RequireClaim(ReliquaryClaims.AuthMethod, AuthMethods.Session))
            .AddPolicy(AuthPolicies.Administrator, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ReliquaryClaims.AuthMethod, AuthMethods.Session)
                .RequireRole(Roles.Administrator));
    }

    private static void Configure(OAuthOptions options, OAuthProviderOptions provider, string callbackPath)
    {
        options.ClientId = provider.ClientId!;
        options.ClientSecret = provider.ClientSecret!;
        options.CallbackPath = callbackPath;
        options.SignInScheme = AuthSchemes.External;
        options.SaveTokens = false;

        // The provider sends the user back with a top-level GET request, so SameSite=Lax is sufficient, and it works on a local instance with HTTP.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }
}

/// <summary>
/// Authenticates the requests with a personal API key in the header <c>Authorization: Bearer rlq_...</c>.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AccountStore store,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    // The API writes the time of the last use at most one time in this interval, so each request does not write to the database.
    private static readonly TimeSpan LastUseResolution = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        if (!ApiKeyFormat.TryParse(header["Bearer ".Length..].Trim(), out var prefix, out var secret))
        {
            return AuthenticateResult.Fail("The API key does not have the correct format.");
        }

        var key = await store.FindActiveApiKeyAsync(prefix, Context.RequestAborted);
        if (key is null || !CryptographicOperations.FixedTimeEquals(key.SecretHash, Secrets.Hash(secret)))
        {
            return AuthenticateResult.Fail("The API key is not valid or it was revoked.");
        }

        var now = time.GetUtcNow();
        if (key.LastUsedAt is not { } lastUsedAt || now.UtcDateTime - lastUsedAt > LastUseResolution)
        {
            await store.MarkApiKeyUsedAsync(key.Id, now, Context.RequestAborted);
        }

        return AuthenticateResult.Success(new AuthenticationTicket(PrincipalClaims.ForApiKey(key, Scheme.Name), Scheme.Name));
    }
}

/// <summary>
/// Reads the claims of an OAuth provider.
/// </summary>
internal static class ExternalClaims
{
    public static ExternalIdentity Read(string provider, ClaimsPrincipal principal, string language)
    {
        var key = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException($"The provider {provider} gave no user ID.");
        var email = principal.FindFirstValue(ClaimTypes.Email);

        // GitHub gives only a primary address that it checked. Google and Discord give a separate value.
        var verified = provider == "github"
            ? !string.IsNullOrEmpty(email)
            : string.Equals(principal.FindFirstValue(AuthenticationSetup.EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase);
        var name = principal.FindFirstValue(ClaimTypes.Name);
        return new ExternalIdentity(provider, key, email, verified, name, language);
    }
}
