using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.Accounts.Auth;
using Reliquary.Accounts.Data;
using Reliquary.Accounts.Security;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.Profile;

/// <summary>The account of the user, with its linked OAuth providers.</summary>
/// <param name="Account">The account.</param>
/// <param name="ExternalLogins">The names of the linked OAuth providers.</param>
public sealed record MeResponse(AccountResponse Account, IReadOnlyList<string> ExternalLogins);

/// <summary>The body of a change of the account. A missing field does not change.</summary>
public sealed record UpdateMeRequest(string? DisplayName, string? Language);

/// <summary>The body of a change of the password.</summary>
/// <param name="CurrentPassword">The current password. An account with no password does not need it.</param>
/// <param name="NewPassword">The new password.</param>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>The body of a new API key.</summary>
public sealed record CreateApiKeyRequest(string? Name);

/// <summary>An API key. The response never contains the secret.</summary>
/// <param name="Id">The ID of the key.</param>
/// <param name="Name">The name that the user gave to the key.</param>
/// <param name="Prefix">The visible start of the key, after <c>rlq_</c>.</param>
/// <param name="CreatedAt">The time of the creation.</param>
/// <param name="LastUsedAt">The approximate time of the last request with the key.</param>
public sealed record ApiKeyResponse(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>A new API key, with the full key. The API shows the full key only one time.</summary>
/// <param name="Id">The ID of the key.</param>
/// <param name="Name">The name of the key.</param>
/// <param name="Prefix">The visible start of the key.</param>
/// <param name="Key">The full key. Keep it secret. Send it in the header <c>Authorization: Bearer</c>.</param>
/// <param name="CreatedAt">The time of the creation.</param>
public sealed record CreatedApiKeyResponse(Guid Id, string Name, string Prefix, string Key, DateTimeOffset CreatedAt);

/// <summary>
/// The endpoints of the account of the user: profile, password and API keys.
/// </summary>
internal static class ProfileEndpoints
{
    /// <summary>The maximum number of active API keys of an account.</summary>
    public const int MaxApiKeys = 20;

    /// <summary>The maximum length of the name of an API key.</summary>
    public const int MaxApiKeyNameLength = 50;

    public static void Map(IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Account").RequireAuthorization(AuthPolicies.User);
        var session = me.MapGroup(string.Empty).RequireAuthorization(AuthPolicies.Session);

        me.MapGet(string.Empty, GetAsync)
            .WithName("GetMe")
            .WithSummary("Gets the account of the user.")
            .RequireScope(Scopes.ProfileRead);

        session.MapPatch(string.Empty, UpdateAsync)
            .WithName("UpdateMe")
            .WithSummary("Changes the display name or the interface language of the account.");

        session.MapPost("/password", ChangePasswordAsync)
            .WithName("ChangePassword")
            .WithSummary("Changes the password, stops all the sessions of the account and starts a new session.")
            .RequireRateLimiting(AuthEndpoints.RateLimiterPolicy);

        session.MapGet("/api-keys", ListApiKeysAsync)
            .WithName("ListApiKeys")
            .WithSummary("Lists the active API keys of the account.");

        session.MapPost("/api-keys", CreateApiKeyAsync)
            .WithName("CreateApiKey")
            .WithSummary("Creates an API key. The response contains the full key one time only.");

        session.MapDelete("/api-keys/{id:guid}", RevokeApiKeyAsync)
            .WithName("RevokeApiKey")
            .WithSummary("Revokes an API key. The key stops working immediately.");
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> GetAsync(ClaimsPrincipal user, AccountStore store, CancellationToken cancellationToken)
    {
        var account = await store.FindByIdAsync(user.GetAccountId(), cancellationToken);
        if (account is null)
        {
            return AccountMissing();
        }

        var providers = await store.ListExternalProvidersAsync(account.Id, cancellationToken);
        return TypedResults.Ok(new MeResponse(AccountResponse.From(account), providers));
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> UpdateAsync(UpdateMeRequest request, ClaimsPrincipal user, AccountStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        var errors = AccountValidation.DisplayName(request.DisplayName, required: false)
            .Concat(AccountValidation.Language(request.Language, required: false))
            .ToList();
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var account = await store.UpdateProfileAsync(user.GetAccountId(), request.DisplayName?.Trim(), request.Language, time.GetUtcNow(), cancellationToken);
        return account is null ? AccountMissing() : TypedResults.Ok(AccountResponse.From(account));
    }

    private static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> ChangePasswordAsync(ChangePasswordRequest request, ClaimsPrincipal user, AccountService accounts, HttpResponse response, CancellationToken cancellationToken)
    {
        var errors = AccountValidation.Password(request.NewPassword, "newPassword").ToList();
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var session = await accounts.ChangePasswordAsync(user.GetAccountId(), request.CurrentPassword, request.NewPassword!, cancellationToken);
        if (session is null)
        {
            return ApiProblems.Problem(
                StatusCodes.Status400BadRequest,
                AccountErrorCodes.InvalidCredentials,
                "The current password is not correct.");
        }

        return AuthEndpoints.Started(session, response);
    }

    private static async Task<Ok<IReadOnlyList<ApiKeyResponse>>> ListApiKeysAsync(ClaimsPrincipal user, AccountStore store, CancellationToken cancellationToken)
    {
        var keys = await store.ListApiKeysAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<ApiKeyResponse>>(keys.Select(key => new ApiKeyResponse(
            key.Id,
            key.Name,
            key.Prefix,
            new DateTimeOffset(key.CreatedAt, TimeSpan.Zero),
            key.LastUsedAt is { } lastUsedAt ? new DateTimeOffset(lastUsedAt, TimeSpan.Zero) : null)).ToList());
    }

    private static async Task<Results<Created<CreatedApiKeyResponse>, ProblemHttpResult>> CreateApiKeyAsync(CreateApiKeyRequest request, ClaimsPrincipal user, AccountStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return ApiProblems.Validation(new FieldError("name", FieldErrorCodes.Required, "The name of the key is necessary."));
        }

        if (name.Length > MaxApiKeyNameLength)
        {
            return ApiProblems.Validation(new FieldError("name", FieldErrorCodes.TooLong, $"The name of the key must have {MaxApiKeyNameLength} characters or fewer."));
        }

        var accountId = user.GetAccountId();
        if (await store.CountApiKeysAsync(accountId, cancellationToken) >= MaxApiKeys)
        {
            return ApiProblems.Conflict(AccountErrorCodes.ApiKeyLimit, $"The account has {MaxApiKeys} API keys. Revoke a key, then try again.");
        }

        var (key, prefix, secretHash) = ApiKeyFormat.Create();
        var now = time.GetUtcNow();
        var row = new ApiKeyRow { Id = Ids.New(), AccountId = accountId, Name = name, Prefix = prefix, SecretHash = secretHash, CreatedAt = now.UtcDateTime };
        await store.InsertApiKeyAsync(row, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/me/api-keys/{row.Id}", new CreatedApiKeyResponse(row.Id, name, prefix, key, now));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeApiKeyAsync(Guid id, ClaimsPrincipal user, AccountStore store, TimeProvider time, CancellationToken cancellationToken) =>
        await store.RevokeApiKeyAsync(user.GetAccountId(), id, time.GetUtcNow(), cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblems.NotFound("The API key does not exist, or it is revoked.");

    private static ProblemHttpResult AccountMissing() => ApiProblems.NotFound("The account does not exist.");
}
