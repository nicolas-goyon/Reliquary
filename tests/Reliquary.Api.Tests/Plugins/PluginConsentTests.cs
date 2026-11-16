using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Platform.Extensions;

namespace Reliquary.Api.Tests.Plugins;

/// <summary>
/// The consent of the user for the access of a plugin, and the pause of a new version that asks for more. See issue #287.
/// </summary>
public sealed class PluginConsentTests(PluginInstanceFixture fixture) : IClassFixture<PluginInstanceFixture>
{
    private static readonly string[] DecksRead = ["decks:read"];

    private static readonly string[] DecksReadWrite = ["decks:read", "decks:write"];

    private static readonly string[] Api = ["https://api.example.com"];

    private static readonly string[] ApiAndImages = ["https://api.example.com", "https://images.example.com"];

    private static readonly string[] Camera = ["camera"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ActivationNeedsTheConsentOfTheUser()
    {
        var instance = fixture.Instance;
        var id = PluginInstance.NewId();
        await instance.InstallAsync(Manifest(id, "1.0.0", DecksRead, Api, Camera));
        using var user = await instance.SignInAsync($"{id}-1@example.org");

        var withoutConsent = await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true }, Cancel);
        var before = await ItemAsync(user, id);
        var withConsent = await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", Consent("1.0.0", DecksRead, Api, Camera), Cancel);
        var after = await ItemAsync(user, id);

