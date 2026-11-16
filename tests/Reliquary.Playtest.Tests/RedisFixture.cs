using Reliquary.Testing;
using StackExchange.Redis;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// One connection to the Redis server of the integration tests, for all the tests of the collection.
/// Each test uses its own game IDs, so the tests do not see the games of the other tests.
/// </summary>
public sealed class RedisFixture : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ConnectionMultiplexer? connection;
    private Exception? failure;

    /// <summary>
    /// Gets the connection. The test skips on a computer, or fails in CI, when Redis is not available.
    /// </summary>
    public async Task<IConnectionMultiplexer> GetAsync()
    {
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (connection is null && failure is null)
            {
                try
                {
                    connection = await ConnectionMultiplexer.ConnectAsync(TestServers.Redis);
                }
                catch (RedisConnectionException exception)
                {
                    failure = exception;
                }
            }
        }
        finally
        {
            gate.Release();
        }

        if (failure is not null)
        {
            TestServers.Unavailable("Redis", failure);
        }

        return connection!;
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }

        gate.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class RedisGroup : ICollectionFixture<RedisFixture>
{
    public const string Name = "Redis";
}
