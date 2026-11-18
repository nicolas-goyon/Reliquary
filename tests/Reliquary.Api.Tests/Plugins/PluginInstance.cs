using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Platform.Extensions;
using Reliquary.Platform.Migrations;
using Reliquary.Testing;

namespace Reliquary.Api.Tests.Plugins;

/// <summary>
/// One API instance for all the tests of a test class. Each test installs a plugin with its own ID, see <see cref="PluginInstance.NewId"/>.
/// One instance for each test class keeps the load of the tests small, because other test assemblies run at the same time.
/// </summary>
public sealed class PluginInstanceFixture : IAsyncLifetime
{
    private PluginInstance? instance;

    internal PluginInstance Instance => instance ?? throw new InvalidOperationException("The fixture is not ready.");

    public async ValueTask InitializeAsync() => instance = await PluginInstance.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        if (instance is not null)
        {
            await instance.DisposeAsync();
        }
    }
}

/// <summary>
/// An API instance with its own database for the plugin tests, and an administrator account.
/// </summary>
internal sealed class PluginInstance : IAsyncDisposable
{
    private static int nextId;

    private readonly SemaphoreSlim administratorLock = new(1, 1);

    private HttpClient? administrator;

    /// <summary>The email address of the administrator.</summary>
    public const string AdministratorEmail = "admin@example.org";

    /// <summary>The content of the entry file of the test plugins.</summary>
    public static readonly byte[] EntryContent = Encoding.UTF8.GetBytes("console.log('hello');\n");

    private PluginInstance(TestDatabase database, WebApplicationFactory<Program> factory)
    {
        Database = database;
        Factory = factory;
    }

    public TestDatabase Database { get; }

    public WebApplicationFactory<Program> Factory { get; }

    public PluginFileStore Files => Factory.Services.GetRequiredService<PluginFileStore>();

    /// <summary>Gets a new plugin ID, so the tests of one instance do not share a plugin.</summary>
    public static string NewId() => $"plugin-{Interlocked.Increment(ref nextId)}";

    /// <summary>
    /// Gets the client of the administrator. The instance signs in the administrator one time, and the instance disposes the client.
    /// </summary>
    public async Task<HttpClient> AdministratorAsync()
    {
        await administratorLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            return administrator ??= await SignInAsync(AdministratorEmail);
        }
        finally
        {
            administratorLock.Release();
        }
    }

    public static Task<PluginInstance> CreateAsync(params (string Key, string Value)[] settings) => CreateAsync(null, settings);

    /// <summary>
    /// Creates an instance with settings and changes of the services, for example the HTTP handler of the plugin catalog.
    /// </summary>
    public static async Task<PluginInstance> CreateAsync(Action<IServiceCollection>? services, params (string Key, string Value)[] settings)
    {
        var database = await TestServers.CreateDatabaseAsync("plugins");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Users", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", TestServers.Redis);
            builder.UseSetting("Auth:SigningKey", "dGVzdC1zaWduaW5nLWtleS1mb3ItdGhlLXBsdWdpbi10ZXN0cw==");
            builder.UseSetting("Auth:AllowDevelopmentSignIn", "true");
            builder.UseSetting("Auth:RequestsPerMinute", "1000");
            builder.UseSetting("Auth:AdministratorEmails:0", AdministratorEmail);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            if (services is not null)
            {
                builder.ConfigureTestServices(services);
            }
        });
        await factory.Services.GetRequiredService<MigrationRunner>().MigrateAsync(TestContext.Current.CancellationToken);
        return new PluginInstance(database, factory);
    }

    /// <summary>
    /// Makes a plugin manifest with the ID "hello" and the version 1.0.0. The values of <paramref name="fields"/> replace the default fields.
    /// </summary>
    public static Dictionary<string, object?> Manifest(params (string Key, object? Value)[] fields)
    {
        var manifest = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = "hello",
            ["name"] = "Hello",
            ["version"] = "1.0.0",
            ["coreApi"] = "^1.0",
            ["entry"] = "dist/index.js",
        };
        foreach (var (key, value) in fields)
        {
            manifest[key] = value;
        }

        return manifest;
    }

    /// <summary>
    /// Registers an account, signs in and returns a client with the access token.
    /// </summary>
    public async Task<HttpClient> SignInAsync(string email)
    {
        var client = Factory.CreateClient();
        var credentials = new { email, displayName = email.Split('@')[0], language = "en" };
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/development/sign-in", credentials, TestContext.Current.CancellationToken);
        var session = await signIn.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>
    /// Installs and enables a plugin with the administrator account, and keeps its files.
    /// </summary>
    public async Task InstallAsync(IReadOnlyDictionary<string, object?> manifest, params PluginFile[] files)
    {
        var admin = await AdministratorAsync();
        var id = (string)manifest["id"]!;
        (await admin.PutAsJsonAsync($"/api/v1/admin/extensions/{id}", manifest, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await admin.PatchAsJsonAsync($"/api/v1/admin/extensions/{id}", new { enabled = true }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var stored = files.Length > 0 ? files : [new PluginFile((string)manifest["entry"]!, EntryContent)];
        await Files.SaveVersionAsync(id, (string)manifest["version"]!, stored, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Activates a plugin for the user, with the consent for all the access of the installed version.
    /// </summary>
    public static async Task ActivateAsync(HttpClient user, string id)
    {
        var cancel = TestContext.Current.CancellationToken;
        var plugin = (await user.GetFromJsonAsync<JsonElement>("/api/v1/extensions", cancel)).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id);
        var consent = new
        {
            version = plugin.GetProperty("version").GetString(),
            scopes = plugin.GetProperty("permissions"),
            network = plugin.GetProperty("network"),
            devices = plugin.GetProperty("devices"),
            sites = plugin.GetProperty("sites"),
        };
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true, consent }, cancel)).EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        administrator?.Dispose();
        administratorLock.Dispose();
        await Factory.DisposeAsync();
        await Database.DisposeAsync();
    }
}
