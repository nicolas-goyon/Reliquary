using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Cards.Import;
using Reliquary.Hosting;
using Reliquary.Platform.Migrations;
using Reliquary.Testing;

namespace Reliquary.Cards.Tests;

/// <summary>
/// A test database with the migrations of all the core components and the services of the worker.
/// </summary>
internal sealed class CardsDatabase : IAsyncDisposable
{
    private CardsDatabase(TestDatabase database, ServiceProvider services)
    {
        Database = database;
        Services = services;
    }

    /// <summary>Gets the folder of the card fixtures.</summary>
    public static string FixturesDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public TestDatabase Database { get; }

    public ServiceProvider Services { get; }

    public static async Task<CardsDatabase> CreateAsync(string? importDirectory = null)
    {
        var database = await TestServers.CreateDatabaseAsync("cards");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Users"] = database.ConnectionString,
            ["Cards:Import:Directory"] = importDirectory ?? FixturesDirectory,
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddReliquaryCore(configuration);
        services.AddReliquaryWorker(configuration);
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<MigrationRunner>().MigrateAsync(TestContext.Current.CancellationToken);
        return new CardsDatabase(database, provider);
    }

    public Task<IReadOnlyList<SourceImportResult>> ImportAsync(bool force = true) =>
        Services.GetRequiredService<CardDataImport>().RunAsync(force, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await Database.DisposeAsync();
    }
}

/// <summary>
/// One database with the imported fixtures, shared by the tests that only read the data.
/// </summary>
public sealed class ImportedCardsFixture : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CardsDatabase? database;

    internal async Task<CardsDatabase> GetAsync()
    {
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (database is null)
            {
                var created = await CardsDatabase.CreateAsync();
                await created.ImportAsync();
                database = created;
            }

            return database;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }

        gate.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class ImportedCardsGroup : ICollectionFixture<ImportedCardsFixture>
{
    public const string Name = "Imported cards";
}
