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

    private static readonly string[] WideNetwork = ["https://*.com"];

    private static readonly int[] NotAnObject = [1, 2];

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
        Assert.Equal(new PluginRuntimeResponse(8, null, false), afterActivate.Plugins);
    }

    [Fact]
    public async Task UserCannotActivateMorePluginsThanTheLimit()
    {
        await using var instance = await Instance.CreateAsync(new Dictionary<string, string>
        {
            ["Plugins:MaxActivePlugins"] = "1",
            ["Plugins:FrameOrigin"] = "https://plugins.example.org/",
        });
        using var admin = await instance.SignInAsync("admin@example.org");
        using var user = await instance.SignInAsync("user@example.org");
        foreach (var id in new[] { "first", "second" })
        {
            var manifest = new { id, kind = "plugin", name = id, version = "1.0.0", coreApi = "^1.0", entry = "index.js" };
            (await admin.PutAsJsonAsync($"/api/v1/admin/extensions/{id}", manifest, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await admin.PatchAsJsonAsync($"/api/v1/admin/extensions/{id}", new { enabled = true }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        }

        var first = await user.PutAsJsonAsync("/api/v1/me/extensions/first", new { enabled = true }, TestContext.Current.CancellationToken);
        var firstAgain = await user.PutAsJsonAsync("/api/v1/me/extensions/first", new { enabled = true }, TestContext.Current.CancellationToken);
        var second = await user.PutAsJsonAsync("/api/v1/me/extensions/second", new { enabled = true }, TestContext.Current.CancellationToken);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var deactivate = await user.PutAsJsonAsync("/api/v1/me/extensions/first", new { enabled = false }, TestContext.Current.CancellationToken);
        var secondAfterDeactivate = await user.PutAsJsonAsync("/api/v1/me/extensions/second", new { enabled = true }, TestContext.Current.CancellationToken);
        var list = await ListAsync(user);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, firstAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ExtensionEndpoints.TooManyActivePlugins, problem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondAfterDeactivate.StatusCode);
        Assert.Equal(["second"], list.Items.Where(item => item.Active).Select(item => item.Id));
        Assert.Equal(new PluginRuntimeResponse(1, "https://plugins.example.org", false), list.Plugins);
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
    public async Task RegistryKeepsAllTheFieldsOfAPluginManifest()
    {
        await using var instance = await Instance.CreateAsync();
        using var admin = await instance.SignInAsync("admin@example.org");
        var manifest = new
        {
            id = "card-scan",
            kind = "plugin",
            name = "Card scan",
            version = "1.0.0",
            coreApi = "^1.0",
            entry = "dist/index.js",
            slots = new[] { "analysis.tabs" },
            devices = new[] { "camera" },
            panels = new[] { new { id = "stats", slot = "analysis.tabs", title = "Scan statistics" } },
            i18n = new { fr = new { name = "Scan de cartes" } },
        };

        var install = await admin.PutAsJsonAsync("/api/v1/admin/extensions/card-scan", manifest, TestContext.Current.CancellationToken);
        var body = await install.Content.ReadFromJsonAsync<AdminExtensionResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, install.StatusCode);
        Assert.Equal("stats", body!.Manifest.GetProperty("panels")[0].GetProperty("id").GetString());
        Assert.Equal("camera", body.Manifest.GetProperty("devices")[0].GetString());
        Assert.Equal("Scan de cartes", body.Manifest.GetProperty("i18n").GetProperty("fr").GetProperty("name").GetString());
        Assert.Equal([new ExtensionPanelResponse("stats", "analysis.tabs", "Scan statistics")], body.Extension.Panels);
    }

    [Fact]
    public async Task ExtensionListGivesThePanelsAndTheTranslationsOfAPlugin()
    {
        await using var instance = await Instance.CreateAsync();
        using var admin = await instance.SignInAsync("admin@example.org");
        var manifest = new
        {
            id = "card-scan",
            kind = "plugin",
            name = "Card scan",
            version = "1.0.0",
            coreApi = "^1.0",
            entry = "dist/index.js",
            slots = new[] { "card.page" },
            panels = new[] { new { id = "prices", slot = "card.page", title = "Prices" } },
            i18n = new Dictionary<string, Dictionary<string, string>> { ["fr"] = new() { ["panels.prices.title"] = "Prix" } },
        };
        (await admin.PutAsJsonAsync("/api/v1/admin/extensions/card-scan", manifest, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await admin.PatchAsJsonAsync("/api/v1/admin/extensions/card-scan", new { enabled = true }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var item = Assert.Single((await ListAsync(admin)).Items);

        Assert.Equal([new ExtensionPanelResponse("prices", "card.page", "Prices")], item.Panels);
        Assert.Equal("Prix", item.I18n!.Value.GetProperty("fr").GetProperty("panels.prices.title").GetString());
    }

    [Fact]
    public async Task IncorrectPluginManifestGivesTheFieldErrors()
    {
        await using var instance = await Instance.CreateAsync();
        using var admin = await instance.SignInAsync("admin@example.org");

        var response = await admin.PutAsJsonAsync(
            "/api/v1/admin/extensions/card-scan",
            new
            {
                id = "card-scan",
                kind = "plugin",
                name = "Card scan",
                version = "1.0.0",
                coreApi = "^1.0",
                entry = "../index.js",
                network = WideNetwork,
                author = "Somebody",
            },
            TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var fields = problem.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()).ToList();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["author", "entry", "network[0]"], fields.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ManifestMustBeAnObject()
    {
        await using var instance = await Instance.CreateAsync();
        using var admin = await instance.SignInAsync("admin@example.org");

        var response = await admin.PutAsJsonAsync("/api/v1/admin/extensions/card-scan", NotAnObject, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    [Fact]
    public async Task DevelopmentPluginFrameNeedsTheSetting()
    {
        await using var instance = await Instance.CreateAsync();
        using var client = instance.Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/plugins/development/frame?entry=http://localhost:5173/src/main.ts", UriKind.Relative), TestContext.Current.CancellationToken);
        var list = await ListAsync(client);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(list.Plugins.DevelopmentPlugins);
    }

    [Fact]
    public async Task DevelopmentPluginFrameAllowsTheOriginOfThePlugin()
    {
        await using var instance = await Instance.CreateAsync(new Dictionary<string, string> { ["Plugins:AllowDevelopmentPlugins"] = "true" });
        using var client = instance.Factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/v1/plugins/development/frame?entry=http://localhost:5173/src/main.ts&network=https://api.moxfield.com/v2", UriKind.Relative),
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var list = await ListAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "default-src 'none'; script-src http://localhost:5173; style-src http://localhost:5173 'unsafe-inline'; font-src http://localhost:5173; "
            + "img-src http://localhost:5173 https://api.moxfield.com data: blob:; connect-src http://localhost:5173 ws://localhost:5173 https://api.moxfield.com; "
            + "worker-src blob:; base-uri 'none'; form-action 'none'",
            string.Join(";", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Contains("<script type=\"module\" src=\"http://localhost:5173/src/main.ts\"></script>", html, StringComparison.Ordinal);
        Assert.True(list.Plugins.DevelopmentPlugins);
    }

    [Theory]
    [InlineData("entry=javascript:alert(1)")]
    [InlineData("entry=/src/main.ts")]
    [InlineData("entry=http://user:secret@localhost:5173/main.ts")]
    [InlineData("entry=http://localhost:5173/main.ts&network=ftp://example.org")]
    [InlineData("entry=http://localhost:5173/main.ts&network=https://a.example.org%20%27unsafe-eval%27")]
    public async Task DevelopmentPluginFrameRefusesAnIncorrectAddress(string query)
    {
        await using var instance = await Instance.CreateAsync(new Dictionary<string, string> { ["Plugins:AllowDevelopmentPlugins"] = "true" });
        using var client = instance.Factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/api/v1/plugins/development/frame?{query}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

        public static async Task<Instance> CreateAsync(IReadOnlyDictionary<string, string>? settings = null)
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
                foreach (var (key, value) in settings ?? new Dictionary<string, string>())
                {
                    builder.UseSetting(key, value);
                }
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
