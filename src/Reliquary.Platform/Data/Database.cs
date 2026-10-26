using Npgsql;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Platform.Data;

/// <summary>
/// Opens connections to the databases of the instance.
/// Content connections go through the shard directory.
/// </summary>
/// <param name="sources">The connection settings and pools.</param>
/// <param name="directory">The shard directory.</param>
internal sealed class Database(DataSources sources, IShardDirectory directory) : IDatabase
{
    public IReadOnlyCollection<string> Shards => [.. sources.Shards.Keys];

    public ValueTask<NpgsqlConnection> OpenUsersAsync(CancellationToken cancellationToken = default) =>
        sources.Pool(sources.Users).OpenConnectionAsync(cancellationToken);

    public ValueTask<NpgsqlConnection> OpenReferenceAsync(CancellationToken cancellationToken = default) =>
        sources.Pool(sources.Reference).OpenConnectionAsync(cancellationToken);

    public ValueTask<NpgsqlConnection> OpenShardAsync(string shard, CancellationToken cancellationToken = default) =>
        sources.Pool(sources.Shard(shard)).OpenConnectionAsync(cancellationToken);

    public async ValueTask<NpgsqlConnection> OpenContentAsync(Guid ownerId, ContentAccess access, CancellationToken cancellationToken = default)
    {
        var assignment = await directory.FindAsync(ownerId, cancellationToken) ?? throw new OwnerNotFoundException(ownerId);
        if (access == ContentAccess.Write && assignment.ReadOnly)
        {
            throw new OwnerReadOnlyException(ownerId);
        }

        return await OpenShardAsync(assignment.Shard, cancellationToken);
    }
}
