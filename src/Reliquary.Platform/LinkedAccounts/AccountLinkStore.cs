using Dapper;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Platform.LinkedAccounts;

/// <summary>A linked account as the table <c>content.account_links</c> stores it.</summary>
internal sealed class AccountLinkRow
{
    public Guid OwnerId { get; set; }

    public string ExtensionId { get; set; } = string.Empty;

    public string Site { get; set; } = string.Empty;

    public string AuthMethod { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public byte[]? SecretCiphertext { get; set; }

    public string? KeyId { get; set; }

    public string State { get; set; } = AccountLinkStates.Linked;

    public DateTime? LastSyncAt { get; set; }

    public string? LastSyncError { get; set; }

    public DateTime LinkedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Gets the encrypted secret, or <see langword="null"/> when the link has no secret.</summary>
    public SealedSecret? Secret => SecretCiphertext is { } ciphertext && KeyId is { } keyId ? new SealedSecret(keyId, ciphertext) : null;
}

/// <summary>The states of a linked account.</summary>
internal static class AccountLinkStates
{
    /// <summary>The user linked the account. No sync completed yet.</summary>
    public const string Linked = "linked";

    /// <summary>The last sync completed.</summary>
    public const string Synced = "synced";

    /// <summary>The last sync failed.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// Reads and writes the table <c>content.account_links</c> in the database of the owner.
/// </summary>
/// <param name="database">The databases of the instance.</param>
internal sealed class AccountLinkStore(IDatabase database)
{
    private const string Columns = """
        owner_id as OwnerId, extension_id as ExtensionId, site, auth_method as AuthMethod, display_name as DisplayName,
        secret_ciphertext as SecretCiphertext, key_id as KeyId, state, last_sync_at as LastSyncAt, last_sync_error as LastSyncError,
        linked_at as LinkedAt, updated_at as UpdatedAt
        """;

    /// <summary>Gets all the linked accounts of an owner, in the order of the plugin and the site.</summary>
    public async Task<IReadOnlyList<AccountLinkRow>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Read, cancellationToken);
        var rows = await connection.QueryAsync<AccountLinkRow>(new CommandDefinition(
            $"select {Columns} from content.account_links where owner_id = @ownerId order by extension_id, site",
            new { ownerId },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    /// <summary>Gets one linked account, or <see langword="null"/>.</summary>
    public async Task<AccountLinkRow?> FindAsync(Guid ownerId, string extensionId, string site, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountLinkRow>(new CommandDefinition(
            $"select {Columns} from content.account_links where owner_id = @ownerId and extension_id = @extensionId and site = @site",
            new { ownerId, extensionId, site },
            cancellationToken: cancellationToken));
    }

    /// <summary>Adds a linked account, or replaces the linked account of the same plugin and site. A new link has no sync.</summary>
    public async Task SaveAsync(AccountLinkRow row, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(row.OwnerId, ContentAccess.Write, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.account_links (owner_id, extension_id, site, auth_method, display_name, secret_ciphertext, key_id,
                state, last_sync_at, last_sync_error, linked_at, updated_at)
            values (@OwnerId, @ExtensionId, @Site, @AuthMethod, @DisplayName, @SecretCiphertext, @KeyId,
                @State, @LastSyncAt, @LastSyncError, @LinkedAt, @UpdatedAt)
            on conflict (owner_id, extension_id, site) do update
            set auth_method = excluded.auth_method, display_name = excluded.display_name, secret_ciphertext = excluded.secret_ciphertext,
                key_id = excluded.key_id, state = excluded.state, last_sync_at = excluded.last_sync_at,
                last_sync_error = excluded.last_sync_error, linked_at = excluded.linked_at, updated_at = excluded.updated_at
            """,
            row,
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Replaces the encrypted secret of a linked account, when the secret did not change since the read.
    /// The vault uses it to encrypt an old secret again with the current key.
    /// </summary>
    /// <returns><see langword="true"/> when the row changed.</returns>
    public async Task<bool> ReplaceSecretAsync(AccountLinkRow row, SealedSecret previous, SealedSecret next, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(row.OwnerId, ContentAccess.Write, cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            update content.account_links set secret_ciphertext = @nextCiphertext, key_id = @nextKeyId
            where owner_id = @OwnerId and extension_id = @ExtensionId and site = @Site
                and key_id = @previousKeyId and secret_ciphertext = @previousCiphertext
            """,
            new
            {
                row.OwnerId,
                row.ExtensionId,
                row.Site,
                nextCiphertext = next.Ciphertext,
                nextKeyId = next.KeyId,
                previousKeyId = previous.KeyId,
                previousCiphertext = previous.Ciphertext,
            },
            cancellationToken: cancellationToken)) == 1;
    }

    /// <summary>Deletes a linked account and its secret.</summary>
    /// <returns><see langword="true"/> when the link existed.</returns>
    public async Task<bool> DeleteAsync(Guid ownerId, string extensionId, string site, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Write, cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "delete from content.account_links where owner_id = @ownerId and extension_id = @extensionId and site = @site",
            new { ownerId, extensionId, site },
            cancellationToken: cancellationToken)) == 1;
    }
}
