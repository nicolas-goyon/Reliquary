using Npgsql;

namespace Reliquary.BuildingBlocks.Data;

/// <summary>
/// Opens connections to the databases of the instance.
/// </summary>
/// <remarks>
/// Each method returns an open connection. The caller must dispose it.
/// Content connections go through the shard directory, so the data of each owner can move to another database.
/// </remarks>
public interface IDatabase
{
    /// <summary>
    /// Gets the names of the shard databases.
    /// </summary>
    IReadOnlyCollection<string> Shards { get; }

    /// <summary>
    /// Opens a connection to the database of the <c>users</c> schema.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>An open connection.</returns>
    ValueTask<NpgsqlConnection> OpenUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a connection to the database of the <c>reference</c> schema.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>An open connection.</returns>
    ValueTask<NpgsqlConnection> OpenReferenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a connection to the shard database that contains the data of an owner.
    /// </summary>
    /// <param name="ownerId">The owner of the data.</param>
    /// <param name="access">The access that the caller needs.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>An open connection.</returns>
    /// <exception cref="OwnerNotFoundException">The shard directory has no entry for the owner.</exception>
    /// <exception cref="OwnerReadOnlyException">The access is <see cref="ContentAccess.Write"/>, and the data of the owner is read-only during a move.</exception>
    ValueTask<NpgsqlConnection> OpenContentAsync(Guid ownerId, ContentAccess access, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a connection to a shard database by its name.
    /// </summary>
    /// <param name="shard">The name of the shard.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>An open connection.</returns>
    ValueTask<NpgsqlConnection> OpenShardAsync(string shard, CancellationToken cancellationToken = default);
}

/// <summary>
/// The access that a caller needs to the content of an owner.
/// </summary>
public enum ContentAccess
{
    /// <summary>The caller reads the data only.</summary>
    Read,

    /// <summary>The caller can change the data.</summary>
    Write,
}

/// <summary>
/// The shard directory has no entry for an owner.
/// </summary>
/// <param name="ownerId">The owner.</param>
public sealed class OwnerNotFoundException(Guid ownerId)
    : Exception($"The shard directory has no entry for the owner {ownerId}.")
{
    /// <summary>Gets the owner.</summary>
    public Guid OwnerId { get; } = ownerId;
}

/// <summary>
/// The data of an owner is read-only, because the data moves to another database.
/// </summary>
/// <param name="ownerId">The owner.</param>
public sealed class OwnerReadOnlyException(Guid ownerId)
    : Exception($"The data of the owner {ownerId} is read-only. The data moves to another database. Try again later.")
{
    /// <summary>Gets the owner.</summary>
    public Guid OwnerId { get; } = ownerId;
}
