using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Reliquary.Api.Tests.Plugins;
using Reliquary.Platform.LinkedAccounts;

namespace Reliquary.Api.Tests.LinkedAccounts;

/// <summary>
/// One API instance with a vault key for the tests of the linked accounts.
/// </summary>
public sealed class LinkedAccountInstanceFixture : IAsyncLifetime
{
    /// <summary>The key of the vault of the instance, with the ID <c>2026-01</c>.</summary>
    public static readonly string FirstKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(Vault.KeySize));

    private PluginInstance? instance;

    internal PluginInstance Instance => instance ?? throw new InvalidOperationException("The fixture is not ready.");

    public async ValueTask InitializeAsync() => instance = await PluginInstance.CreateAsync(("Plugins:VaultKeys:2026-01", FirstKey));

    public async ValueTask DisposeAsync()
    {
        if (instance is not null)
        {
            await instance.DisposeAsync();
        }
    }
}

/// <summary>
/// The linked accounts and the vault. See docs/decisions/0051-linked-accounts.md.
/// </summary>
public sealed class LinkedAccountTests(LinkedAccountInstanceFixture fixture) : IClassFixture<LinkedAccountInstanceFixture>
{
    private const string ApiSite = "api.example.com";

    private const string NameSite = "example.org";

    private static readonly object[] Accounts =
    [
        new { site = ApiSite, auth = "api-key", sync = new { mode = "periodic", intervalMinutes = 30 } },
        new { site = NameSite, auth = "public-username" },
        new { site = "oauth.example.net", auth = "oauth2-pkce" },
    ];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private PluginInstance Instance => fixture.Instance;

