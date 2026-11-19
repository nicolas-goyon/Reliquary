using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Reliquary.Accounts.Data;
using Reliquary.Accounts.Security;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Accounts.Auth;

/// <summary>The body of the development sign-in.</summary>
/// <param name="Email">The email address of the account. The API makes the account when it does not exist.</param>
/// <param name="DisplayName">The public name of a new account. The default is the part of the address before the character @.</param>
/// <param name="Language">The language of a new account: <c>en</c> or <c>fr</c>. The default is <c>en</c>.</param>
public sealed record DevelopmentSignInRequest(string? Email, string? DisplayName, string? Language);

/// <summary>An account, for its owner.</summary>
/// <param name="Id">The ID of the account.</param>
/// <param name="Email">The email address that the OAuth provider gave.</param>
/// <param name="DisplayName">The public name.</param>
/// <param name="Language">The language of the interface: <c>en</c> or <c>fr</c>.</param>
/// <param name="IsAdministrator">Whether the account is an administrator of the instance.</param>
/// <param name="CreatedAt">The time when the account was created.</param>
public sealed record AccountResponse(Guid Id, string Email, string DisplayName, string Language, bool IsAdministrator, DateTimeOffset CreatedAt)
{
    internal static AccountResponse From(AccountRow account) => new(
        account.Id,
        account.Email,
        account.DisplayName,
        account.Language,
        account.IsAdministrator,
        new DateTimeOffset(account.CreatedAt, TimeSpan.Zero));
}

/// <summary>A new access token for a web session. The refresh token is in an HttpOnly cookie.</summary>
/// <param name="AccessToken">The access token. Send it in the header <c>Authorization: Bearer</c>.</param>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="ExpiresIn">The number of seconds before the access token stops being valid.</param>
/// <param name="ExpiresAt">The time when the access token stops being valid.</param>
/// <param name="Account">The account.</param>
public sealed record SessionResponse(string AccessToken, string TokenType, int ExpiresIn, DateTimeOffset ExpiresAt, AccountResponse Account);

/// <summary>
/// The endpoints of the web session: refresh, sign-out and the development sign-in.
/// A user starts a session with an OAuth provider: see <see cref="ExternalEndpoints"/> and docs/decisions/0054-sign-in-with-oauth-providers-only.md.
/// </summary>
internal static class AuthEndpoints
{
    /// <summary>The name of the rate limiter policy of the sign-in endpoints.</summary>
    public const string RateLimiterPolicy = "accounts-auth";

    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/development/sign-in", DevelopmentSignInAsync)
            .WithName("DevelopmentSignIn")
            .WithSummary("Starts a web session for an email address with no OAuth provider, and makes the account when it does not exist. Only an instance with Auth:AllowDevelopmentSignIn has this endpoint: a development instance or a test instance. Other instances return 404.");

        auth.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshSession")
            .WithSummary("Gives a new access token with the refresh cookie, and replaces the cookie.");

        auth.MapPost("/sign-out", SignOutAsync)
            .WithName("SignOut")
            .WithSummary("Stops the web session of the refresh cookie and deletes the cookie.");
    }

    private static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> DevelopmentSignInAsync(
        DevelopmentSignInRequest request, AccountService accounts, IOptions<AuthOptions> options, HttpResponse response, CancellationToken cancellationToken)
    {
        if (!options.Value.AllowDevelopmentSignIn)
        {
            return ApiProblems.NotFound("This instance does not have the development sign-in. Sign in with an OAuth provider: GET /auth/providers lists them.");
        }

        var errors = AccountValidation.Email(request.Email)
            .Concat(AccountValidation.DisplayName(request.DisplayName, required: false))
            .Concat(AccountValidation.Language(request.Language, required: false))
            .ToList();
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var session = await accounts.DevelopmentSignInAsync(request.Email!, request.DisplayName, request.Language, cancellationToken);
        return session is null
            ? ApiProblems.Problem(StatusCodes.Status409Conflict, ErrorCodes.Conflict, "The API did not make the account. Try again.")
            : Started(session, response);
    }

    private static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> RefreshAsync(HttpRequest request, HttpResponse response, AccountService accounts, CancellationToken cancellationToken)
    {
        var token = RefreshCookie.Read(request);
        var session = token is null ? null : await accounts.RefreshAsync(token, cancellationToken);
        if (session is null)
        {
            RefreshCookie.Delete(response);
            return ApiProblems.Problem(
                StatusCodes.Status401Unauthorized,
                AccountErrorCodes.SessionExpired,
                "The session is not valid or it stopped. Sign in again.");
        }

        return Started(session, response);
    }

    private static async Task<NoContent> SignOutAsync(HttpRequest request, HttpResponse response, AccountService accounts, CancellationToken cancellationToken)
    {
        if (RefreshCookie.Read(request) is { } token)
        {
            await accounts.SignOutAsync(token, cancellationToken);
        }

        RefreshCookie.Delete(response);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Writes the refresh cookie and makes the response of a new session.
    /// </summary>
    internal static Ok<SessionResponse> Started(Session session, HttpResponse response)
    {
        RefreshCookie.Write(response, session);
        response.Headers.CacheControl = "no-store";
        var expiresIn = (int)Math.Max(0, (session.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return TypedResults.Ok(new SessionResponse(session.AccessToken, "Bearer", expiresIn, session.AccessTokenExpiresAt, AccountResponse.From(session.Account)));
    }
}
