using Reliquary.BuildingBlocks.Events;

namespace Reliquary.Accounts;

/// <summary>
/// A user made a new account. The other core components can prepare the data of the new owner.
/// </summary>
/// <param name="AccountId">The ID of the account. The owner of the user data has the same ID.</param>
/// <param name="Language">The interface language of the account.</param>
/// <param name="Shard">The shard database of the new owner.</param>
/// <param name="CreatedAt">The time of the creation.</param>
[DomainEvent("accounts.account.created", 1)]
public sealed record AccountCreated(Guid AccountId, string Language, string Shard, DateTimeOffset CreatedAt) : IDomainEvent;