    [Fact]
    public async Task ListShowsTheSitesOfThePluginAndNeverTheSecret()
    {
        var (user, id) = await SetupAsync();

        var linked = await user.PutAsJsonAsync(Link(id, ApiSite), new { secret = " secret-key-1 ", userName = "Jace" }, Cancel);
        var list = await ListAsync(user);

        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.DoesNotContain("secret-key-1", await linked.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key-1", list.GetRawText(), StringComparison.Ordinal);
        var items = list.GetProperty("items").EnumerateArray().Where(item => item.GetProperty("pluginId").GetString() == id).ToList();
        Assert.Equal([ApiSite, NameSite, "oauth.example.net"], items.Select(item => item.GetProperty("site").GetString()));
        var api = items[0];
        Assert.True(api.GetProperty("pluginActive").GetBoolean());
        Assert.Equal("periodic", api.GetProperty("sync").GetProperty("mode").GetString());
        Assert.Equal(30, api.GetProperty("sync").GetProperty("intervalMinutes").GetInt32());
        Assert.Equal("Jace", api.GetProperty("link").GetProperty("userName").GetString());
        Assert.True(api.GetProperty("link").GetProperty("hasSecret").GetBoolean());
        Assert.True(api.GetProperty("link").GetProperty("secretReadable").GetBoolean());
        Assert.Equal("linked", api.GetProperty("link").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("link").ValueKind);
    }

    [Fact]
    public async Task OnlyThePluginTokenOfThePluginReadsTheSecret()
    {
        var (user, id) = await SetupAsync();
        var other = PluginInstance.NewId();
        await Instance.InstallAsync(PluginInstance.Manifest(("id", other), ("kind", "plugin"), ("accounts", Accounts)));
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{other}", new { enabled = true }, Cancel)).EnsureSuccessStatusCode();
        (await user.PutAsJsonAsync(Link(id, ApiSite), new { secret = "secret-key-1" }, Cancel)).EnsureSuccessStatusCode();
        var key = (await (await user.PostAsJsonAsync("/api/v1/me/api-keys", new { name = "Script" }, Cancel)).Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("key").GetString()!;

        var withPlugin = await SendAsync(user, Secret(id, ApiSite), await PluginTokenAsync(user, id));
        var withOtherPlugin = await SendAsync(user, Secret(id, ApiSite), await PluginTokenAsync(user, other));
        var withSession = await user.GetAsync(Secret(id, ApiSite), Cancel);
        var withApiKey = await SendAsync(user, Secret(id, ApiSite), key);
        using var anonymous = Instance.Factory.CreateClient();
        var withoutCredential = await anonymous.GetAsync(Secret(id, ApiSite), Cancel);

        Assert.Equal(HttpStatusCode.OK, withPlugin.StatusCode);
        var body = await withPlugin.Content.ReadFromJsonAsync<LinkedAccountSecretResponse>(Cancel);
        Assert.Equal(new LinkedAccountSecretResponse(ApiSite, "api-key", null, "secret-key-1"), body);
        Assert.Equal("no-store", withPlugin.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, withOtherPlugin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withApiKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutCredential.StatusCode);

        // The plugin token of the plugin does not give the endpoints of the web session.
        var token = await PluginTokenAsync(user, id);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(user, "/api/v1/me/linked-accounts", token)).StatusCode);
        using var unlink = new HttpRequestMessage(HttpMethod.Delete, Link(id, ApiSite));
        unlink.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.SendAsync(unlink, Cancel)).StatusCode);
    }

    [Fact]
    public async Task PublicUserNameHasNoSecret()
    {
        var (user, id) = await SetupAsync();

        var missing = await user.PutAsJsonAsync(Link(id, NameSite), new { userName = "  " }, Cancel);
        var withSecret = await user.PutAsJsonAsync(Link(id, NameSite), new { userName = "Jace", secret = "x" }, Cancel);
        var linked = await user.PutAsJsonAsync(Link(id, NameSite), new { userName = "Jace" }, Cancel);
        var secret = await SendAsync(user, Secret(id, NameSite), await PluginTokenAsync(user, id));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withSecret.StatusCode);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal(new LinkedAccountSecretResponse(NameSite, "public-username", "Jace", null), await secret.Content.ReadFromJsonAsync<LinkedAccountSecretResponse>(Cancel));
        Assert.Equal(0, await CountSecretsAsync(id));
    }

    [Fact]
    public async Task LinkNeedsASiteOfAPluginThatRuns()
    {
        var (user, id) = await SetupAsync();

        var unknownSite = await user.PutAsJsonAsync(Link(id, "unknown.example.com"), new { secret = "x" }, Cancel);
        var oauth = await user.PutAsJsonAsync(Link(id, "oauth.example.net"), new { secret = "x" }, Cancel);
        var noKey = await user.PutAsJsonAsync(Link(id, ApiSite), new { userName = "Jace" }, Cancel);
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = false }, Cancel)).EnsureSuccessStatusCode();
        var inactive = await user.PutAsJsonAsync(Link(id, ApiSite), new { secret = "x" }, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, unknownSite.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, oauth.StatusCode);
        Assert.Equal("linked_account_method_not_supported", await CodeAsync(oauth));
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
    }

    [Fact]
    public async Task UnlinkDeletesTheSecret()
    {
        var (user, id) = await SetupAsync();
        (await user.PutAsJsonAsync(Link(id, ApiSite), new { secret = "secret-key-1" }, Cancel)).EnsureSuccessStatusCode();

        var unlinked = await user.DeleteAsync(Link(id, ApiSite), Cancel);
        var again = await user.DeleteAsync(Link(id, ApiSite), Cancel);
        var secret = await SendAsync(user, Secret(id, ApiSite), await PluginTokenAsync(user, id));

        Assert.Equal(HttpStatusCode.NoContent, unlinked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, secret.StatusCode);
        Assert.Equal(0, await CountSecretsAsync(id));
    }

    [Fact]
    public async Task LinkOfAPluginThatDoesNotRunStaysInTheList()
    {
        var (user, id) = await SetupAsync();
        (await user.PutAsJsonAsync(Link(id, ApiSite), new { secret = "secret-key-1" }, Cancel)).EnsureSuccessStatusCode();

        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = false }, Cancel)).EnsureSuccessStatusCode();
        var items = (await ListAsync(user)).GetProperty("items").EnumerateArray().Where(item => item.GetProperty("pluginId").GetString() == id).ToList();
        var unlinked = await user.DeleteAsync(Link(id, ApiSite), Cancel);

        var item = Assert.Single(items);
        Assert.False(item.GetProperty("pluginActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("sync").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, unlinked.StatusCode);
    }

    [Fact]
    public async Task KeyRotationKeepsTheOldSecretsReadable()
    {
        var (user, id) = await SetupAsync();
        (await user.PutAsJsonAsync(Link(id, ApiSite), new { secret = "secret-key-1" }, Cancel)).EnsureSuccessStatusCode();
        Assert.Equal("2026-01", await KeyIdAsync(id));

        // The operator adds a new key and keeps the old key.
        var newKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(Vault.KeySize));
        await using var rotated = Instance.Factory.WithWebHostBuilder(builder => builder.UseSetting("Plugins:VaultKeys:2026-02", newKey));
        using var client = rotated.CreateClient();
        client.DefaultRequestHeaders.Authorization = user.DefaultRequestHeaders.Authorization;

        var secret = await SendAsync(client, Secret(id, ApiSite), await PluginTokenAsync(client, id));

        Assert.Equal(HttpStatusCode.OK, secret.StatusCode);
        Assert.Equal("secret-key-1", (await secret.Content.ReadFromJsonAsync<LinkedAccountSecretResponse>(Cancel))!.Secret);

        // The read encrypted the secret again with the new key, so the old key is not necessary any more.
        Assert.Equal("2026-02", await KeyIdAsync(id));
        var again = await SendAsync(client, Secret(id, ApiSite), await PluginTokenAsync(client, id));
        Assert.Equal("secret-key-1", (await again.Content.ReadFromJsonAsync<LinkedAccountSecretResponse>(Cancel))!.Secret);

        // A new link uses the new key.
        (await client.PutAsJsonAsync(Link(id, ApiSite), new { secret = "secret-key-2" }, Cancel)).EnsureSuccessStatusCode();
        Assert.Equal("2026-02", await KeyIdAsync(id));
    }

    [Fact]
    public async Task MissingKeyAsksForANewLink()
    {
        var (user, id) = await SetupAsync();
        (await user.PutAsJsonAsync(Link(id, ApiSite), new { secret = "secret-key-1" }, Cancel)).EnsureSuccessStatusCode();

        // The instance lost the key of the secret.
        await using (var connection = new NpgsqlConnection(Instance.Database.ConnectionString))
        {
            await connection.ExecuteAsync("update content.account_links set key_id = 'lost-key' where extension_id = @id", new { id });
        }

        var secret = await SendAsync(user, Secret(id, ApiSite), await PluginTokenAsync(user, id));
        var item = (await ListAsync(user)).GetProperty("items").EnumerateArray().First(entry => entry.GetProperty("pluginId").GetString() == id);
        var relinked = await user.PutAsJsonAsync(Link(id, ApiSite), new { secret = "secret-key-2" }, Cancel);
        var after = await SendAsync(user, Secret(id, ApiSite), await PluginTokenAsync(user, id));

        Assert.Equal(HttpStatusCode.Conflict, secret.StatusCode);
        Assert.Equal("vault_key_missing", await CodeAsync(secret));
        Assert.False(item.GetProperty("link").GetProperty("secretReadable").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, relinked.StatusCode);
        Assert.Equal("secret-key-2", (await after.Content.ReadFromJsonAsync<LinkedAccountSecretResponse>(Cancel))!.Secret);
    }

    [Fact]
    public async Task InstanceWithoutVaultKeyRefusesAnApiKey()
    {
        await using var instance = await PluginInstance.CreateAsync();
        var id = PluginInstance.NewId();
        await instance.InstallAsync(PluginInstance.Manifest(("id", id), ("kind", "plugin"), ("accounts", Accounts)));
        using var user = await instance.SignInAsync($"{id}@example.org");
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true }, Cancel)).EnsureSuccessStatusCode();

        var apiKey = await user.PutAsJsonAsync(Link(id, ApiSite), new { secret = "secret-key-1" }, Cancel);
        var userName = await user.PutAsJsonAsync(Link(id, NameSite), new { userName = "Jace" }, Cancel);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, apiKey.StatusCode);
        Assert.Equal("vault_not_configured", await CodeAsync(apiKey));
        Assert.Equal(HttpStatusCode.OK, userName.StatusCode);
    }

    [Fact]
    public void InvalidVaultKeyStopsTheStart()
    {
        using var factory = Instance.Factory.WithWebHostBuilder(builder => builder.UseSetting("Plugins:VaultKeys:2026-03", "c2hvcnQ="));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("vault key", exception.ToString(), StringComparison.Ordinal);
    }

    private static string Link(string id, string site) => $"/api/v1/me/extensions/{id}/accounts/{site}";

    private static string Secret(string id, string site) => $"{Link(id, site)}/secret";

    private async Task<(HttpClient User, string Id)> SetupAsync()
    {
        var id = PluginInstance.NewId();
        await Instance.InstallAsync(PluginInstance.Manifest(("id", id), ("kind", "plugin"), ("accounts", Accounts)));
        var user = await Instance.SignInAsync($"{id}@example.org");
        (await user.PutAsJsonAsync($"/api/v1/me/extensions/{id}", new { enabled = true }, Cancel)).EnsureSuccessStatusCode();
        return (user, id);
    }

    private static async Task<string> PluginTokenAsync(HttpClient user, string id)
    {
        var response = await user.PostAsync($"/api/v1/me/extensions/{id}/token", null, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Cancel);
    }

    private static async Task<JsonElement> ListAsync(HttpClient user)
    {
        var response = await user.GetAsync("/api/v1/me/linked-accounts", Cancel);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString();

    private async Task<string?> KeyIdAsync(string id)
    {
        await using var connection = new NpgsqlConnection(Instance.Database.ConnectionString);
        return await connection.ExecuteScalarAsync<string?>("select key_id from content.account_links where extension_id = @id", new { id });
    }

    private async Task<int> CountSecretsAsync(string id)
    {
        await using var connection = new NpgsqlConnection(Instance.Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("select count(*) from content.account_links where extension_id = @id and secret_ciphertext is not null", new { id });
    }
}
