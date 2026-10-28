using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.Accounts.Security;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.ServiceAccounts;

/// <summary>
/// Authenticates a service account with its secret in the header <c>Authorization: Bearer rls_...</c>.
/// The principal has no account ID, so it is not a user.
/// </summary>
internal sealed class ServiceAccountAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ServiceAccountStore store,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!PrefixedSecretFormat.ServiceAccount.Matches(Request.Headers.Authorization.ToString()))
        {
            return AuthenticateResult.NoResult();
        }

        var value = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        if (!PrefixedSecretFormat.ServiceAccount.TryParse(value, out var prefix, out var secret))
        {
            return AuthenticateResult.Fail("The secret of the service account does not have the correct format.");
        }

        var account = await store.FindActiveServiceAccountByPrefixAsync(prefix, Context.RequestAborted);
        if (account is null || !CryptographicOperations.FixedTimeEquals(account.SecretHash, Secrets.Hash(secret)))
        {
            return AuthenticateResult.Fail("The secret of the service account is not valid, or the administrator revoked the service account.");
        }

        var now = time.GetUtcNow();
        if (LastUse.IsOld(account.LastUsedAt, now))
        {
            await store.MarkServiceAccountUsedAsync(account.Id, now, Context.RequestAborted);
        }

        Claim[] claims =
        [
            new(ReliquaryClaims.ServiceAccountId, account.Id.ToString()),
            new(ReliquaryClaims.DisplayName, account.Name),
            new(ReliquaryClaims.AuthMethod, AuthMethods.ServiceAccount),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, ReliquaryClaims.DisplayName, ReliquaryClaims.Role));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

/// <summary>
/// Authenticates a delegation token in the header <c>Authorization: Bearer rld_...</c>.
/// The principal is the user of the delegation, with the scopes of the token.
/// </summary>
/// <remarks>
/// The handler reads the database for each request, so a revoked or expired delegation stops working immediately.
/// </remarks>
internal sealed class DelegationAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ServiceAccountStore store,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!PrefixedSecretFormat.Delegation.Matches(Request.Headers.Authorization.ToString()))
        {
            return AuthenticateResult.NoResult();
        }

        var value = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        if (!PrefixedSecretFormat.Delegation.TryParse(value, out var prefix, out var secret))
        {
            return AuthenticateResult.Fail("The delegation token does not have the correct format.");
        }

        var now = time.GetUtcNow();
        var token = await store.FindValidDelegationTokenAsync(prefix, now, Context.RequestAborted);
        if (token is null || !CryptographicOperations.FixedTimeEquals(token.SecretHash, Secrets.Hash(secret)))
        {
            return AuthenticateResult.Fail("The delegation token is not valid, it expired, or the user revoked the delegation.");
        }

        if (LastUse.IsOld(token.DelegationLastUsedAt, now))
        {
            await store.MarkDelegationUsedAsync(token.DelegationId, now, Context.RequestAborted);
        }

        var claims = new List<Claim>
        {
            new(ReliquaryClaims.AccountId, token.AccountId.ToString()),
            new(ReliquaryClaims.DisplayName, token.DisplayName),
            new(ReliquaryClaims.Language, token.Language),
            new(ReliquaryClaims.AuthMethod, AuthMethods.Delegation),
            new(ReliquaryClaims.ServiceAccountId, token.ServiceAccountId.ToString()),
            new(ReliquaryClaims.DelegationId, token.DelegationId.ToString()),
        };
        claims.AddRange(token.Scopes.Select(scope => new Claim(ReliquaryClaims.Scope, scope)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, ReliquaryClaims.DisplayName, ReliquaryClaims.Role));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

/// <summary>
/// Decides when the API writes the time of the last use. The API writes it at most one time in a minute, so each request does not write to the database.
/// </summary>
internal static class LastUse
{
    private static readonly TimeSpan Resolution = TimeSpan.FromMinutes(1);

    public static bool IsOld(DateTime? lastUsedAt, DateTimeOffset now) => lastUsedAt is not { } value || now.UtcDateTime - value > Resolution;
}

/// <summary>
/// Checks the scope of each request with a delegation token, on all the endpoints of the public API.
/// An endpoint accepts a delegation token only when it declares a scope with <c>RequireScope</c> and the token has this scope.
/// </summary>
internal sealed class ScopeFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!http.User.IsDelegated())
        {
            return await next(context);
        }

        var required = http.GetEndpoint()?.Metadata.GetMetadata<RequiredScopeMetadata>();
        if (required is null)
        {
            return ApiProblems.Problem(
                StatusCodes.Status403Forbidden,
                AccountErrorCodes.ScopeMissing,
                "This endpoint does not accept a delegation token. See the list of the endpoints for each scope in docs/architecture/service-accounts.md.");
        }

        if (!http.User.HasScope(required.Scope))
        {
            return ApiProblems.Problem(
                StatusCodes.Status403Forbidden,
                AccountErrorCodes.ScopeMissing,
                $"The delegation token does not have the scope {required.Scope}. Ask for a token with this scope.",
                new Dictionary<string, object?> { ["scope"] = required.Scope });
        }

        return await next(context);
    }
}
