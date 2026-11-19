using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.Accounts.Data;
using Reliquary.Accounts.Security;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;

namespace Reliquary.Accounts.Auth;

/// <summary>
/// A new web session, or a new access token for a session.
/// </summary>
/// <param name="Account">The account.</param>
/// <param name="AccessToken">The access token.</param>
/// <param name="AccessTokenExpiresAt">The time when the access token stops being valid.</param>
/// <param name="RefreshToken">The new refresh token, or <see langword="null"/> when the cookie does not change.</param>
/// <param name="RefreshTokenExpiresAt">The time when the refresh token stops being valid.</param>
internal sealed record Session(AccountRow Account, string AccessToken, DateTimeOffset AccessTokenExpiresAt, string? RefreshToken, DateTimeOffset? RefreshTokenExpiresAt);

/// <summary>
/// The result of a sign-in.
/// </summary>
/// <summary>
/// The data of an account from an OAuth provider.
/// </summary>
/// <param name="Provider">The name of the provider, for example <c>github</c>.</param>
/// <param name="ProviderKey">The ID of the user at the provider.</param>
/// <param name="Email">The email address, or <see langword="null"/>.</param>
/// <param name="EmailVerified">Whether the provider checked the email address.</param>
/// <param name="Name">The name of the user at the provider, or <see langword="null"/>.</param>
/// <param name="Language">The interface language for a new account.</param>
internal sealed record ExternalIdentity(string Provider, string ProviderKey, string? Email, bool EmailVerified, string? Name, string Language);

