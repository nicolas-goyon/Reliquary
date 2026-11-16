using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.Plugins;

/// <summary>
/// Authenticates a plugin token in the header <c>Authorization: Bearer rlp_...</c>.
/// The principal is the user, with the method <see cref="AuthMethods.Plugin"/>, the plugin ID and the scopes of the token.
/// </summary>
/// <remarks>
/// The handler asks the platform for each request whether the plugin runs for the user.
/// Thus a deactivation of the plugin by the user or by the administrator stops the token immediately.
/// The scopes of the principal are the scopes of the token that the plugin still has.
/// </remarks>
internal sealed class PluginTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    PluginTokens tokens,
    IPluginAccess access)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The start of the header value of a plugin token.</summary>
    public const string HeaderStart = "Bearer " + PluginTokens.Start;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(HeaderStart, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        if (await tokens.ReadAsync(header["Bearer ".Length..].Trim()) is not { } token)
        {
            return AuthenticateResult.Fail("The plugin token is not valid or it expired.");
        }

        if (await access.ScopesAsync(token.AccountId, token.PluginId, Context.RequestAborted) is not { } current)
        {
            return AuthenticateResult.Fail("The plugin does not run for the user. The user or the administrator deactivated it.");
        }

        var claims = new List<Claim>
        {
            new(ReliquaryClaims.AccountId, token.AccountId.ToString()),
            new(ReliquaryClaims.AuthMethod, AuthMethods.Plugin),
            new(ReliquaryClaims.PluginId, token.PluginId),
        };
        claims.AddRange(token.Scopes.Where(scope => current.Contains(scope, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)
            .Select(scope => new Claim(ReliquaryClaims.Scope, scope)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, ReliquaryClaims.DisplayName, ReliquaryClaims.Role));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
