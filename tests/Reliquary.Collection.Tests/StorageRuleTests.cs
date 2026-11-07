using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.Collection.Api;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

/// <summary>
/// The storage rules, the card search behind them, and the tokens of the assembled decks.
/// </summary>
[Collection(CollectionGroup.Name)]
public sealed class StorageRuleTests(CollectionInstance instance)
{
    [Fact]
    public async Task CatalogMatchesTheCardsOfAQuery()
    {
        var catalog = (await instance.GetAsync()).Services.GetRequiredService<ICardCatalog>();
        var counterspell = await instance.OracleIdAsync("Counterspell");
        var forest = await instance.OracleIdAsync("Forest");
        var bolt = await instance.OracleIdAsync("Lightning Bolt");

        var blue = await catalog.MatchAsync("c:u t:instant", [counterspell, forest, bolt], Cancel);
        var none = await catalog.MatchAsync("t:land", [], Cancel);
        var syntax = await Assert.ThrowsAsync<CardQueryException>(() => catalog.MatchAsync("t:(", [forest], Cancel));
        var regex = await Assert.ThrowsAsync<CardQueryException>(() => catalog.MatchAsync("o:/[/", [forest], Cancel));

        Assert.Equal([counterspell], blue);
        Assert.Empty(none);
        Assert.Equal("search_syntax_error", syntax.Code);
        Assert.Equal("search_syntax_error", regex.Code);
    }

    [Fact]
    public async Task FirstMatchingRuleByPrioritySuggestsTheLocation()
    {
        using var client = await instance.NewUserAsync();
        var blueRack = await client.CreateLocationAsync("Blue rack", "rack");
        var lands = await client.CreateLocationAsync("Lands", "box");
        var instants = await client.CreateLocationAsync("Instants", null, blueRack.Id);
        var counterspell = await instance.OracleIdAsync("Counterspell");
        var forest = await instance.OracleIdAsync("Forest");
        var solRing = await instance.OracleIdAsync("Sol Ring");

        var blue = await client.PostAsJsonAsync("/api/v1/collection/storage-rules", new { locationId = blueRack.Id, query = "c:u", priority = 2 }, Cancel);
        await client.PostAsJsonAsync("/api/v1/collection/storage-rules", new { locationId = instants.Id, query = "c:u t:instant", priority = 1 }, Cancel);
        await client.PostAsJsonAsync("/api/v1/collection/storage-rules", new { locationId = lands.Id, query = "t:land" }, Cancel);
        var invalid = await client.PostAsJsonAsync("/api/v1/collection/storage-rules", new { locationId = lands.Id, query = "t:(" }, Cancel);
        var forCounterspell = await client.GetFromJsonAsync<StorageRuleResponse>(new Uri($"/api/v1/collection/storage-rules/suggestion?oracleId={counterspell}", UriKind.Relative), Cancel);
        var forForest = await client.GetFromJsonAsync<StorageRuleResponse>(new Uri($"/api/v1/collection/storage-rules/suggestion?oracleId={forest}", UriKind.Relative), Cancel);
        var forSolRing = await client.GetAsync(new Uri($"/api/v1/collection/storage-rules/suggestion?oracleId={solRing}", UriKind.Relative), Cancel);
        var blueRule = (await blue.Content.ReadFromJsonAsync<StorageRuleResponse>(Cancel))!;
        await client.PatchAsJsonAsync($"/api/v1/collection/storage-rules/{blueRule.Id}", new { priority = 0 }, Cancel);
        var afterChange = await client.GetFromJsonAsync<StorageRuleResponse>(new Uri($"/api/v1/collection/storage-rules/suggestion?oracleId={counterspell}", UriKind.Relative), Cancel);
        var list = await client.GetFromJsonAsync<List<StorageRuleResponse>>(new Uri("/api/v1/collection/storage-rules", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Created, blue.StatusCode);
        Assert.Equal("query", await invalid.FieldAsync());
        Assert.Equal((instants.Id, "Blue rack", "Instants"), (forCounterspell!.LocationId, forCounterspell.LocationName, forCounterspell.SectionName));
        Assert.Equal(lands.Id, forForest!.LocationId);
        Assert.Equal(HttpStatusCode.NoContent, forSolRing.StatusCode);
        Assert.Equal(blueRack.Id, afterChange!.LocationId);
        Assert.Equal(["c:u", "t:land", "c:u t:instant"], list!.Select(rule => rule.Query));
    }

    [Fact]
    public async Task OwnerMarksTheTokensOfAnAssembledDeck()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var decks = (await instance.GetAsync()).Services.GetRequiredService<IAssembledDeckLocations>();
        var goblin = await instance.OracleIdAsync("Goblin");
        var deckId = Guid.CreateVersion7();
        var locationId = await decks.EnsureLocationAsync(ownerId, deckId, "Krenko", Cancel);
        await decks.SetContentAsync(ownerId, deckId, [new(await instance.OracleIdAsync("Krenko, Mob Boss"), null, "en", null, 1), new(goblin, null, "en", null, 1)], Cancel);
        var rack = await client.CreateLocationAsync("Rack", "rack");

        var before = await client.GetFromJsonAsync<List<DeckTokenResponse>>(new Uri($"/api/v1/collection/locations/{locationId}/tokens", UriKind.Relative), Cancel);
        var marked = await client.PutAsJsonAsync($"/api/v1/collection/locations/{locationId}/tokens/{goblin}", new { owned = true }, Cancel);
        await decks.SetContentAsync(ownerId, deckId, [new(goblin, null, "en", null, 1)], Cancel);
        var after = await client.GetFromJsonAsync<List<DeckTokenResponse>>(new Uri($"/api/v1/collection/locations/{locationId}/tokens", UriKind.Relative), Cancel);
        var notDeck = await client.GetAsync(new Uri($"/api/v1/collection/locations/{rack.Id}/tokens", UriKind.Relative), Cancel);

        Assert.Equal(("Goblin", false), (Assert.Single(before!).Name, before![0].Owned));
        Assert.True(Assert.Single((await marked.Content.ReadFromJsonAsync<List<DeckTokenResponse>>(Cancel))!).Owned);
        Assert.True(Assert.Single(after!).Owned);
        Assert.Equal(HttpStatusCode.Conflict, notDeck.StatusCode);
    }
}
