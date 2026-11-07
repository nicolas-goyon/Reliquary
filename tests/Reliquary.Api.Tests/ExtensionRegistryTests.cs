using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Platform.Extensions;
using Reliquary.Platform.Migrations;
using Reliquary.Testing;

namespace Reliquary.Api.Tests;

public sealed class ExtensionRegistryTests
{
    private static readonly object Manifest = new
    {
        id = "moxfield-sync",
        kind = "plugin",
        name = "Moxfield sync",
        version = "0.1.0",
        coreApi = "^1.0",
        permissions = new[] { "decks:read", "decks:write" },
        slots = new[] { "deck.actions", "account.linked" },
        network = new[] { "https://api.moxfield.com" },
        entry = "dist/index.js",
    };

    private static readonly string[] BadSlots = ["Deck Actions"];

    [Fact]
    public async Task AdministratorInstallsAndEnablesAnExtension()
    {
        await using var instance = await Instance.CreateAsync();
        using var admin = await instance.SignInAsync("admin@example.org");
        using var user = await instance.SignInAsync("user@example.org");

        var install = await admin.PutAsJsonAsync("/api/v1/admin/extensions/moxfield-sync", Manifest, TestContext.Current.CancellationToken);
        var beforeEnable = await ListAsync(user);
        var enable = await admin.PatchAsJsonAsync("/api/v1/admin/extensions/moxfield-sync", new { enabled = true, settings = new { relay = true } }, TestContext.Current.CancellationToken);
        var afterEnable = await ListAsync(user);
        var activate = await user.PutAsJsonAsync("/api/v1/me/extensions/moxfield-sync", new { enabled = true }, TestContext.Current.CancellationToken);
        var afterActivate = await ListAsync(user);
        var anonymous = await ListAsync(instance.Factory.CreateClient());

        Assert.Equal(HttpStatusCode.Created, install.StatusCode);
        Assert.Empty(beforeEnable.Items);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var listed = Assert.Single(afterEnable.Items);
        Assert.Equal("1.1.0", afterEnable.CoreApi);
        Assert.Equal(["decks:read", "decks:write"], listed.Permissions);
        Assert.False(listed.Active);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
        Assert.True(Assert.Single(afterActivate.Items).Active);
        Assert.False(Assert.Single(anonymous.Items).Active);
    }

    [Fact]
    public async Task ExtensionForANewerCoreIsRefused()
    {
        await using var instance = await Instance.CreateAsync();
        using var admin = await instance.SignInAsync("admin@example.org");

        var response = await admin.PutAsJsonAsync(
            "/api/v1/admin/extensions/future",
            new { id = "future", kind = "module", name = "Future", version = "1.0.0", coreApi = "^2.0" },
            TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ExtensionEndpoints.CoreVersionNotSupported, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IncorrectManifestGivesTheFieldErrors()
    {
        await using var instance = await Instance.CreateAsync();
        using var admin = await instance.SignInAsync("admin@example.org");

        var response = await admin.PutAsJsonAsync(
            "/api/v1/admin/extensions/Bad_Id",
            new { id = "Bad_Id", kind = "theme", version = "1", coreApi = "latest", slots = BadSlots },
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        foreach (var field in new[] { "id", "kind", "name", "version", "coreApi", "slots[0]" })
        {
            Assert.Contains($"\"field\":\"{field}\"", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OnlyAnAdministratorCanManageTheExtensions()
    {
        await using var instance = await Instance.CreateAsync();
        using var user = await instance.SignInAsync("user@example.org");

        var install = await user.PutAsJsonAsync("/api/v1/admin/extensions/moxfield-sync", Manifest, TestContext.Current.CancellationToken);
        var anonymous = await instance.Factory.CreateClient().GetAsync(new Uri("/api/v1/admin/extensions", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, install.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task UninstalledExtensionLeavesTheList()
    {
        await using var instance = await Instance.CreateAsync();
        using var admin = await instance.SignInAsync("admin@example.org");
        await admin.PutAsJsonAsync("/api/v1/admin/extensions/moxfield-sync", Manifest, TestContext.Current.CancellationToken);

        var update = await admin.PutAsJsonAsync("/api/v1/admin/extensions/moxfield-sync", Manifest, TestContext.Current.CancellationToken);
        var delete = await admin.DeleteAsync(new Uri("/api/v1/admin/extensions/moxfield-sync", UriKind.Relative), TestContext.Current.CancellationToken);
        var all = await admin.GetFromJsonAsync<List<AdminExtensionResponse>>(new Uri("/api/v1/admin/extensions", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(all!);
    }

    private static async Task<ExtensionListResponse> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ExtensionListResponse>(new Uri("/api/v1/extensions", UriKind.Relative), TestContext.Current.CancellationToken))!;

    private sealed class Instance : IAsyncDisposable
    {
        private Instance(TestDatabase database, WebApplicationFactory<Program> factory)
        {
            Database = database;
            Factory = factory;
        }

        public TestDatabase Database { get; }

        public WebApplicationFactory<Program> Factory { get; }

        public static async Task<Instance> CreateAsync()
        {
            var database = await TestServers.CreateDatabaseAsync("extensions");
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:Users", database.ConnectionString);
                builder.UseSetting("ConnectionStrings:Redis", TestServers.Redis);
                builder.UseSetting("Auth:SigningKey", "dGVzdC1zaWduaW5nLWtleS1mb3ItdGhlLWV4dGVuc2lvbi10ZXN0cw==");
                builder.UseSetting("Auth:RequireCheckedEmail", "false");
                builder.UseSetting("Auth:RequestsPerMinute", "1000");
                builder.UseSetting("Auth:AdministratorEmails:0", "admin@example.org");
            });
            await factory.Services.GetRequiredService<MigrationRunner>().MigrateAsync(TestContext.Current.CancellationToken);
            return new Instance(database, factory);
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

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
