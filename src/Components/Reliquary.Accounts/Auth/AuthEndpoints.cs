using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.Accounts.Data;
using Reliquary.Accounts.Security;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Accounts.Auth;

/// <summary>The body of a registration.</summary>
public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName, string? Language);

/// <summary>The body of a sign-in with a password.</summary>
public sealed record SignInRequest(string? Email, string? Password);

/// <summary>The body of a request with the token of an email link.</summary>
public sealed record TokenRequest(string? Token);

/// <summary>The body of a request about an email address.</summary>
public sealed record EmailRequest(string? Email);

/// <summary>The body of a password reset.</summary>
public sealed record ResetPasswordRequest(string? Token, string? Password);

/// <summary>An account, as the owner sees it.</summary>
/// <param name="Id">The ID of the account.</param>
/// <param name="Email">The email address.</param>
/// <param name="EmailChecked">Whether the user opened the link of the email check, or an OAuth provider checked the address.</param>
/// <param name="DisplayName">The name that other users see.</param>
/// <param name="Language">The interface language: <c>en</c> or <c>fr</c>.</param>
/// <param name="IsAdministrator">Whether the account is an administrator of the instance.</param>
/// <param name="HasPassword">Whether the account has a password. An account of an OAuth provider can have no password.</param>
/// <param name="CreatedAt">The time of the creation.</param>
public sealed record AccountResponse(Guid Id, string Email, bool EmailChecked, string DisplayName, string Language, bool IsAdministrator, bool HasPassword, DateTimeOffset CreatedAt)
{
    internal static AccountResponse From(AccountRow account) => new(
        account.Id,
        account.Email,
        account.EmailCheckedAt is not null,
        account.DisplayName,
        account.Language,
        account.IsAdministrator,
        account.PasswordHash is not null,
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
/// The endpoints of the registration, the sign-in and the web sessions.
/// </summary>
internal static class AuthEndpoints
{
    /// <summary>The name of the rate limiter policy of the authentication endpoints.</summary>
    public const string RateLimiterPolicy = "accounts-auth";

    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Authentication");
        var limited = auth.MapGroup(string.Empty).RequireRateLimiting(RateLimiterPolicy);

        limited.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .WithSummary("Makes a new account with a password. The response is the same when the address has an account, and the API then sends an email to the address.");

        limited.MapPost("/email-check", CheckEmailAsync)
            .WithName("CheckEmail")
            .WithSummary("Marks the email address as checked, with the token of the email link.");

        limited.MapPost("/email-check/resend", ResendEmailCheckAsync)
            .WithName("ResendEmailCheck")
            .WithSummary("Sends the email of the address check again, when the address is not checked.");

        limited.MapPost("/sign-in", SignInAsync)
            .WithName("SignIn")
            .WithSummary("Starts a web session with an email address and a password. The refresh token goes into an HttpOnly cookie.");

        limited.MapPost("/password/forgot", ForgotPasswordAsync)
            .WithName("ForgotPassword")
            .WithSummary("Sends an email with a link that resets the password, when the address has an account.");

        limited.MapPost("/password/reset", ResetPasswordAsync)
            .WithName("ResetPassword")
            .WithSummary("Sets a new password with the token of the email link, and stops all the sessions of the account.");

        auth.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshSession")
            .WithSummary("Gives a new access token with the refresh cookie, and replaces the cookie.");

        auth.MapPost("/sign-out", SignOutAsync)
            .WithName("SignOut")
            .WithSummary("Stops the web session of the refresh cookie and deletes the cookie.");
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> RegisterAsync(RegisterRequest request, AccountService accounts, CancellationToken cancellationToken)
    {
        var errors = AccountValidation.Email(request.Email)
            .Concat(AccountValidation.Password(request.Password))
            .Concat(AccountValidation.DisplayName(request.DisplayName, required: true))
            .Concat(AccountValidation.Language(request.Language, required: true))
            .ToList();
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        await accounts.RegisterAsync(request.Email!, request.Password!, request.DisplayName!, request.Language!, cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> CheckEmailAsync(TokenRequest request, AccountService accounts, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return ApiProblems.Validation(new FieldError("token", FieldErrorCodes.Required, "The token is necessary."));
        }

        return await accounts.CheckEmailAsync(request.Token, cancellationToken)
            ? TypedResults.NoContent()
            : InvalidToken();
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> ResendEmailCheckAsync(EmailRequest request, AccountService accounts, CancellationToken cancellationToken)
    {
        var errors = AccountValidation.Email(request.Email).ToList();
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        await accounts.ResendEmailCheckAsync(request.Email!, cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> SignInAsync(SignInRequest request, AccountService accounts, HttpResponse response, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return InvalidCredentials();
        }

        var (status, session) = await accounts.SignInAsync(request.Email, request.Password, cancellationToken);
        return status switch
        {
            SignInStatus.Success => Started(session!, response),
            SignInStatus.EmailNotChecked => ApiProblems.Problem(
                StatusCodes.Status403Forbidden,
                AccountErrorCodes.EmailNotChecked,
                "The email address is not checked. Open the link in the email, or ask for a new email."),
            _ => InvalidCredentials(),
        };
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

    private static async Task<Results<Accepted, ProblemHttpResult>> ForgotPasswordAsync(EmailRequest request, AccountService accounts, CancellationToken cancellationToken)
    {
        var errors = AccountValidation.Email(request.Email).ToList();
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        await accounts.ForgotPasswordAsync(request.Email!, cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetPasswordAsync(ResetPasswordRequest request, AccountService accounts, CancellationToken cancellationToken)
    {
        var errors = AccountValidation.Password(request.Password).ToList();
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            errors.Insert(0, new FieldError("token", FieldErrorCodes.Required, "The token is necessary."));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        return await accounts.ResetPasswordAsync(request.Token!, request.Password!, cancellationToken)
            ? TypedResults.NoContent()
            : InvalidToken();
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

    private static ProblemHttpResult InvalidCredentials() =>
        ApiProblems.Problem(
            StatusCodes.Status401Unauthorized,
            AccountErrorCodes.InvalidCredentials,
            "The email address or the password is not correct.");

    private static ProblemHttpResult InvalidToken() =>
        ApiProblems.Problem(
            StatusCodes.Status400BadRequest,
            AccountErrorCodes.InvalidToken,
            "The link is not valid. You used it before, it expired, or it is not complete. Ask for a new email.");
}