        Assert.Equal(HttpStatusCode.Conflict, withoutConsent.StatusCode);
        Assert.Equal(PluginActivationEndpoints.ConsentRequired, await CodeAsync(withoutConsent));
        Assert.False(before.Active);
        Assert.Null(before.Consent);
        Assert.Equal(Api, before.Network);
        Assert.Equal(Camera, before.Devices);
        Assert.Equal(HttpStatusCode.NoContent, withConsent.StatusCode);
        Assert.True(after.Active);
        Assert.False(after.Paused);
        Assert.Equal("1.0.0", after.Consent!.Version);
        Assert.Equal(DecksRead, after.Consent.Scopes);
        Assert.Equal(Api, after.Consent.Network);
        Assert.Equal(Camera, after.Consent.Devices);
        Assert.NotNull(after.Consent.AcceptedAt);
    }

    [Fact]
    public async Task ConsentForAnOtherVersionOrWithLessAccessIsRefused()
    {
        var instance = fixture.Instance;
        var id = PluginInstance.NewId();
        await instance.InstallAsync(Manifest(id, "1.0.0", DecksReadWrite, Api, []));
        using var user = await instance.SignInAsync($"{id}-1@example.org");

        var otherVersion = await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", Consent("0.9.0", DecksReadWrite, Api, []), Cancel);
        var lessScopes = await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", Consent("1.0.0", DecksRead, Api, []), Cancel);
        var noVersion = await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true, consent = new { scopes = DecksReadWrite } }, Cancel);

        Assert.Equal(HttpStatusCode.Conflict, otherVersion.StatusCode);
        Assert.Equal(PluginActivationEndpoints.ConsentOutdated, await CodeAsync(otherVersion));
        Assert.Equal(HttpStatusCode.Conflict, lessScopes.StatusCode);
        Assert.Equal(PluginActivationEndpoints.ConsentOutdated, await CodeAsync(lessScopes));
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        Assert.False((await ItemAsync(user, id)).Active);
    }

    [Fact]
    public async Task NewVersionWithMoreAccessStaysPausedUntilTheUserAccepts()
    {
        var instance = fixture.Instance;
        var id = PluginInstance.NewId();
        await instance.InstallAsync(Manifest(id, "1.0.0", DecksRead, Api, []));
        using var user = await instance.SignInAsync($"{id}-1@example.org");
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", Consent("1.0.0", DecksRead, Api, []), Cancel)).EnsureSuccessStatusCode();
        var firstToken = await user.PostAsync($"/api/v1/me/extensions/{id}/token", null, Cancel);

        await instance.InstallAsync(Manifest(id, "2.0.0", DecksReadWrite, ApiAndImages, []));
        var paused = await ItemAsync(user, id);
        var pausedToken = await user.PostAsync($"/api/v1/me/extensions/{id}/token", null, Cancel);
        var reactivate = await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true }, Cancel);
        var accept = await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", Consent("2.0.0", DecksReadWrite, ApiAndImages, []), Cancel);
        var accepted = await ItemAsync(user, id);
        var newToken = await (await user.PostAsync($"/api/v1/me/extensions/{id}/token", null, Cancel)).Content.ReadFromJsonAsync<JsonElement>(Cancel);

        Assert.Equal(HttpStatusCode.OK, firstToken.StatusCode);
        Assert.True(paused.Paused);
        Assert.False(paused.Active);
        Assert.Equal("1.0.0", paused.Consent!.Version);
        Assert.Equal(DecksRead, paused.Consent.Scopes);
        Assert.Equal(Api, paused.Consent.Network);
        Assert.Equal(HttpStatusCode.NotFound, pausedToken.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reactivate.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        Assert.False(accepted.Paused);
        Assert.True(accepted.Active);
        Assert.Equal(ApiAndImages, accepted.Consent!.Network);
        Assert.Equal(DecksReadWrite, newToken.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
    }

    [Fact]
    public async Task NewVersionWithLessAccessRunsWithTheScopesOfTheNewVersion()
    {
        var instance = fixture.Instance;
        var id = PluginInstance.NewId();
        await instance.InstallAsync(Manifest(id, "1.0.0", DecksReadWrite, ApiAndImages, []));
        using var user = await instance.SignInAsync($"{id}-1@example.org");
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", Consent("1.0.0", DecksReadWrite, ApiAndImages, []), Cancel)).EnsureSuccessStatusCode();

        await instance.InstallAsync(Manifest(id, "1.1.0", DecksRead, Api, []));
        var item = await ItemAsync(user, id);
        var token = await (await user.PostAsync($"/api/v1/me/extensions/{id}/token", null, Cancel)).Content.ReadFromJsonAsync<JsonElement>(Cancel);

        Assert.True(item.Active);
        Assert.False(item.Paused);
        Assert.Equal(DecksRead, token.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
    }

    [Fact]
    public async Task DeactivationKeepsTheConsent()
    {
        var instance = fixture.Instance;
        var id = PluginInstance.NewId();
        await instance.InstallAsync(Manifest(id, "1.0.0", DecksRead, [], []));
        using var user = await instance.SignInAsync($"{id}-1@example.org");
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", Consent("1.0.0", DecksRead, [], []), Cancel)).EnsureSuccessStatusCode();

        var deactivate = await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = false }, Cancel);
        var inactive = await ItemAsync(user, id);
        var activate = await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true }, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.False(inactive.Active);
        Assert.False(inactive.Paused);
        Assert.Equal("1.0.0", inactive.Consent!.Version);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
        Assert.True((await ItemAsync(user, id)).Active);
    }

    [Fact]
    public void GrantGivesTheMissingAccess()
    {
        var manifest = """
            {"permissions":["decks:read","decks:write"],"network":["https://*.example.com"],"devices":["camera"],
             "accounts":[{"site":"example.com","auth":"api-key"},{"site":"example.com","auth":"api-key"}]}
            """;
        var asked = PluginGrant.FromManifest(manifest);
        var accepted = new PluginGrant(["decks:read"], ["https://api.example.com"], ["camera"], []);

        var missing = accepted.Missing(asked);

        Assert.Equal(["example.com"], asked.Sites);
        Assert.Equal(["decks:write"], missing.Scopes);
        Assert.Equal(["https://*.example.com"], missing.Network);
        Assert.Empty(missing.Devices);
        Assert.Equal(["example.com"], missing.Sites);
        Assert.False(accepted.Covers(asked));
        Assert.True(asked.Covers(asked));
        Assert.True(PluginGrant.FromManifest("{}").IsEmpty);
    }

    private static Dictionary<string, object?> Manifest(string id, string version, string[] permissions, string[] network, string[] devices)
    {
        var manifest = PluginInstance.Manifest(("id", id), ("version", version), ("permissions", permissions));
        if (network.Length > 0)
        {
            manifest["network"] = network;
        }

        if (devices.Length > 0)
        {
            manifest["devices"] = devices;
        }

        return manifest;
    }

    private static object Consent(string version, string[] scopes, string[] network, string[] devices) =>
        new { enabled = true, consent = new { version, scopes, network, devices, sites = Array.Empty<string>() } };

    private static async Task<ExtensionResponse> ItemAsync(HttpClient client, string id)
    {
        var list = await client.GetFromJsonAsync<ExtensionListResponse>(new Uri("/api/v1/extensions", UriKind.Relative), Cancel);
        return list!.Items.Single(item => item.Id == id);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString();
}
