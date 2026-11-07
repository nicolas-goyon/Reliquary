using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Reliquary.Platform.Data;

/// <summary>
/// Gives the connection strings of the instance, and keeps one connection pool for each database.
/// </summary>
public sealed class DataSources : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> pools = new(StringComparer.Ordinal);
    private readonly Lazy<string> users;
    private readonly Lazy<string> reference;
    private readonly Lazy<IReadOnlyDictionary<string, string>> shards;

    /// <summary>
    /// Creates the connection settings from the configuration.
    /// </summary>
    /// <param name="configuration">The configuration of the host.</param>
    /// <param name="options">The database settings.</param>
    public DataSources(IConfiguration configuration, IOptions<DatabaseOptions> options)
    {
        users = new(() => configuration.GetConnectionString("Users") is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException("The configuration has no connection string \"Users\". Set ConnectionStrings:Users."));
        reference = new(() => configuration.GetConnectionString("Reference") is { Length: > 0 } value ? value : users.Value);
        shards = new(() => options.Value.Shards.Count > 0
            ? new Dictionary<string, string>(options.Value.Shards, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { [DatabaseOptions.MainShard] = users.Value });
    }

    /// <summary>Gets the connection string of the users database.</summary>
    public string Users => users.Value;

    /// <summary>Gets the connection string of the reference database.</summary>
    public string Reference => reference.Value;

    /// <summary>Gets the connection string of each shard database, by shard name.</summary>
    public IReadOnlyDictionary<string, string> Shards => shards.Value;

    /// <summary>
    /// Gets the connection string of a shard database.
    /// </summary>
    /// <param name="shard">The name of the shard.</param>
    /// <returns>The connection string.</returns>
    public string Shard(string shard) =>
        Shards.TryGetValue(shard, out var value)
            ? value
            : throw new InvalidOperationException($"The configuration has no shard \"{shard}\". Add it to Database:Shards.");

    /// <summary>
    /// Gets the connection pool of a connection string. The pool exists for the life of the host.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The connection pool.</returns>
    public NpgsqlDataSource Pool(string connectionString) =>
        pools.GetOrAdd(connectionString, static value => new NpgsqlDataSourceBuilder(value).Build());

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var pool in pools.Values)
        {
            await pool.DisposeAsync();
        }

        pools.Clear();
    }
}
