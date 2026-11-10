using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Platform.Extensions;
using Reliquary.Platform.Migrations;
using Reliquary.Testing;

namespace Reliquary.Api.Tests.Plugins;

/// <summary>
/// An API instance with its own database for the plugin tests, and an administrator account.
/// </summary>
internal sealed class PluginInstance : IAsyncDisposable
{
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

    public static async Task<PluginInstance> CreateAsync(params (string Key, string Value)[] settings)
    {
        var database = await TestServers.CreateDatabaseAsync("plugins");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Users", database.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", TestServers.Redis);
            builder.UseSetting("Auth:SigningKey", "dGVzdC1zaWduaW5nLWtleS1mb3ItdGhlLXBsdWdpbi10ZXN0cw==");
            builder.UseSetting("Auth:RequireCheckedEmail", "false");
            builder.UseSetting("Auth:RequestsPerMinute", "1000");
            builder.UseSetting("Auth:AdministratorEmails:0", AdministratorEmail);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
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
        var credentials = new { email, password = "correct horse battery", displayName = email.Split('@')[0], language = "en" };
        (await client.PostAsJsonAsync("/api/v1/auth/register", credentials, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/sign-in", credentials, TestContext.Current.CancellationToken);
        var session = await signIn.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>
    /// Installs and enables a plugin with the administrator account, and keeps its files.
    /// </summary>
    /// <returns>The client of the administrator.</returns>
    public async Task<HttpClient> InstallAsync(IReadOnlyDictionary<string, object?> manifest, params PluginFile[] files)
    {
        var admin = await SignInAsync(AdministratorEmail);
        var id = (string)manifest["id"]!;
        (await admin.PutAsJsonAsync($"/api/v1/admin/extensions/{id}", manifest, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await admin.PatchAsJsonAsync($"/api/v1/admin/extensions/{id}", new { enabled = true }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var stored = files.Length > 0 ? files : [new PluginFile((string)manifest["entry"]!, EntryContent)];
        await Files.SaveVersionAsync(id, (string)manifest["version"]!, stored, TestContext.Current.CancellationToken);
        return admin;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Database.DisposeAsync();
    }
}