/// <summary>
/// The operations on the accounts and the web sessions.
/// </summary>
internal sealed class AccountService(
    AccountStore store,
    AccessTokens accessTokens,
    IShardDirectory shards,
    IDomainEventWriter events,
    IOptions<AuthOptions> options,
    TimeProvider time,
    ILogger<AccountService> logger)
{
    /// <summary>The time after a refresh when the previous refresh token can still give an access token, for requests that run at the same time.</summary>
    public static readonly TimeSpan RefreshGracePeriod = TimeSpan.FromSeconds(30);

    private AuthOptions Settings => options.Value;

    /// <summary>
    /// Creates an account with no OAuth provider: a demo account, or an account of the development sign-in.
    /// </summary>
    /// <returns>The new account, or null when an account has this email address.</returns>
    public async Task<AccountRow?> CreateAccountAsync(string email, string displayName, string language, string origin, CancellationToken cancellationToken)
    {
        var normalized = AccountValidation.NormalizeEmail(email);
        if (await store.FindByEmailAsync(normalized, cancellationToken) is not null)
        {
            return null;
        }

        var account = NewAccount(email.Trim(), normalized, displayName.Trim(), language, time.GetUtcNow());
        if (!await CreateAsync(account, null, cancellationToken))
        {
            return null;
        }

        logger.AccountCreated(account.Id, origin);
        return account;
    }

    /// <summary>
    /// Starts a web session for the account with this email address, with no OAuth provider. The account is created when it does not exist.
    /// Only the development sign-in calls this method. See docs/decisions/0054-sign-in-with-oauth-providers-only.md.
    /// </summary>
    public async Task<Session?> DevelopmentSignInAsync(string email, string? displayName, string? language, CancellationToken cancellationToken)
    {
        var normalized = AccountValidation.NormalizeEmail(email);
        var name = string.IsNullOrWhiteSpace(displayName) ? email.Trim().Split('@')[0] : displayName;
        var account = await store.FindByEmailAsync(normalized, cancellationToken)
            ?? await CreateAccountAsync(email, name, language ?? "en", "development", cancellationToken)
            ?? await store.FindByEmailAsync(normalized, cancellationToken);
        if (account is null)
        {
            return null;
        }

        logger.DevelopmentSignIn(account.Id);
        return await StartSessionAsync(account, cancellationToken);
    }

    /// <summary>
    /// Replaces a refresh token with a new one, and gives a new access token.
    /// </summary>
    /// <param name="refreshToken">The refresh token of the cookie.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The session, or <see langword="null"/> when the refresh token is not valid.</returns>
    public async Task<Session?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var connection = await store.OpenAsync(cancellationToken);
        var token = await AccountStore.FindRefreshTokenAsync(connection, Secrets.Hash(refreshToken), cancellationToken);
        if (token is null || token.RevokedAt is not null || token.ExpiresAt <= now.UtcDateTime || token.SecurityStamp != token.AccountSecurityStamp)
        {
            return null;
        }

        if (token.UsedAt is { } usedAt)
        {
            return await ReusedAsync(token, usedAt, now, cancellationToken);
        }

        var newToken = Secrets.NewToken();
        var newId = Ids.New();
        var expiresAt = now + Settings.RefreshTokenLifetime;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (!await AccountStore.UseRefreshTokenAsync(transaction, token.Id, newId, now, cancellationToken))
            {
                // Another request used the token at the same time.
                await transaction.RollbackAsync(cancellationToken);
                return await ReusedAsync(token, now.UtcDateTime, now, cancellationToken);
            }

            await AccountStore.InsertRefreshTokenAsync(connection, transaction, newId, token.AccountId, token.FamilyId, Secrets.Hash(newToken), token.SecurityStamp, now, expiresAt, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var account = await RefreshAccountAsync(token.AccountId, now, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var (accessToken, accessExpiresAt) = accessTokens.Create(account);
        return new Session(account, accessToken, accessExpiresAt, newToken, expiresAt);
    }

    public async Task SignOutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        var token = await AccountStore.FindRefreshTokenAsync(connection, Secrets.Hash(refreshToken), cancellationToken);
        if (token is not null)
        {
            await store.RevokeFamilyAsync(token.FamilyId, time.GetUtcNow(), cancellationToken);
        }
    }

    /// <summary>
    /// Signs in with an account of an OAuth provider. The method links the provider account or creates an account when necessary.
    /// </summary>
    /// <returns>The session, or the error code for the web interface.</returns>
    public async Task<(Session? Session, string? Error)> ExternalSignInAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (await store.FindAccountOfExternalLoginAsync(identity.Provider, identity.ProviderKey, cancellationToken) is { } linkedId)
        {
            var linked = await store.FindByIdAsync(linkedId, cancellationToken);
            return linked is null ? (null, AccountErrorCodes.ExternalSignInFailed) : (await StartSessionAsync(linked, cancellationToken), null);
        }

        if (string.IsNullOrWhiteSpace(identity.Email))
        {
            return (null, AccountErrorCodes.ExternalEmailMissing);
        }

        var normalized = AccountValidation.NormalizeEmail(identity.Email);
        if (!identity.EmailVerified)
        {
            return (null, AccountErrorCodes.ExternalEmailNotVerified);
        }

        var existing = await store.FindByEmailAsync(normalized, cancellationToken);
        if (existing is not null)
        {
            // The provider checked the address, so the user owns the account with this address.
            await using var connection = await store.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await AccountStore.InsertExternalLoginAsync(connection, transaction, identity.Provider, identity.ProviderKey, existing.Id, identity.Email, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.ExternalLoginLinked(identity.Provider, existing.Id);
            return (await StartSessionAsync(existing, cancellationToken), null);
        }

        var displayName = string.IsNullOrWhiteSpace(identity.Name) ? identity.Email.Split('@')[0] : identity.Name.Trim();
        if (displayName.Length > AccountValidation.MaxDisplayNameLength)
        {
            displayName = displayName[..AccountValidation.MaxDisplayNameLength];
        }

        var account = NewAccount(identity.Email.Trim(), normalized, displayName, identity.Language, now);
        if (!await CreateAsync(account, identity, cancellationToken))
        {
            // Another request created an account with the same address at the same time.
            return (null, AccountErrorCodes.ExternalSignInFailed);
        }

        logger.AccountCreated(account.Id, identity.Provider);
        return (await StartSessionAsync(account, cancellationToken), null);
    }

    public async Task<Session> StartSessionAsync(AccountRow account, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        account.IsAdministrator = IsAdministrator(account.EmailNormalized);
        await store.MarkSignInAsync(account.Id, account.IsAdministrator, now, cancellationToken);

        var refreshToken = Secrets.NewToken();
        var expiresAt = now + Settings.RefreshTokenLifetime;
        await store.InsertRefreshTokenAsync(account.Id, Ids.New(), Secrets.Hash(refreshToken), account.SecurityStamp, now, expiresAt, cancellationToken);
        var (accessToken, accessExpiresAt) = accessTokens.Create(account);
        return new Session(account, accessToken, accessExpiresAt, refreshToken, expiresAt);
    }

    private async Task<Session?> ReusedAsync(RefreshTokenRow token, DateTime usedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now.UtcDateTime - usedAt <= RefreshGracePeriod)
        {
            // Two tabs can refresh at the same time. The browser already has the new cookie, so give an access token and keep the cookie.
            var account = await RefreshAccountAsync(token.AccountId, now, cancellationToken);
            if (account is null)
            {
                return null;
            }

            var (accessToken, accessExpiresAt) = accessTokens.Create(account);
            return new Session(account, accessToken, accessExpiresAt, null, null);
        }

        // A used token came back after the grace period: a person can have a copy of the token. Stop the session.
        logger.RefreshTokenReused(token.AccountId, token.FamilyId);
        await store.RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
        return null;
    }

    private async Task<AccountRow?> RefreshAccountAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var account = await store.FindByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var isAdministrator = IsAdministrator(account.EmailNormalized);
        if (isAdministrator != account.IsAdministrator)
        {
            account.IsAdministrator = isAdministrator;
            await store.MarkSignInAsync(account.Id, isAdministrator, now, cancellationToken);
        }

        return account;
    }

    private bool IsAdministrator(string normalizedEmail) =>
        Settings.AdministratorEmails.Any(address => AccountValidation.NormalizeEmail(address) == normalizedEmail);

    private AccountRow NewAccount(string email, string normalized, string displayName, string language, DateTimeOffset now) => new()
    {
        Id = Ids.New(),
        Email = email,
        EmailNormalized = normalized,
        DisplayName = displayName,
        Language = language,
        IsAdministrator = IsAdministrator(normalized),
        SecurityStamp = Ids.New(),
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime,
    };

    /// <summary>
    /// Writes a new account, its shard assignment, its first OAuth login and the event, in one transaction.
    /// </summary>
    /// <returns><see langword="false"/> when an account with the same address exists.</returns>
    private async Task<bool> CreateAsync(AccountRow account, ExternalIdentity? external, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await AccountStore.InsertAccountAsync(transaction, account, cancellationToken);
        }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        if (external is not null)
        {
            await AccountStore.InsertExternalLoginAsync(connection, transaction, external.Provider, external.ProviderKey, account.Id, external.Email, new DateTimeOffset(account.CreatedAt), cancellationToken);
        }

        var assignment = await shards.AssignAsync(account.Id, transaction, cancellationToken);
        await events.WriteAsync(
            DatabaseTarget.Users,
            transaction,
            new AccountCreated(account.Id, account.Language, assignment.Shard, new DateTimeOffset(account.CreatedAt)),
            account.Id,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
