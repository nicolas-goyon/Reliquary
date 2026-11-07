using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Platform.Migrations;
using Reliquary.Testing;

namespace Reliquary.Platform.Tests;

/// <summary>
/// The platform services on new test databases. The first database is the users database and the shard "main".
/// The other databases are the shards "shard2", "shard3" and so on.
/// </summary>
internal sealed class PlatformHost : IAsyncDisposable
{
    private readonly List<TestDatabase> databases;
    private readonly ServiceProvider provider;

    private PlatformHost(List<TestDatabase> databases, ServiceProvider provider, FakeTimeProvider time)
    {
        this.databases = databases;
        this.provider = provider;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    public IReadOnlyList<TestDatabase> Databases => databases;

    public static async Task<PlatformHost> CreateAsync(int databaseCount = 1, Action<IServiceCollection>? configure = null, bool migrate = true)
    {
        var databases = new List<TestDatabase>();
        for (var index = 0; index < databaseCount; index++)
        {
            databases.Add(await TestServers.CreateDatabaseAsync("platform"));
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Users"] = databases[0].ConnectionString,
            ["Database:Shards:main"] = databases[0].ConnectionString,
            ["Database:DirectoryCacheDuration"] = "00:00:00.001",
        };
        for (var index = 1; index < databaseCount; index++)
        {
            settings[$"Database:Shards:shard{index + 1}"] = databases[index].ConnectionString;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddReliquaryPlatform(configuration);
        services.AddReliquaryWorkerPlatform();
        services.AddSingleton(new MigrationSet(DatabaseTarget.Content, "content", "tests", typeof(PlatformHost).Assembly));
        configure?.Invoke(services);

        var host = new PlatformHost(databases, services.BuildServiceProvider(), time);
        if (migrate)
        {
            await host.Get<MigrationRunner>().MigrateAsync(TestContext.Current.CancellationToken);
        }

        return host;
    }

    public T Get<T>()
        where T : notnull => provider.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        foreach (var database in databases)
        {
            await database.DisposeAsync();
        }
    }
}
