using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Platform.Extensions;
using Reliquary.Platform.Extensions.Catalog;
using Reliquary.Platform.Tests.Extensions.Catalog;

namespace Reliquary.Api.Tests.Plugins;

/// <summary>
/// The gate test of milestone M4. See the section "M4: Plugins" of docs/roadmap.md.
/// </summary>
/// <remarks>
/// The test covers the parts of the gate that the API controls.
/// The consent of the user gives the scopes of the plugin token. The web app gives the accepted network origins in the query of the frame address.
/// The test checks the CSP header that the browser gets, because the test has no browser that applies the CSP.
/// </remarks>
public sealed class SignedPluginGateTests
{
    private const string AcceptedOrigin = "https://api.example.com";

    private const string RefusedOrigin = "https://tracker.example.net";

    private static readonly string[] Permissions = ["decks:read"];

    private static readonly string[] Slots = ["deck.actions", "analysis.tabs"];

    private static readonly string[] Network = [AcceptedOrigin, RefusedOrigin];

    private static readonly object[] Panels = [new { id = "stats", slot = "analysis.tabs", title = "Statistics" }];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SignedPluginStaysInItsLimits()
    {
        using var catalog = new TestCatalog();
        var id = PluginInstance.NewId();
        var files = catalog.AddVersion(id, "1.0.0", ("permissions", Permissions), ("slots", Slots), ("network", Network), ("panels", Panels));
        await using var instance = await PluginRevocationTests.CreateInstanceAsync(catalog);
        var sync = instance.Factory.Services.GetRequiredService<PluginCatalogSync>();

        // The instance gets the signed plugin from the test catalog, and the administrator installs and enables it.
        Assert.Equal([$"{id} 1.0.0"], (await sync.SyncAsync(Cancel)).AddedVersions);
        var admin = await instance.AdministratorAsync();
        var manifest = JsonDocument.Parse(files[CatalogIndex.ManifestPath]).RootElement;
        (await admin.PutAsJsonAsync($"/api/v1/admin/extensions/{id}", manifest, Cancel)).EnsureSuccessStatusCode();
        (await admin.PatchAsJsonAsync($"/api/v1/admin/extensions/{id}", new { enabled = true }, Cancel)).EnsureSuccessStatusCode();

        // 1. The user accepts the scopes and the domains of the plugin.
        var user = await instance.SignInAsync($"{id}@example.org");
        Assert.Equal(HttpStatusCode.Conflict, (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true }, Cancel)).StatusCode);
        var consent = new { version = "1.0.0", scopes = Permissions, network = Network };
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true, consent }, Cancel)).EnsureSuccessStatusCode();
        var frame = $"/api/v1/plugins/{id}/1.0.0/frame?network={Uri.EscapeDataString(AcceptedOrigin)}";

        // 2. The plugin adds a deck action and an analysis panel, and it reads a deck.
        var extension = (await user.GetFromJsonAsync<JsonElement>("/api/v1/extensions", Cancel)).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id);
        Assert.True(extension.GetProperty("active").GetBoolean());
        Assert.Contains("deck.actions", extension.GetProperty("slots").EnumerateArray().Select(slot => slot.GetString()));
        var panel = Assert.Single(extension.GetProperty("panels").EnumerateArray());
        Assert.Equal(("stats", "analysis.tabs"), (panel.GetProperty("id").GetString(), panel.GetProperty("slot").GetString()));

        var page = await user.GetAsync(frame, Cancel);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains($"integrity=\"{PluginFileStore.Integrity(files["dist/index.js"])}\"", await page.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);

        var deck = await user.PostAsJsonAsync("/api/v1/decks", new { name = "Gate deck" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, deck.StatusCode);
        var deckId = (await deck.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetString();
        var token = await PluginRevocationTests.PluginTokenAsync(user, id);
        var read = await PluginRevocationTests.WithToken(user, $"/api/v1/decks/{deckId}", token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("Gate deck", (await read.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("name").GetString());

        // 3. The API refuses a request of the plugin with a scope that the user did not accept.
        await AssertScopeMissingAsync(await PluginRevocationTests.WithToken(user, "/api/v1/decks", token, HttpMethod.Post, new { name = "Plugin deck" }));
        await AssertScopeMissingAsync(await PluginRevocationTests.WithToken(user, $"/api/v1/decks/{deckId}", token, HttpMethod.Patch, new { name = "Changed" }));
        await AssertScopeMissingAsync(await PluginRevocationTests.WithToken(user, $"/api/v1/collection/locations/{Guid.NewGuid()}/entries", token));

        // 4. The browser refuses a request of the plugin to a domain that the user did not accept.
        // The CSP of the frame contains only the accepted origin, also when the manifest declares more origins.
        var policy = Assert.Single(page.Headers.GetValues("Content-Security-Policy"));
        var connect = Directive(policy, "connect-src");
        Assert.Contains(AcceptedOrigin, connect, StringComparison.Ordinal);
        Assert.DoesNotContain(RefusedOrigin, policy, StringComparison.Ordinal);
        Assert.StartsWith("default-src 'none'", policy, StringComparison.Ordinal);
        var outside = await user.GetAsync($"/api/v1/plugins/{id}/1.0.0/frame?network={Uri.EscapeDataString("https://other.example.org")}", Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);

        // 5. The test catalog revokes the version. After one sync, the plugin does not run.
        catalog.Serial = 2;
        catalog.Revoked.Add(new CatalogRevocation(id, "1.0.0", "Gate test."));
        await sync.SyncAsync(Cancel);

        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync(frame, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/v1/plugins/{id}/1.0.0/files/dist/index.js", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PluginRevocationTests.WithToken(user, $"/api/v1/decks/{deckId}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsync($"/api/v1/me/extensions/{id}/token", null, Cancel)).StatusCode);
        var items = (await user.GetFromJsonAsync<JsonElement>("/api/v1/extensions", Cancel)).GetProperty("items").EnumerateArray();
        Assert.DoesNotContain(id, items.Select(item => item.GetProperty("id").GetString()));
    }

    private static string Directive(string policy, string name) =>
        policy.Split(';', StringSplitOptions.TrimEntries).Single(directive => directive.StartsWith(name + " ", StringComparison.Ordinal));

    private static async Task AssertScopeMissingAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("scope_missing", (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
    }
}
