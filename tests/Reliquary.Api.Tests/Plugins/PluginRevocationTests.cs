using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Platform.Extensions.Catalog;
using Reliquary.Platform.Tests.Extensions.Catalog;

namespace Reliquary.Api.Tests.Plugins;

/// <summary>
/// The revocation of a plugin version by the plugin catalog. See docs/decisions/0052-plugin-catalog-signature-and-revocation.md.
/// </summary>
public sealed class PluginRevocationTests
{
    private static readonly string[] DecksRead = ["decks:read"];

    private static readonly string[] ExampleNetwork = ["https://api.example.com"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RevokedVersionStopsAfterOneSync()
    {
        using var catalog = new TestCatalog();
        var id = PluginInstance.NewId();
        var files = catalog.AddVersion(id, "1.0.0", ("permissions", DecksRead), ("network", ExampleNetwork));
        await using var instance = await CreateInstanceAsync(catalog);
        var sync = instance.Factory.Services.GetRequiredService<PluginCatalogSync>();
        await sync.SyncAsync(Cancel);

        var admin = await instance.AdministratorAsync();
        var manifest = JsonDocument.Parse(files[CatalogIndex.ManifestPath]).RootElement;
        (await admin.PutAsJsonAsync($"/api/v1/admin/extensions/{id}", manifest, Cancel)).EnsureSuccessStatusCode();
        (await admin.PatchAsJsonAsync($"/api/v1/admin/extensions/{id}", new { enabled = true }, Cancel)).EnsureSuccessStatusCode();
        var user = await instance.SignInAsync($"{id}@example.org");
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true }, Cancel)).EnsureSuccessStatusCode();
        var token = await PluginTokenAsync(user, id);
        var frame = $"/api/v1/plugins/{id}/1.0.0/frame";
        var file = $"/api/v1/plugins/{id}/1.0.0/files/dist/index.js";

        // Before the revocation, the plugin runs.
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync(frame, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync(file, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WithToken(user, "/api/v1/decks", token)).StatusCode);
        Assert.Contains(id, await ActivePluginIdsAsync(user));

        catalog.Serial = 2;
        catalog.Revoked.Add(new CatalogRevocation(id, "1.0.0", "The version sends the deck lists to an unknown site."));
        var result = await sync.SyncAsync(Cancel);

        // After one sync, the plugin does not run.
        Assert.Equal(id, Assert.Single(result.NewRevocations).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync(frame, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync(file, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(user, "/api/v1/decks", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsync($"/api/v1/me/extensions/{id}/token", null, Cancel)).StatusCode);
        Assert.DoesNotContain(id, await ExtensionIdsAsync(user));
        Assert.Equal(HttpStatusCode.NotFound, (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true }, Cancel)).StatusCode);

        // The administrator sees the reason and cannot enable the version again.
        var installed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/extensions", Cancel);
        var row = installed.EnumerateArray().Single(item => item.GetProperty("extension").GetProperty("id").GetString() == id);
        Assert.False(row.GetProperty("extension").GetProperty("enabled").GetBoolean());
        Assert.Equal("The version sends the deck lists to an unknown site.", row.GetProperty("revocationReason").GetString());
        await AssertRevokedAsync(await admin.PatchAsJsonAsync($"/api/v1/admin/extensions/{id}", new { enabled = true }, Cancel));
        await AssertRevokedAsync(await admin.PutAsJsonAsync($"/api/v1/admin/extensions/{id}", manifest, Cancel));
    }

    [Fact]
    public async Task RevocationStaysWhenALaterIndexDoesNotListIt()
    {
        using var catalog = new TestCatalog();
        var id = PluginInstance.NewId();
        catalog.AddVersion(id, "1.0.0");
        catalog.Revoked.Add(new CatalogRevocation(id, "1.0.0", "Test revocation."));
        await using var instance = await CreateInstanceAsync(catalog);
        var sync = instance.Factory.Services.GetRequiredService<PluginCatalogSync>();
        await sync.SyncAsync(Cancel);

        catalog.Serial = 2;
        catalog.Revoked.Clear();
        var result = await sync.SyncAsync(Cancel);

        Assert.Empty(result.NewRevocations);
        var admin = await instance.AdministratorAsync();
        await AssertRevokedAsync(await admin.PutAsJsonAsync($"/api/v1/admin/extensions/{id}", PluginInstance.Manifest(("id", id)), Cancel));
    }

    /// <summary>
    /// Creates an API instance that syncs the test catalog. The catalog sends the files of its current state at each request.
    /// </summary>
    internal static Task<PluginInstance> CreateInstanceAsync(TestCatalog catalog)
    {
        var handler = new TestCatalogHandler(() => catalog.Files());
        var key = catalog.PublicKey;
        return PluginInstance.CreateAsync(
            services => services.AddHttpClient(PluginCatalogSync.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler),
            ("Plugins:Catalog:Url", TestCatalog.IndexUrl),
            ("Plugins:Catalog:Keys:0:Id", key.Id),
            ("Plugins:Catalog:Keys:0:PublicKey", key.PublicKey));
    }

    internal static async Task<string> PluginTokenAsync(HttpClient user, string id)
    {
        var response = await user.PostAsync($"/api/v1/me/extensions/{id}/token", null, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("token").GetString()!;
    }

    internal static async Task<HttpResponseMessage> WithToken(HttpClient user, string path, string token, HttpMethod? method = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await user.SendAsync(request, Cancel);
    }

    private static async Task<List<string>> ExtensionIdsAsync(HttpClient user) =>
        (await user.GetFromJsonAsync<JsonElement>("/api/v1/extensions", Cancel)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToList();

    private static async Task<List<string>> ActivePluginIdsAsync(HttpClient user) =>
        (await user.GetFromJsonAsync<JsonElement>("/api/v1/extensions", Cancel)).GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("active").GetBoolean())
            .Select(item => item.GetProperty("id").GetString()!)
            .ToList();

    private static async Task AssertRevokedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("version_revoked", problem.GetProperty("code").GetString());
    }
}
