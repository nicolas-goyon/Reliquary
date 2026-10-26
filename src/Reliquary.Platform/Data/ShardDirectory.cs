using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Platform.Data;

/// <summary>
/// The shard directory in the table <c>users.shard_directory</c>.
/// </summary>
/// <remarks>
/// The directory keeps each assignment in memory for a short time, so most requests do not read the table.
/// </remarks>
/// <param name="sources">The connection settings and pools.</param>
/// <param name="cache">The memory cache of the host.</param>
/// <param name="options">The database settings.</param>
internal sealed class ShardDirectory(DataSources sources, IMemoryCache cache, IOptions<DatabaseOptions> options) : IShardDirectory
{
    private const string FindSql = """
        select owner_id as OwnerId, shard as Shard, read_only as ReadOnly
        from users.shard_directory
        where owner_id = @ownerId
        """;

    private const string AssignSql = """
        insert into users.shard_directory (owner_id, shard)
        values (@ownerId, @shard)
        on conflict (owner_id) do nothing;
        select owner_id as OwnerId, shard as Shard, read_only as ReadOnly
        from users.shard_directory
        where owner_id = @ownerId;
        """;

    public async ValueTask<ShardAssignment?> FindAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey(ownerId), out ShardAssignment? cached))
        {
            return cached;
        }

        await using var connection = await sources.Pool(sources.Users).OpenConnectionAsync(cancellationToken);
        var assignment = await connection.QuerySingleOrDefaultAsync<ShardAssignment>(
            new CommandDefinition(FindSql, new { ownerId }, cancellationToken: cancellationToken));

        if (assignment is not null)
        {
            cache.Set(CacheKey(ownerId), assignment, options.Value.DirectoryCacheDuration);
        }

        return assignment;
    }

    public async Task<ShardAssignment> AssignAsync(Guid ownerId, NpgsqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        var shard = options.Value.DefaultShard;
        _ = sources.Shard(shard);

        return await transaction.Connection!.QuerySingleAsync<ShardAssignment>(
            new CommandDefinition(AssignSql, new { ownerId, shard }, transaction, cancellationToken: cancellationToken));
    }

    private static string CacheKey(Guid ownerId) => $"shard-directory:{ownerId}";
}
