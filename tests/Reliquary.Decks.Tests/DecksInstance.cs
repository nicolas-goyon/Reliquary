using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Jobs;
using Reliquary.Hosting;
using Reliquary.Platform.Events;
using Reliquary.Platform.Migrations;
using Reliquary.Testing;

namespace Reliquary.Decks.Tests;

/// <summary>
/// One instance for all the deck tests: a database with the card fixtures, Redis and the API host.
/// Each test uses its own accounts, so the tests do not see the decks of the other tests.
/// </summary>
public sealed class DecksInstance : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private TestDatabase? database;
    private WebApplicationFactory<Program>? factory;
    private ServiceProvider? worker;
    private int accounts;

    /// <summary>The email address of the administrator of the instance, in the setting <c>Auth:AdministratorEmails</c>.</summary>
    public const string AdministratorEmail = "moderator@decks.example.org";

    /// <summary>The maximum number of reports of one user in 24 hours, in the setting <c>Moderation:ReportsPerDay</c>.</summary>
    public const int ReportsPerDay = 3;

    public static string FixturesDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public async Task<WebApplicationFactory<Program>> GetAsync()
    {
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (factory is null)
            {
                database = await TestServers.CreateDatabaseAsync("decks");
                await ImportCardsAsync(database.ConnectionString);
                var connectionString = database.ConnectionString;
                factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.UseSetting("ConnectionStrings:Users", connectionString);
                    builder.UseSetting("ConnectionStrings:Redis", TestServers.Redis);
                    builder.UseSetting("Auth:SigningKey", "dGVzdC1zaWduaW5nLWtleS1mb3ItdGhlLWRlY2tzLXRlc3RzLW9ubHk=");
                    builder.UseSetting("Auth:RequireCheckedEmail", "false");
                    builder.UseSetting("Auth:RequestsPerMinute", "100000");
                    builder.UseSetting("Auth:AdministratorEmails:0", AdministratorEmail);
                    builder.UseSetting("Moderation:ReportsPerDay", ReportsPerDay.ToString(System.Globalization.CultureInfo.InvariantCulture));
                });
            }

            return factory;
        }
        finally
        {
            gate.Release();
        }
    }

    public string ConnectionString => database!.ConnectionString;

    /// <summary>
    /// Sends the domain events that wait in the outboxes to the consumers of the worker, as the worker does.
    /// </summary>
    public async Task PublishEventsAsync()
    {
        await EnsureWorkerAsync();
        var publisher = worker!.GetRequiredService<OutboxPublisher>();
        await publisher.PublishPendingAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Gets the services of the worker host, on the same database as the API.</summary>
    public IServiceProvider Worker => worker ?? throw new InvalidOperationException("Call EnsureWorkerAsync first.");

    /// <summary>Builds the services of the worker host one time.</summary>
    public async Task EnsureWorkerAsync()
    {
        await GetAsync();
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (worker is null)
            {
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Users"] = database!.ConnectionString,
                    ["ConnectionStrings:Redis"] = TestServers.Redis,
                }).Build();
                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(configuration);
                services.AddLogging();
                services.AddReliquaryCore(configuration);
                services.AddReliquaryWorker(configuration);
                worker = services.BuildServiceProvider();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Registers a new account and returns a client with its access token.
    /// </summary>
    public Task<HttpClient> NewUserAsync() =>
        SignInAsync($"user{Interlocked.Increment(ref accounts)}-{Guid.NewGuid():N}@example.org");

    /// <summary>
    /// Returns a client with the access token of the administrator. The first call registers the account.
    /// </summary>
    public Task<HttpClient> AdministratorAsync() => SignInAsync(AdministratorEmail);

    private async Task<HttpClient> SignInAsync(string email)
    {
        var api = await GetAsync();
        var client = api.CreateClient();
        var credentials = new { email, password = "correct horse battery", displayName = "Tester", language = "en" };
        (await client.PostAsJsonAsync("/api/v1/auth/register", credentials, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/sign-in", credentials, TestContext.Current.CancellationToken);
        signIn.EnsureSuccessStatusCode();
        var session = await signIn.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        if (worker is not null)
        {
            await worker.DisposeAsync();
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
public sealed class DecksGroup : ICollectionFixture<DecksInstance>
{
    public const string Name = "Decks";
}
