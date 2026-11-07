using Dapper;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Accounts.Data;

/// <summary>
/// Gives the public profiles of the accounts to the other core components.
/// </summary>
internal sealed class AccountDirectory(IDatabase database) : IAccountDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, AccountProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, AccountProfile>();
        }

        await using var connection = await database.OpenUsersAsync(cancellationToken);
        var rows = await connection.QueryAsync<(Guid Id, string DisplayName)>(new CommandDefinition(
            "select id, display_name from users.accounts where id = any(@ids)",
            new { ids = accountIds.ToArray() },
            cancellationToken: cancellationToken));
        return rows.ToDictionary(row => row.Id, row => new AccountProfile(row.Id, row.DisplayName));
    }
}
