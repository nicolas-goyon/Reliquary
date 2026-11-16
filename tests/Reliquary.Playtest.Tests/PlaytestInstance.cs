using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Jobs;
using Reliquary.Hosting;
using Reliquary.Platform.Migrations;
using Reliquary.Testing;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// One instance for all the endpoint tests of the online games: a database with the card fixtures, Redis and the API host.
/// Each test uses its own accounts, so the limits of an account do not change the other tests.
/// </summary>
public sealed class PlaytestInstance : IAsyncDisposable
{
    /// <summary>The maximum number of join requests from one IP address in one minute, in the setting <c>Playtest:JoinsPerIpPerMinute</c>.</summary>
    public const int JoinsPerIpPerMinute = 1000;

    private readonly SemaphoreSlim gate = new(1, 1);
    private TestDatabase? database;
    private WebApplicationFactory<Program>? factory;
    private WebApplicationFactory<Program>? second;
    private int accounts;

    public static string FixturesDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public async Task<WebApplicationFactory<Program>> GetAsync()
    {
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (factory is null)
            {
                database = await TestServers.CreateDatabaseAsync("playtest");
                await ImportCardsAsync(database.ConnectionString);
                factory = NewHost();
                second = NewHost();
            }

            return factory;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Gets a second API host on the same database and the same Redis: a second replica, for the tests of the backplane.
    /// </summary>
    public async Task<WebApplicationFactory<Program>> GetSecondAsync()
    {
        await GetAsync();
        return second!;
    }

    private WebApplicationFactory<Program> NewHost()
    {
        var connectionString = database!.ConnectionString;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Users", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", TestServers.Redis);
            builder.UseSetting("Auth:SigningKey", "dGVzdC1zaWduaW5nLWtleS1mb3ItdGhlLXBsYXl0ZXN0LXRlc3RzLW9ubHk=");
            builder.UseSetting("Auth:RequireCheckedEmail", "false");
            builder.UseSetting("Auth:RequestsPerMinute", "100000");
            builder.UseSetting("Playtest:JoinsPerIpPerMinute", JoinsPerIpPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // The gate test sends the actions of one hour in less than one minute.
            builder.UseSetting("Playtest:ActionsPerSecond", "1000");
            builder.UseSetting("Playtest:ActionBurst", "1000");
        });
    }

    /// <summary>
    /// Registers a new account and returns a client with its access token.
    /// </summary>
    public async Task<HttpClient> NewUserAsync(string displayName = "Tester")
    {
        var api = await GetAsync();
        var client = api.CreateClient();
        var credentials = new { email = $"player{Interlocked.Increment(ref accounts)}-{Guid.NewGuid():N}@example.org", password = "correct horse battery", displayName, language = "en" };
        (await client.PostAsJsonAsync("/api/v1/auth/register", credentials, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/sign-in", credentials, TestContext.Current.CancellationToken);
        signIn.EnsureSuccessStatusCode();
        var session = await signIn.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Returns a client with no credential: a guest.</summary>
    public async Task<HttpClient> GuestAsync() => (await GetAsync()).CreateClient();

    public async ValueTask DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        if (second is not null)
        {
            await second.DisposeAsync();
        }

        if (database is not null)
        {
            await database.DisposeAsync();
        }

        gate.Dispose();
    }

    private static async Task ImportCardsAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Users"] = connectionString,
            ["ConnectionStrings:Redis"] = TestServers.Redis,
            ["Cards:Import:Directory"] = FixturesDirectory,
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddReliquaryCore(configuration);
        services.AddReliquaryWorker(configuration);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<MigrationRunner>().MigrateAsync(TestContext.Current.CancellationToken);
        var import = provider.GetServices<IScheduledJob>().Single(job => job.Name == "cards.import");
        await import.RunAsync(new JobRun(Force: true), TestContext.Current.CancellationToken);
    }
}

[CollectionDefinition(Name)]
public sealed class PlaytestGroup : ICollectionFixture<PlaytestInstance>
{
    public const string Name = "Playtest";
}
