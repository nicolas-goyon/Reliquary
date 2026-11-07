using Npgsql;

namespace Reliquary.Testing;

/// <summary>
/// Gives the PostgreSQL and Redis servers of the integration tests.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>RELIQUARY_TEST_POSTGRES</c> is the connection string of a PostgreSQL server, with a user that can create databases.
/// The default is the server of <c>just infra</c>.</item>
/// <item><c>RELIQUARY_TEST_REDIS</c> is the address of a Redis server. The default is the server of <c>just infra</c>.</item>
/// </list>
/// When a server is not available, the tests skip on a computer, and fail in CI.
/// </remarks>
public static class TestServers
{
    /// <summary>Gets the connection string of the PostgreSQL server.</summary>
    public static string Postgres { get; } =
        Environment.GetEnvironmentVariable("RELIQUARY_TEST_POSTGRES")
        ?? "Host=localhost;Port=15432;Database=postgres;Username=reliquary;Password=reliquary";

    /// <summary>Gets the address of the Redis server.</summary>
    public static string Redis { get; } =
        Environment.GetEnvironmentVariable("RELIQUARY_TEST_REDIS") ?? "localhost:16379";

    private static bool InContinuousIntegration =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an empty database on the PostgreSQL server.
    /// </summary>
    /// <param name="prefix">A short name that tells which test created the database.</param>
    /// <returns>The database. Dispose it to delete the database.</returns>
    public static async Task<TestDatabase> CreateDatabaseAsync(string prefix = "test")
    {
        var name = $"reliquary_{prefix}_{Guid.NewGuid():N}";
        name = name.Length > 63 ? name[..63] : name;

        try
        {
            await using var connection = new NpgsqlConnection(Postgres);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand($"create database {name}", connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            Unavailable("PostgreSQL", exception);
        }

        return new TestDatabase(new NpgsqlConnectionStringBuilder(Postgres) { Database = name }.ConnectionString, name);
    }

    /// <summary>
    /// Skips the test on a computer, or fails it in CI, because a server is not available.
    /// </summary>
    /// <param name="server">The name of the server.</param>
    /// <param name="exception">The connection error.</param>
    public static void Unavailable(string server, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = $"{server} is not available: {exception.Message} Run \"just infra\", or set the RELIQUARY_TEST_* variables.";
        if (InContinuousIntegration)
        {
            Assert.Fail(message);
        }

        Assert.Skip(message);
    }
}

/// <summary>
/// A database that a test created. Dispose it to delete the database.
/// </summary>
/// <param name="connectionString">The connection string of the database.</param>
/// <param name="name">The name of the database.</param>
public sealed class TestDatabase(string connectionString, string name) : IAsyncDisposable
{
    /// <summary>Gets the connection string of the database.</summary>
    public string ConnectionString { get; } = connectionString;

    /// <summary>Gets the name of the database.</summary>
    public string Name { get; } = name;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(TestServers.Postgres);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"drop database if exists {Name} with (force)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
