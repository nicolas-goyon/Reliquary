using Dapper;
using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Accounts.Data;

/// <summary>
/// An account as the <c>users.accounts</c> table stores it.
/// </summary>
internal sealed class AccountRow
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string EmailNormalized { get; set; } = string.Empty;

    public DateTime? EmailCheckedAt { get; set; }

    public string? PasswordHash { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public bool IsAdministrator { get; set; }

    public Guid SecurityStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastSignInAt { get; set; }
}

/// <summary>
/// A refresh token with the security stamp of its account.
/// </summary>
internal sealed class RefreshTokenRow
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid FamilyId { get; set; }

    public Guid SecurityStamp { get; set; }

    public Guid AccountSecurityStamp { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// An API key, with the fields of its account that the authentication needs.
/// </summary>
internal sealed class ApiKeyRow
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public byte[] SecretHash { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public bool IsAdministrator { get; set; }
}

/// <summary>
/// Reads and writes the tables of the Accounts component in the <c>users</c> schema.
/// </summary>
/// <param name="database">The databases of the instance.</param>
internal sealed class AccountStore(IDatabase database)
{
    private const string AccountColumns = """
        id, email, email_normalized as EmailNormalized, email_checked_at as EmailCheckedAt, password_hash as PasswordHash,
        display_name as DisplayName, language, is_administrator as IsAdministrator, security_stamp as SecurityStamp,
        created_at as CreatedAt, updated_at as UpdatedAt, last_sign_in_at as LastSignInAt
        """;

    public ValueTask<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) => database.OpenUsersAsync(cancellationToken);

    public async Task<AccountRow?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            $"select {AccountColumns} from users.accounts where email_normalized = @normalizedEmail",
            new { normalizedEmail },
            cancellationToken: cancellationToken));
    }

    public async Task<AccountRow?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            $"select {AccountColumns} from users.accounts where id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public static Task InsertAccountAsync(NpgsqlTransaction transaction, AccountRow account, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            insert into users.accounts (id, email, email_normalized, email_checked_at, password_hash, display_name, language,
                is_administrator, security_stamp, created_at, updated_at, last_sign_in_at)
            values (@Id, @Email, @EmailNormalized, @EmailCheckedAt, @PasswordHash, @DisplayName, @Language,
                @IsAdministrator, @SecurityStamp, @CreatedAt, @UpdatedAt, @LastSignInAt)
            """,
            account,
            transaction,
            cancellationToken: cancellationToken));

    public async Task MarkSignInAsync(Guid id, bool isAdministrator, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "update users.accounts set last_sign_in_at = @now, is_administrator = @isAdministrator where id = @id",
            new { id, now, isAdministrator },
            cancellationToken: cancellationToken));
    }

    public async Task<AccountRow?> UpdateProfileAsync(Guid id, string? displayName, string? language, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            $"""
            update users.accounts
            set display_name = coalesce(@displayName, display_name), language = coalesce(@language, language), updated_at = @now
            where id = @id
            returning {AccountColumns}
            """,
            new { id, displayName, language, now },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Sets a new password and a new security stamp, and revokes all the refresh tokens of the account.
    /// </summary>
    public static async Task SetPasswordAsync(NpgsqlTransaction transaction, Guid id, string passwordHash, bool checkEmail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            update users.accounts
            set password_hash = @passwordHash, security_stamp = @stamp, updated_at = @now,
                email_checked_at = case when @checkEmail then coalesce(email_checked_at, @now) else email_checked_at end
            where id = @id
            """,
            new { id, passwordHash, stamp = Ids.New(), checkEmail, now },
            transaction,
            cancellationToken: cancellationToken));
        await RevokeAllRefreshTokensAsync(transaction, id, now, cancellationToken);
    }

    public static Task<int> CheckEmailAsync(NpgsqlTransaction transaction, Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "update users.accounts set email_checked_at = coalesce(email_checked_at, @now), updated_at = @now where id = @id",
            new { id, now },
            transaction,
            cancellationToken: cancellationToken));

    public async Task InsertAccountTokenAsync(Guid accountId, string purpose, byte[] tokenHash, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.account_tokens (id, account_id, purpose, token_hash, created_at, expires_at)
            values (@id, @accountId, @purpose, @tokenHash, @now, @expiresAt)
            """,
            new { id = Ids.New(), accountId, purpose, tokenHash, now, expiresAt },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Marks a single-use token as used, when it is valid.
    /// </summary>
    /// <returns>The account of the token, or <see langword="null"/> when the token is not valid.</returns>
    public static Task<Guid?> UseAccountTokenAsync(NpgsqlTransaction transaction, string purpose, byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            update users.account_tokens set used_at = @now
            where purpose = @purpose and token_hash = @tokenHash and used_at is null and expires_at > @now
            returning account_id
            """,
            new { purpose, tokenHash, now },
            transaction,
            cancellationToken: cancellationToken));

    public async Task InsertRefreshTokenAsync(Guid accountId, Guid familyId, byte[] tokenHash, Guid securityStamp, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await InsertRefreshTokenAsync(connection, null, Ids.New(), accountId, familyId, tokenHash, securityStamp, now, expiresAt, cancellationToken);
    }

    public static Task InsertRefreshTokenAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, Guid accountId, Guid familyId, byte[] tokenHash, Guid securityStamp, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.refresh_tokens (id, account_id, family_id, token_hash, security_stamp, created_at, expires_at)
            values (@id, @accountId, @familyId, @tokenHash, @securityStamp, @now, @expiresAt)
            """,
            new { id, accountId, familyId, tokenHash, securityStamp, now, expiresAt },
            transaction,
            cancellationToken: cancellationToken));

    public static Task<RefreshTokenRow?> FindRefreshTokenAsync(NpgsqlConnection connection, byte[] tokenHash, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<RefreshTokenRow>(new CommandDefinition(
            """
            select t.id, t.account_id as AccountId, t.family_id as FamilyId, t.security_stamp as SecurityStamp,
                a.security_stamp as AccountSecurityStamp, t.expires_at as ExpiresAt, t.used_at as UsedAt, t.revoked_at as RevokedAt
            from users.refresh_tokens t
            join users.accounts a on a.id = t.account_id
            where t.token_hash = @tokenHash
            """,
            new { tokenHash },
            cancellationToken: cancellationToken));

    /// <summary>
    /// Marks a refresh token as used and replaced. Only one request can do this.
    /// </summary>
    /// <returns><see langword="true"/> when this request marked the token.</returns>
    public static async Task<bool> UseRefreshTokenAsync(NpgsqlTransaction transaction, Guid id, Guid replacedBy, DateTimeOffset now, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "update users.refresh_tokens set used_at = @now, replaced_by = @replacedBy where id = @id and used_at is null and revoked_at is null",
            new { id, replacedBy, now },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "update users.refresh_tokens set revoked_at = @now where family_id = @familyId and revoked_at is null",
            new { familyId, now },
            cancellationToken: cancellationToken));
    }

    public static Task RevokeAllRefreshTokensAsync(NpgsqlTransaction transaction, Guid accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "update users.refresh_tokens set revoked_at = @now where account_id = @accountId and revoked_at is null",
            new { accountId, now },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<Guid?> FindAccountOfExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "select account_id from users.external_logins where provider = @provider and provider_key = @providerKey",
            new { provider, providerKey },
            cancellationToken: cancellationToken));
    }

    public static Task InsertExternalLoginAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string provider, string providerKey, Guid accountId, string? email, DateTimeOffset now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.external_logins (provider, provider_key, account_id, email, created_at)
            values (@provider, @providerKey, @accountId, @email, @now)
            on conflict (provider, provider_key) do nothing
            """,
            new { provider, providerKey, accountId, email, now },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<string>> ListExternalProvidersAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var providers = await connection.QueryAsync<string>(new CommandDefinition(
            "select provider from users.external_logins where account_id = @accountId order by provider",
            new { accountId },
            cancellationToken: cancellationToken));
        return providers.ToList();
    }

    public async Task<IReadOnlyList<ApiKeyRow>> ListApiKeysAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var keys = await connection.QueryAsync<ApiKeyRow>(new CommandDefinition(
            """
            select id, account_id as AccountId, name, prefix, created_at as CreatedAt, last_used_at as LastUsedAt
            from users.api_keys
            where account_id = @accountId and revoked_at is null
            order by created_at desc, id desc
            """,
            new { accountId },
            cancellationToken: cancellationToken));
        return keys.ToList();
    }

    public async Task<int> CountApiKeysAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from users.api_keys where account_id = @accountId and revoked_at is null",
            new { accountId },
            cancellationToken: cancellationToken));
    }

    public async Task InsertApiKeyAsync(ApiKeyRow key, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.api_keys (id, account_id, name, prefix, secret_hash, created_at)
            values (@Id, @AccountId, @Name, @Prefix, @SecretHash, @CreatedAt)
            """,
            key,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> RevokeApiKeyAsync(Guid accountId, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "update users.api_keys set revoked_at = @now where id = @id and account_id = @accountId and revoked_at is null",
            new { id, accountId, now },
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task<ApiKeyRow?> FindActiveApiKeyAsync(string prefix, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ApiKeyRow>(new CommandDefinition(
            """
            select k.id, k.account_id as AccountId, k.name, k.prefix, k.secret_hash as SecretHash, k.created_at as CreatedAt,
                k.last_used_at as LastUsedAt, a.display_name as DisplayName, a.language, a.is_administrator as IsAdministrator
            from users.api_keys k
            join users.accounts a on a.id = k.account_id
            where k.prefix = @prefix and k.revoked_at is null
            """,
            new { prefix },
            cancellationToken: cancellationToken));
    }

    public async Task MarkApiKeyUsedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "update users.api_keys set last_used_at = @now where id = @id",
            new { id, now },
            cancellationToken: cancellationToken));
    }
}
