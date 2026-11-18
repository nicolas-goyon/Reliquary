using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Platform.Extensions;

namespace Reliquary.Api.Tests.Plugins;

/// <summary>
/// The storage of a plugin for a user. See issue #279.
/// </summary>
public sealed class PluginStorageTests(PluginInstanceFixture fixture) : IClassFixture<PluginInstanceFixture>
{
    private static readonly string[] DecksRead = ["decks:read"];

    private static readonly int[] Sizes = [1, 2];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private PluginInstance Instance => fixture.Instance;

    [Fact]
    public async Task PluginKeepsReadsListsAndDeletesItsValues()
    {
        var (user, id, token) = await SetupAsync();
        using var client = user;

        var set = await SendAsync(user, token, HttpMethod.Put, Path(id, "settings"), new { value = new { theme = "dark", sizes = Sizes } });
        var text = await SendAsync(user, token, HttpMethod.Put, Path(id, "last sync/é"), new { value = "2026-11-18" });
        var replace = await SendAsync(user, token, HttpMethod.Put, Path(id, "settings"), new { value = new { theme = "light" } });
        var keys = await SendAsync(user, token, HttpMethod.Get, $"/api/v1/me/extensions/{id}/storage");
        var value = await SendAsync(user, token, HttpMethod.Get, Path(id, "settings"));
        var remove = await SendAsync(user, token, HttpMethod.Delete, Path(id, "settings"));
        var removeAgain = await SendAsync(user, token, HttpMethod.Delete, Path(id, "settings"));
        var missing = await SendAsync(user, token, HttpMethod.Get, Path(id, "settings"));

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, text.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replace.StatusCode);
        Assert.Equal(["last sync/é", "settings"], (await JsonAsync(keys)).GetProperty("keys").EnumerateArray().Select(key => key.GetString()));
        Assert.Equal("light", (await JsonAsync(value)).GetProperty("value").GetProperty("theme").GetString());
        Assert.Equal("no-store", value.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removeAgain.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task StorageOfOnePluginAndOneUserHasALimit()
    {
        var (user, id, token) = await SetupAsync();
        using var client = user;
        var half = new string('a', (PluginStorageEndpoints.MaxBytes / 2) - 100);

        var first = await SendAsync(user, token, HttpMethod.Put, Path(id, "a"), new { value = half });
        var second = await SendAsync(user, token, HttpMethod.Put, Path(id, "b"), new { value = half });
        var third = await SendAsync(user, token, HttpMethod.Put, Path(id, "c"), new { value = half });
        var replace = await SendAsync(user, token, HttpMethod.Put, Path(id, "a"), new { value = "small" });
        var afterReplace = await SendAsync(user, token, HttpMethod.Put, Path(id, "c"), new { value = half });
        var keys = await SendAsync(user, token, HttpMethod.Get, $"/api/v1/me/extensions/{id}/storage");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, third.StatusCode);
        Assert.Equal(PluginStorageEndpoints.StorageFull, (await JsonAsync(third)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NoContent, replace.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, afterReplace.StatusCode);
        Assert.Equal(["a", "b", "c"], (await JsonAsync(keys)).GetProperty("keys").EnumerateArray().Select(key => key.GetString()));
    }

    [Fact]
    public async Task OnlyThePluginTokenOfThePluginUsesTheStorage()
    {
        var (user, id, token) = await SetupAsync();
        using var client = user;
        var (otherUser, otherId, otherToken) = await SetupAsync();
        using var otherClient = otherUser;
        await PluginInstance.ActivateAsync(user, otherId);
        var tokenOfOtherPlugin = await PluginRevocationTests.PluginTokenAsync(user, otherId);
        (await SendAsync(user, token, HttpMethod.Put, Path(id, "note"), new { value = "private" })).EnsureSuccessStatusCode();

        var session = await user.GetAsync(Path(id, "note"), Cancel);
        var otherPlugin = await SendAsync(user, tokenOfOtherPlugin, HttpMethod.Get, Path(id, "note"));
        var otherUserSamePlugin = await SendAsync(otherUser, otherToken, HttpMethod.Get, Path(otherId, "note"));
        var noValue = await SendAsync(user, token, HttpMethod.Put, Path(id, "note"), new { });
        var noKey = await SendAsync(user, token, HttpMethod.Put, $"/api/v1/me/extensions/{id}/storage", new { value = 1 });
        var longKey = await SendAsync(user, token, HttpMethod.Put, Path(id, new string('k', PluginStorageEndpoints.MaxKeyLength + 1)), new { value = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, session.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherPlugin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherUserSamePlugin.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noValue.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longKey.StatusCode);
    }

    private static string Path(string id, string key) => $"/api/v1/me/extensions/{id}/storage?key={Uri.EscapeDataString(key)}";

    private static Task<HttpResponseMessage> SendAsync(HttpClient user, string token, HttpMethod method, string path, object? body = null) =>
        PluginRevocationTests.WithToken(user, path, token, method, body);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private async Task<(HttpClient User, string Id, string Token)> SetupAsync()
    {
        var id = PluginInstance.NewId();
        await Instance.InstallAsync(PluginInstance.Manifest(("id", id), ("kind", "plugin"), ("permissions", DecksRead)));
        var user = await Instance.SignInAsync($"{id}@example.org");
        await PluginInstance.ActivateAsync(user, id);
        return (user, id, await PluginRevocationTests.PluginTokenAsync(user, id));
    }
}
