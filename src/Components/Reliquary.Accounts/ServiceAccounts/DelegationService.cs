using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.Accounts.Security;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.ServiceAccounts;

/// <summary>A service account as a user sees it, when they give a delegation.</summary>
/// <param name="Id">The ID of the service account.</param>
/// <param name="Name">The unique name.</param>
/// <param name="Description">The description.</param>
public sealed record ServiceAccountSummary(Guid Id, string Name, string Description);

/// <summary>A delegation of the user to a service account.</summary>
/// <param name="Id">The ID of the delegation.</param>
/// <param name="ServiceAccount">The service account that can act for the user.</param>
/// <param name="Scopes">The scopes that the user gave.</param>
/// <param name="CreatedAt">The time of the creation.</param>
/// <param name="ExpiresAt">The time when the delegation stops.</param>
/// <param name="LastUsedAt">The approximate time of the last request with a token of the delegation.</param>
public sealed record DelegationResponse(Guid Id, ServiceAccountSummary ServiceAccount, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt);

/// <summary>The body of a new delegation.</summary>
/// <param name="ServiceAccountId">The service account that can act for the user.</param>
/// <param name="Scopes">The scopes. See docs/architecture/service-accounts.md.</param>
/// <param name="ExpiresInDays">The number of days until the delegation stops, from 1 to 365. The default is 90.</param>
public sealed record CreateDelegationRequest(Guid? ServiceAccountId, IReadOnlyList<string>? Scopes, int? ExpiresInDays);

