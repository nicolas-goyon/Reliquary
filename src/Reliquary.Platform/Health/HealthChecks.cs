using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reliquary.Platform.Data;
using StackExchange.Redis;

namespace Reliquary.Platform.Health;

/// <summary>
/// The tags of the health checks.
/// </summary>
public static class HealthTags
{
    /// <summary>The checks of the readiness endpoint: the services that the host needs to serve requests.</summary>
    public const string Ready = "ready";
}

/// <summary>
/// Checks that each PostgreSQL database of the instance answers: the users database, the reference database and each shard.
/// </summary>
/// <param name="sources">The connection settings.</param>
internal sealed class PostgresHealthCheck(DataSources sources) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var connectionString in new[] { sources.Users, sources.Reference }.Concat(sources.Shards.Values).Distinct(StringComparer.Ordinal))
            {
                await using var connection = await sources.Pool(connectionString).OpenConnectionAsync(cancellationToken);
                await connection.ExecuteScalarAsync<int>(new CommandDefinition("select 1", cancellationToken: cancellationToken));
            }

            return HealthCheckResult.Healthy("All the PostgreSQL databases answer.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("A PostgreSQL database does not answer.", exception);
        }
    }
}

/// <summary>
/// Checks that the Redis server answers.
/// </summary>
/// <param name="redis">The Redis connection.</param>
internal sealed class RedisHealthCheck(Lazy<IConnectionMultiplexer> redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.Value.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy("Redis answers.");
        }
        catch (Exception exception) when (exception is RedisException or InvalidOperationException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Redis does not answer.", exception);
        }
    }
}
