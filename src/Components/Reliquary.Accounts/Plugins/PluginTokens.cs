using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Reliquary.Accounts.Auth;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.Plugins;

/// <summary>The claims of a plugin token that the API read and checked.</summary>
/// <param name="AccountId">The ID of the account of the user.</param>
/// <param name="PluginId">The ID of the plugin.</param>
/// <param name="Scopes">The scopes of the token.</param>
internal sealed record PluginTokenClaims(Guid AccountId, string PluginId, IReadOnlyList<string> Scopes);

/// <summary>
/// Creates and reads the plugin tokens: JSON Web Tokens signed with HMAC SHA-256 and the key of the access tokens, with the start <c>rlp_</c>.
/// See docs/decisions/0049-plugin-sandbox.md.
/// </summary>
/// <remarks>
/// Three properties keep a plugin token separate from an access token of a web session:
/// <list type="bullet">
/// <item>the audience <see cref="Audience"/>, not the audience of the access tokens,</item>
/// <item>the token type <see cref="TokenType"/> in the header of the JWT, not <c>JWT</c>,</item>
/// <item>the claim <c>amr</c> with the value <see cref="AuthMethods.Plugin"/>.</item>
/// </list>
/// The plugin scheme checks the three properties, and the scheme of the access tokens refuses each of them.
/// A delegation token is a random secret with the start <c>rld_</c>, so it is never a valid JWT.
/// </remarks>
internal sealed class PluginTokens(IOptions<AuthOptions> options, TimeProvider time)
{
    /// <summary>The start of each plugin token. The default authentication scheme uses it to choose the plugin scheme.</summary>
    public const string Start = "rlp_";

    /// <summary>The audience of the plugin tokens.</summary>
    public const string Audience = "reliquary-plugin";

    /// <summary>The token type in the header of a plugin token.</summary>
    public const string TokenType = "rlp+jwt";

    /// <summary>The maximum length of a plugin token, so the API does not read a very long header.</summary>
    public const int MaxLength = 4096;

    private readonly JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false, MapInboundClaims = false };

    /// <summary>
    /// Creates a plugin token.
    /// </summary>
    /// <param name="accountId">The ID of the account of the user.</param>
    /// <param name="pluginId">The ID of the plugin.</param>
    /// <param name="scopes">The scopes that the user accepted for the plugin.</param>
    /// <returns>The token, with its start, and the time when it stops being valid.</returns>
    public (string Token, DateTimeOffset ExpiresAt) Create(Guid accountId, string pluginId, IReadOnlyList<string> scopes)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var expiresAt = now + settings.PluginTokenLifetime;
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = Audience,
            TokenType = TokenType,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                [ReliquaryClaims.AccountId] = accountId.ToString(),
                [ReliquaryClaims.AuthMethod] = AuthMethods.Plugin,
                [ReliquaryClaims.PluginId] = pluginId,
                [ReliquaryClaims.Scope] = string.Join(' ', scopes),
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(AccessTokens.ValidationParameters(settings).IssuerSigningKey, SecurityAlgorithms.HmacSha256),
        });
        return (Start + token, expiresAt);
    }

    /// <summary>
    /// Reads a plugin token and checks its signature, its issuer, its audience, its type, its method and its lifetime.
    /// </summary>
    /// <param name="value">The token, with its start <c>rlp_</c>.</param>
    /// <returns>The claims, or <see langword="null"/> when the token is not a valid plugin token.</returns>
    public async Task<PluginTokenClaims?> ReadAsync(string? value)
    {
        if (value is null || value.Length > MaxLength || !value.StartsWith(Start, StringComparison.Ordinal))
        {
            return null;
        }

        var settings = options.Value;
        var parameters = AccessTokens.ValidationParameters(settings);
        parameters.ValidAudience = Audience;
        parameters.ValidTypes = [TokenType];
        parameters.LifetimeValidator = (notBefore, expires, _, _) =>
        {
            var now = time.GetUtcNow().UtcDateTime;
            return expires is { } end && now < end.AddSeconds(30) && (notBefore is not { } start || now >= start.AddSeconds(-30));
        };

        var result = await handler.ValidateTokenAsync(value[Start.Length..], parameters);
        if (!result.IsValid
            || !result.Claims.TryGetValue(ReliquaryClaims.AuthMethod, out var method) || method as string != AuthMethods.Plugin
            || !result.Claims.TryGetValue(ReliquaryClaims.AccountId, out var subject) || !Guid.TryParse(subject as string, out var accountId)
            || !result.Claims.TryGetValue(ReliquaryClaims.PluginId, out var plugin) || plugin is not string { Length: > 0 } pluginId)
        {
            return null;
        }

        var scopes = result.Claims.TryGetValue(ReliquaryClaims.Scope, out var scope) && scope is string text
            ? text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];
        return new PluginTokenClaims(accountId, pluginId, scopes);
    }
}
