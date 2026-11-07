namespace Reliquary.BuildingBlocks.Accounts;

/// <summary>
/// The public profile of an account: the data that other users can see.
/// </summary>
/// <param name="AccountId">The ID of the account.</param>
/// <param name="DisplayName">The display name.</param>
public sealed record AccountProfile(Guid AccountId, string DisplayName);

/// <summary>
/// Gives the public profiles of the accounts to the other core components.
/// The Accounts component implements it.
/// </summary>
public interface IAccountDirectory
{
    /// <summary>
    /// Gets the public profiles of accounts.
    /// </summary>
    /// <param name="accountIds">The IDs of the accounts.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The profiles of the accounts that exist, by account ID.</returns>
    Task<IReadOnlyDictionary<Guid, AccountProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken = default);
}
