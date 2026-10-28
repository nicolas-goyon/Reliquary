using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Accounts.ServiceAccounts;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Tests;

/// <summary>
/// Checks the scopes of the delegation tokens on the deck endpoints. See docs/architecture/service-accounts.md.
/// </summary>
[Collection(DecksGroup.Name)]
public sealed class DelegationScopeTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadScopeReadsTheDecksAndCannotChangeThem()
    {
        using var user = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(user, "Private deck");
        using var module = await DelegatedClientAsync(user, "decks:read");

        var list = await module.GetAsync(new Uri("/api/v1/decks", UriKind.Relative), Cancel);
        var read = await module.GetAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var folders = await module.GetAsync(new Uri("/api/v1/deck-folders", UriKind.Relative), Cancel);
        var create = await module.PostAsJsonAsync("/api/v1/decks", new { name = "By the module" }, Cancel);
        var delete = await module.DeleteAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var favorites = await module.GetAsync(new Uri("/api/v1/public-decks/favorites", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(deck.Id.ToString(), await list.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, folders.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("scope_missing", await CodeAsync(create));
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, favorites.StatusCode);
    }

    [Fact]
    public async Task WriteScopeChangesTheDecksOfTheUser()
    {
        using var user = await instance.NewUserAsync();
        using var module = await DelegatedClientAsync(user, "decks:read", "decks:write");

        var create = await module.PostAsJsonAsync("/api/v1/decks", new { name = "By the module", lines = new[] { new { name = "Sol Ring" } } }, Cancel);
        var deck = (await create.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
        var mine = await user.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var profile = await module.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("By the module", mine.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, profile.StatusCode);
    }

    /// <summary>
    /// Creates a service account, a delegation of the user with the scopes, and a client with a delegation token.
    /// </summary>
    private async Task<HttpClient> DelegatedClientAsync(HttpClient user, params string[] scopes)
    {
        var api = await instance.GetAsync();
        var name = $"module-{Guid.NewGuid():N}"[..20];
        var created = (await api.Services.GetRequiredService<ServiceAccountService>().CreateAsync(name, "A test module.", Cancel)).Created!;
        var delegation = await user.PostAsJsonAsync("/api/v1/me/delegations", new { serviceAccountId = created.ServiceAccount.Id, scopes }, Cancel);
        Assert.Equal(HttpStatusCode.Created, delegation.StatusCode);
        var delegationId = (await delegation.Content.ReadFromJsonAsync<DelegationResponse>(Cancel))!.Id;

        using var serviceAccount = api.CreateClient();
        serviceAccount.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Secret);
        var token = await serviceAccount.PostAsJsonAsync($"/internal/v1/delegations/{delegationId}/tokens", new { }, Cancel);
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);

        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await token.Content.ReadFromJsonAsync<DelegationTokenResponse>(Cancel))!.Token);
        return client;
    }

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString();
}
