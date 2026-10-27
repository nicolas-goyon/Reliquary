using Npgsql;

namespace Reliquary.BuildingBlocks.Data;

/// <summary>
/// The shard directory: it gives the shard database of each owner.
/// </summary>
public interface IShardDirectory
{
    /// <summary>
    /// Gets the shard assignment of an owner.
    /// </summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The assignment, or <see langword="null"/> when the directory has no entry for the owner.</returns>
    ValueTask<ShardAssignment?> FindAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a new owner to the default shard, in the transaction of the caller.
    /// When the owner has an assignment, the method keeps it.
    /// </summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="transaction">A transaction on the database of the <c>users</c> schema.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The assignment of the owner.</returns>
    Task<ShardAssignment> AssignAsync(Guid ownerId, NpgsqlTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the assignment of a deleted owner, in the transaction of the caller.
    /// </summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="transaction">A transaction on the database of the <c>users</c> schema.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the directory has no entry for the owner.</returns>
    Task RemoveAsync(Guid ownerId, NpgsqlTransaction transaction, CancellationToken cancellationToken = default);
}

/// <summary>
/// The shard database of an owner.
/// </summary>
/// <param name="OwnerId">The owner.</param>
/// <param name="Shard">The name of the shard database.</param>
/// <param name="ReadOnly">Whether the data of the owner is read-only, because it moves to another database.</param>
public sealed record ShardAssignment(Guid OwnerId, string Shard, bool ReadOnly);
