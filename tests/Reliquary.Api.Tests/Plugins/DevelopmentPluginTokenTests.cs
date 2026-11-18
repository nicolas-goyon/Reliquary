using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Reliquary.Api.Tests.Plugins;

/// <summary>
/// The plugin token of a development plugin. The instance has no manifest of a development plugin, so the web app gives the scopes of the manifest.
/// </summary>
public sealed class DevelopmentPluginTokenTests
{
    private const string TokenPath = "/api/v1/me/extensions/dev:deck-stats/token";

    private static readonly string[] DecksRead = ["decks:read"];

    private static readonly string[] DecksReadAndUnknown = ["decks:read", "unknown:scope"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DevelopmentInstanceGivesATokenWithTheScopesOfTheRequest()
    {
        await using var instance = await PluginInstance.CreateAsync(("Plugins:AllowDevelopmentPlugins", "true"));
        using var user = await instance.SignInAsync("developer@example.org");

        var created = await user.PostAsJsonAsync(TokenPath, new { scopes = DecksReadAndUnknown }, Cancel);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var token = body.GetProperty("token").GetString()!;

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("dev:deck-stats", body.GetProperty("pluginId").GetString());
        Assert.Equal(DecksRead, body.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await PluginRevocationTests.WithToken(user, "/api/v1/decks", token)).StatusCode);
        var write = await PluginRevocationTests.WithToken(user, "/api/v1/decks", token, HttpMethod.Post, new { name = "Plugin deck" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal("scope_missing", (await write.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsync("/api/v1/me/extensions/dev:/token", null, Cancel)).StatusCode);
    }

    [Fact]
    public async Task TokenWithoutScopesInTheRequestOfADevelopmentPluginHasNoScope()
    {
        await using var instance = await PluginInstance.CreateAsync(("Plugins:AllowDevelopmentPlugins", "true"));
        using var user = await instance.SignInAsync("developer@example.org");

        var created = await user.PostAsJsonAsync(TokenPath, new { scopes = Array.Empty<string>() }, Cancel);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Cancel);

        Assert.Empty(body.GetProperty("scopes").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await PluginRevocationTests.WithToken(user, "/api/v1/decks", body.GetProperty("token").GetString()!)).StatusCode);
    }

    [Fact]
    public async Task OtherInstanceGivesNoTokenToADevelopmentPlugin()
    {
        await using var instance = await PluginInstance.CreateAsync(("Plugins:AllowDevelopmentPlugins", "false"));
        using var user = await instance.SignInAsync("developer@example.org");

        var created = await user.PostAsJsonAsync(TokenPath, new { scopes = DecksRead }, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
    }
}
