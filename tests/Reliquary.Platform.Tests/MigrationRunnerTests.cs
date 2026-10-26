using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Platform.Migrations;

namespace Reliquary.Platform.Tests;

public sealed class MigrationRunnerTests
{
    [Fact]
    public async Task MigrationsCreateThePlatformTablesInEachDatabase()
    {
        await using var host = await PlatformHost.CreateAsync(databaseCount: 2);

        var usersTables = await TablesAsync(host.Databases[0].ConnectionString);
        var shardTables = await TablesAsync(host.Databases[1].ConnectionString);

        Assert.Contains("users.shard_directory", usersTables);
        Assert.Contains("users.outbox", usersTables);
        Assert.Contains("users.job_runs", usersTables);
        Assert.Contains("reference.outbox", usersTables);
        Assert.Contains("content.outbox", usersTables);
        Assert.Contains("content.test_items", usersTables);
        Assert.Contains("content.outbox", shardTables);
        Assert.Contains("content.test_items", shardTables);
        Assert.DoesNotContain("users.shard_directory", shardTables);
    }

    [Fact]
    public async Task SecondRunAppliesNoScript()
    {
        await using var host = await PlatformHost.CreateAsync(databaseCount: 2);
        var runner = host.Get<MigrationRunner>();

        var applied = await runner.MigrateAsync(TestContext.Current.CancellationToken);
        var pending = await runner.PendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, applied);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task PendingListsTheScriptsOfAnEmptyDatabase()
    {
        await using var host = await PlatformHost.CreateAsync(migrate: false);

        var pending = await host.Get<MigrationRunner>().PendingAsync(TestContext.Current.CancellationToken);

        Assert.Contains(("users", "platform/0001_platform.sql"), pending);
        Assert.Contains(("shard main", "tests/0001_test_items.sql"), pending);
    }

    [Fact]
    public async Task FailedScriptStopsTheMigrationWithTheNameOfTheScript()
    {
        var broken = new MigrationSet(DatabaseTarget.Users, "users", "broken", typeof(PlatformHost).Assembly);
        await using var host = await PlatformHost.CreateAsync(migrate: false, configure: services => services.AddSingleton(broken));

        var exception = await Assert.ThrowsAsync<MigrationException>(() => host.Get<MigrationRunner>().MigrateAsync(TestContext.Current.CancellationToken));

        Assert.Equal("users", exception.Database);
        Assert.Equal("broken/0001_broken.sql", exception.Script);
    }

    [Fact]
    public void ReadScriptsGivesTheScriptsOfOneSchemaInOrder()
    {
        var set = new MigrationSet(DatabaseTarget.Users, "users", "platform", typeof(PlatformMigrations).Assembly);

        var scripts = set.ReadScripts();

        Assert.Equal("platform/0001_platform.sql", scripts[0].Name);
        Assert.All(scripts, script => Assert.StartsWith("platform/", script.Name, StringComparison.Ordinal));
        Assert.Equal(scripts.Select(script => script.Name).Order(StringComparer.Ordinal), scripts.Select(script => script.Name));
    }

    private static async Task<HashSet<string>> TablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        var tables = await connection.QueryAsync<string>(
            "select table_schema || '.' || table_name from information_schema.tables where table_schema in ('users', 'reference', 'content')");
        return [.. tables];
    }
}