/// <summary>A delegation that a user gave to the service account, as the internal API gives it.</summary>
/// <param name="Id">The ID of the delegation.</param>
/// <param name="AccountId">The user. The owner of the user data has the same ID.</param>
/// <param name="Scopes">The scopes that the user gave.</param>
/// <param name="CreatedAt">The time of the creation.</param>
/// <param name="ExpiresAt">The time when the delegation stops.</param>
public sealed record InternalDelegationResponse(Guid Id, Guid AccountId, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>The body of a request for a delegation token.</summary>
/// <param name="Scopes">The scopes of the token. They must be scopes of the delegation. With no value, the token gets all the scopes of the delegation.</param>
public sealed record CreateDelegationTokenRequest(IReadOnlyList<string>? Scopes);

/// <summary>A new delegation token. The API shows the token only one time.</summary>
/// <param name="Token">The token. Send it to the public API in the header <c>Authorization: Bearer</c>.</param>
/// <param name="TokenType">The type of the token: always <c>Bearer</c>.</param>
/// <param name="AccountId">The user that the token acts for.</param>
/// <param name="Scopes">The scopes of the token.</param>
/// <param name="ExpiresAt">The time when the token stops working.</param>
public sealed record DelegationTokenResponse(string Token, string TokenType, Guid AccountId, IReadOnlyList<string> Scopes, DateTimeOffset ExpiresAt);

/// <summary>The result of an operation on a delegation.</summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="Value">The value, or <see langword="null"/> after an error.</param>
/// <param name="Errors">The errors of the fields.</param>
/// <param name="NotFound">Whether the delegation or the service account does not exist or is not active.</param>
/// <param name="LimitReached">Whether the account has the maximum number of active delegations.</param>
internal sealed record DelegationResult<T>(T? Value, IReadOnlyList<FieldError> Errors, bool NotFound = false, bool LimitReached = false)
    where T : class;

/// <summary>
/// Creates and revokes the delegations of the users, and gives the delegation tokens to the service accounts.
/// See docs/decisions/0037-service-accounts-and-delegation-tokens.md.
/// </summary>
internal sealed class DelegationService(ServiceAccountStore store, IOptions<AuthOptions> options, TimeProvider time, ILogger<DelegationService> logger)
{
    /// <summary>The maximum number of active delegations of an account.</summary>
    public const int MaxDelegations = 20;

    /// <summary>The default lifetime of a delegation, in days.</summary>
    public const int DefaultLifetimeDays = 90;

    /// <summary>The maximum lifetime of a delegation, in days.</summary>
    public const int MaxLifetimeDays = 365;

    public async Task<IReadOnlyList<ServiceAccountSummary>> ListServiceAccountsAsync(CancellationToken cancellationToken) =>
        (await store.ListServiceAccountsAsync(includeRevoked: false, cancellationToken))
            .Select(row => new ServiceAccountSummary(row.Id, row.Name, row.Description))
            .ToList();

    public async Task<IReadOnlyList<DelegationResponse>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        (await store.ListActiveDelegationsOfAccountAsync(accountId, time.GetUtcNow(), cancellationToken)).Select(ToResponse).ToList();

    /// <summary>
    /// Gives a delegation to a service account: the consent of the user.
    /// </summary>
    public async Task<DelegationResult<DelegationResponse>> CreateAsync(Guid accountId, CreateDelegationRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.ServiceAccountId is null)
        {
            errors.Add(new FieldError("serviceAccountId", FieldErrorCodes.Required, "The service account is necessary."));
        }

        var scopes = ReadScopes(request.Scopes, "scopes", required: true, errors);
        var days = request.ExpiresInDays ?? DefaultLifetimeDays;
        if (days is < 1 or > MaxLifetimeDays)
        {
            errors.Add(new FieldError("expiresInDays", FieldErrorCodes.OutOfRange, $"The number of days must be from 1 to {MaxLifetimeDays}."));
        }

        if (errors.Count > 0)
        {
            return new DelegationResult<DelegationResponse>(null, errors);
        }

        var serviceAccount = await store.FindServiceAccountAsync(request.ServiceAccountId!.Value, cancellationToken);
        if (serviceAccount is null || serviceAccount.RevokedAt is not null)
        {
            return new DelegationResult<DelegationResponse>(null, [], NotFound: true);
        }

        var now = time.GetUtcNow();
        if (await store.CountActiveDelegationsAsync(accountId, now, cancellationToken) >= MaxDelegations)
        {
            return new DelegationResult<DelegationResponse>(null, [], LimitReached: true);
        }

        var row = new DelegationRow
        {
            Id = Ids.New(),
            AccountId = accountId,
            ServiceAccountId = serviceAccount.Id,
            ServiceAccountName = serviceAccount.Name,
            ServiceAccountDescription = serviceAccount.Description,
            Scopes = scopes,
            CreatedAt = now.UtcDateTime,
            ExpiresAt = (now + TimeSpan.FromDays(days)).UtcDateTime,
        };
        await store.InsertDelegationAsync(row, cancellationToken);
        logger.DelegationCreated(accountId, row.Id, serviceAccount.Id, scopes);
        return new DelegationResult<DelegationResponse>(ToResponse(row), []);
    }

    public async Task<bool> RevokeAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var revoked = await store.RevokeDelegationAsync(accountId, id, time.GetUtcNow(), cancellationToken);
        if (revoked)
        {
            logger.DelegationRevoked(accountId, id);
        }

        return revoked;
    }

    public async Task<IReadOnlyList<InternalDelegationResponse>> ListForServiceAccountAsync(Guid serviceAccountId, CancellationToken cancellationToken) =>
        (await store.ListActiveDelegationsOfServiceAccountAsync(serviceAccountId, time.GetUtcNow(), cancellationToken))
            .Select(row => new InternalDelegationResponse(row.Id, row.AccountId, row.Scopes, ServiceAccountService.Utc(row.CreatedAt), ServiceAccountService.Utc(row.ExpiresAt)))
            .ToList();

    /// <summary>
    /// Gives a delegation token to a service account. The token has the scopes of the request, or all the scopes of the delegation.
    /// It stops working at the end of its lifetime, at the end of the delegation, or when the user revokes the delegation.
    /// </summary>
    public async Task<DelegationResult<DelegationTokenResponse>> IssueTokenAsync(Guid serviceAccountId, Guid delegationId, CreateDelegationTokenRequest request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var delegation = await store.FindActiveDelegationAsync(serviceAccountId, delegationId, now, cancellationToken);
        if (delegation is null)
        {
            return new DelegationResult<DelegationTokenResponse>(null, [], NotFound: true);
        }

        var errors = new List<FieldError>();
        var scopes = request.Scopes is null ? delegation.Scopes : ReadScopes(request.Scopes, "scopes", required: true, errors);
        var missing = scopes.Where(scope => !delegation.Scopes.Contains(scope, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new FieldError("scopes", FieldErrorCodes.NotAllowed, $"The delegation does not give the scopes {string.Join(", ", missing)}. Ask only for the scopes of the delegation."));
        }

        if (errors.Count > 0)
        {
            return new DelegationResult<DelegationTokenResponse>(null, errors);
        }

        var delegationEnd = new DateTimeOffset(DateTime.SpecifyKind(delegation.ExpiresAt, DateTimeKind.Utc));
        var expiresAt = now + options.Value.DelegationTokenLifetime;
        if (expiresAt > delegationEnd)
        {
            expiresAt = delegationEnd;
        }

        var (token, prefix, secretHash) = PrefixedSecretFormat.Delegation.Create();
        await store.InsertDelegationTokenAsync(Ids.New(), delegation.Id, prefix, secretHash, scopes, now, expiresAt, cancellationToken);
        return new DelegationResult<DelegationTokenResponse>(new DelegationTokenResponse(token, "Bearer", delegation.AccountId, scopes, expiresAt), []);
    }

    private static DelegationResponse ToResponse(DelegationRow row) => new(
        row.Id,
        new ServiceAccountSummary(row.ServiceAccountId, row.ServiceAccountName, row.ServiceAccountDescription),
        row.Scopes,
        ServiceAccountService.Utc(row.CreatedAt),
        ServiceAccountService.Utc(row.ExpiresAt),
        row.LastUsedAt is { } lastUsedAt ? ServiceAccountService.Utc(lastUsedAt) : null);

    /// <summary>
    /// Checks a list of scopes, removes the copies and sorts it in the order of <see cref="Scopes.All"/>.
    /// </summary>
    private static string[] ReadScopes(IReadOnlyList<string>? values, string field, bool required, List<FieldError> errors)
    {
        if (values is null || values.Count == 0)
        {
            if (required)
            {
                errors.Add(new FieldError(field, FieldErrorCodes.Required, "Give one scope or more."));
            }

            return [];
        }

        var unknown = values.Where(value => !Scopes.All.Contains(value, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            errors.Add(new FieldError(field, FieldErrorCodes.NotAllowed, $"The scopes {string.Join(", ", unknown)} do not exist. Use {string.Join(", ", Scopes.All)}."));
        }

        return Scopes.All.Where(scope => values.Contains(scope, StringComparer.Ordinal)).ToArray();
    }
}
