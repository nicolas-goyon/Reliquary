using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Decks.Api;
using Reliquary.Decks.Purchases;
using Reliquary.Decks.Versions;

namespace Reliquary.Decks.Tests.Purchases;

[Collection(DecksGroup.Name)]
public sealed class TransitionPlanTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>
    /// The reference dataset of the transition plan: each card of the plan is in the correct group with the correct quantity.
    /// </summary>
    [Fact]
    public async Task EachCardOfTheReferenceDatasetIsInTheCorrectGroup()
    {
        using var client = await instance.NewUserAsync();
        var rack = await CreateLocationAsync(client, "Rack");
        var binder = await CreateLocationAsync(client, "Binder");
        var showcase = await CreateLocationAsync(client, "Showcase");
        await AddAsync(client, rack, new { name = "Counterspell" }, new { name = "Forest", quantity = 3 });
        await AddAsync(client, binder, new { name = "Lightning Bolt", quantity = 2 });
        await AddAsync(client, showcase, new { name = "Cultivate" });
        await client.PostAsync(new Uri($"/api/v1/collection/locations/{binder}/lock", UriKind.Relative), null, Cancel);
        await client.PatchAsJsonAsync($"/api/v1/collection/locations/{showcase}", new { pickable = false }, Cancel);
        var other = await CreateDeckAsync(client, "Other", new { name = "Counterspell" });
        await client.PutAsJsonAsync($"/api/v1/decks/{other.Id}/assembled", new { }, Cancel);
        var main = await CreateDeckAsync(client, "Main", new { name = "Sol Ring" }, new { name = "Forest", quantity = 5 });
        await client.PutAsJsonAsync($"/api/v1/decks/{main.Id}/assembled", new { }, Cancel);
        var variant = (await (await client.PostAsJsonAsync($"/api/v1/decks/{main.Id}/variants", new { name = "Spells" }, Cancel)).Content.ReadFromJsonAsync<DeckVariantResponse>(Cancel))!;
        await client.PutAsJsonAsync(
            $"/api/v1/decks/{variant.Id}/text",
            new { text = "Main\n7 Forest\n2 Counterspell\n2 Lightning Bolt\n1 Cultivate\nConsidering\n1 Sol Ring\n" },
            Cancel);

        var response = await client.GetAsync(new Uri($"/api/v1/decks/{variant.Id}/transition-plan", UriKind.Relative), Cancel);
        var plan = (await response.Content.ReadFromJsonAsync<TransitionPlanResponse>(Cancel))!;

        string Name(Guid oracleId) => plan.Cards[oracleId].Name;
        TransitionAdditionResponse Add(string name) => plan.Add.Single(line => Name(line.OracleId) == name);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(main.Id, plan.AssembledDeckId);
        Assert.Equal([("Sol Ring", 1)], plan.Remove.Select(line => (Name(line.OracleId), line.Quantity)));
        Assert.Equal(["Counterspell", "Cultivate", "Forest", "Lightning Bolt"], plan.Add.Select(line => Name(line.OracleId)).Order());
        Assert.Equal((2, "Rack", 0), (Add("Forest").Quantity, Add("Forest").Take.Single().LocationName, Add("Forest").ToBuy));
        Assert.Equal((2, 1, 1), (Add("Counterspell").Quantity, Add("Counterspell").Take.Single().Quantity, Add("Counterspell").ToBuy));
        Assert.Equal(("Other", "unavailable"), (Add("Counterspell").Closest.Single().LocationName, Add("Counterspell").Closest.Single().Reasons.Single()));
        Assert.Equal((2, true), (Add("Lightning Bolt").Take.Single().Quantity, Add("Lightning Bolt").Take.Single().Locked));
        Assert.Equal((0, 1), (Add("Cultivate").Take.Count, Add("Cultivate").ToBuy));
        Assert.Equal("unavailable", Add("Cultivate").Closest.Single().Reasons.Single());
        Assert.Equal(["Binder"], plan.UnlockFirst.Select(location => location.LocationName));
        Assert.Equal(2, plan.TotalToBuy);
    }

    [Fact]
    public async Task PlanNeedsAnAssembledDeck()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Loose", new { name = "Sol Ring" });

        var response = await client.GetAsync(new Uri($"/api/v1/decks/{deck.Id}/transition-plan", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AssembledDeckErrorCodes.DeckNotAssembled, (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PlanOfTheMainDeckGoesToItsWorkingList()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Main", new { name = "Sol Ring" }, new { name = "Forest", quantity = 2 });
        await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/assembled", new { }, Cancel);
        await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/text", new { text = "Main\n3 Forest\n" }, Cancel);

        var plan = await client.GetFromJsonAsync<TransitionPlanResponse>(new Uri($"/api/v1/decks/{deck.Id}/transition-plan", UriKind.Relative), Cancel);

        Assert.Equal(1, Assert.Single(plan!.Remove).Quantity);
        var forest = Assert.Single(plan.Add);
        Assert.Equal((1, 1), (forest.Quantity, forest.ToBuy));
        Assert.Empty(forest.Closest);
    }

    private static async Task<Guid> CreateLocationAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/collection/locations", new { name, kind = "rack" }, Cancel);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task AddAsync(HttpClient client, Guid locationId, params object[] entries)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/collection/locations/{locationId}/entries", new { entries }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancel));
    }

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string name, params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name, lines }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}
