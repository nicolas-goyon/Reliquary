using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Decks.Api;
using Reliquary.Decks.Versions;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class AssembledDeckTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LocationOfTheAssembledDeckHasTheCardsOfTheAssembledVersion()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Atraxa", new { name = "Sol Ring" }, new { name = "Forest", quantity = 10 }, new { name = "Cultivate", zone = "considering" });

        var assemble = await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/assembled", new { }, Cancel);
        var assembled = (await assemble.Content.ReadFromJsonAsync<AssembledDeckResponse>(Cancel))!;
        var location = await DeckLocationAsync(client, deck.Id);
        var first = await EntriesAsync(client, location.GetProperty("id").GetGuid());

        await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/text", new { text = "Main\n1 Sol Ring\n8 Forest\n1 Counterspell\n" }, Cancel);
        await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/assembled", new { }, Cancel);
        await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}", new { name = "Atraxa v2" }, Cancel);
        var second = await EntriesAsync(client, location.GetProperty("id").GetGuid());
        var renamed = await DeckLocationAsync(client, deck.Id);
        var history = await client.GetFromJsonAsync<DeckHistoryResponse>(new Uri($"/api/v1/decks/{deck.Id}/versions", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.OK, assemble.StatusCode);
        Assert.Equal(history!.Versions[^1].Id, assembled.AssembledVersionId);
        Assert.Equal("deck", location.GetProperty("kind").GetString());
        Assert.Equal([("Forest", 10), ("Sol Ring", 1)], first);
        Assert.Equal([("Counterspell", 1), ("Forest", 8), ("Sol Ring", 1)], second);
        Assert.Equal("Atraxa v2", renamed.GetProperty("name").GetString());
        Assert.True(history.Versions[0].Assembled);
    }

    [Fact]
    public async Task AssembledDeckRefusesTheDeletionUntilTheRelease()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Burn", new { name = "Lightning Bolt", quantity = 4 });
        var rack = await client.PostAsJsonAsync("/api/v1/collection/locations", new { name = "Rack", kind = "rack" }, Cancel);
        var rackId = (await rack.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
        await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/assembled", new { }, Cancel);

        var refused = await client.DeleteAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var release = await client.DeleteAsync(new Uri($"/api/v1/decks/{deck.Id}/assembled?targetLocationId={rackId}", UriKind.Relative), Cancel);
        var inRack = await EntriesAsync(client, rackId);
        var deleted = await client.DeleteAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(AssembledDeckErrorCodes.DeckAssembled, (await refused.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Null((await release.Content.ReadFromJsonAsync<AssembledDeckResponse>(Cancel))!.AssembledVersionId);
        Assert.Equal([("Lightning Bolt", 4)], inRack);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task VariantCannotBeAssembled()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Main", new { name = "Sol Ring" });
        var variant = (await (await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/variants", new { name = "Budget" }, Cancel)).Content.ReadFromJsonAsync<DeckVariantResponse>(Cancel))!;

        var response = await client.PutAsJsonAsync($"/api/v1/decks/{variant.Id}/assembled", new { }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<JsonElement> DeckLocationAsync(HttpClient client, Guid deckId)
    {
        var locations = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/collection/locations", UriKind.Relative), Cancel);
        return locations.EnumerateArray().Single(location => location.TryGetProperty("deckId", out var id) && id.ValueKind == JsonValueKind.String && id.GetGuid() == deckId);
    }

    private static async Task<List<(string Name, int Quantity)>> EntriesAsync(HttpClient client, Guid locationId)
    {
        var page = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/collection/locations/{locationId}/entries?limit=100", UriKind.Relative), Cancel);
        return [.. page.GetProperty("items").EnumerateArray()
            .GroupBy(entry => entry.GetProperty("name").GetString()!)
            .Select(group => (group.Key, group.Sum(entry => entry.GetProperty("quantity").GetInt32())))
            .Order()];
    }

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string name, params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name, lines }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}
