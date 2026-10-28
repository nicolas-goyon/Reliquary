using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Decks.Api;
using Reliquary.Decks.Purchases;
using Reliquary.Decks.Versions;

namespace Reliquary.Decks.Tests.Purchases;

[Collection(DecksGroup.Name)]
public sealed class PurchaseListEndpointTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserSetsFrenchAndNonFoilOnALineAndReadsThePreferencesAgain()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Control", new { name = "Counterspell" });
        var line = deck.Lines.Single();

        var update = await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/{line.Id}", new { purchasePreferences = new { languages = new[] { "FR" }, foil = "nonfoil", maximumPrice = new { amount = 2.5m, currency = "EUR" } } }, Cancel);
        var read = await client.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var commit = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/versions", new { message = "Preferences" }, Cancel);
        var version = (await commit.Content.ReadFromJsonAsync<DeckVersionResponse>(Cancel))!;
        var frozen = await client.GetFromJsonAsync<DeckVersionDetailResponse>(new Uri($"/api/v1/decks/{deck.Id}/versions/{version.Id}", UriKind.Relative), Cancel);
        var variant = (await (await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/variants", new { name = "Budget" }, Cancel)).Content.ReadFromJsonAsync<DeckVariantResponse>(Cancel))!;
        var variantDeck = await client.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{variant.Id}", UriKind.Relative), Cancel);
        var clear = await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/{line.Id}", new { clearPurchasePreferences = true }, Cancel);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var preferences = read!.Lines.Single().PurchasePreferences!;
        Assert.Equal(["fr"], preferences.Languages!);
        Assert.Equal("nonfoil", preferences.Foil);
        Assert.Equal(new PriceLimit(2.5m, "EUR"), preferences.MaximumPrice);
        Assert.Equal(["fr"], frozen!.Lines.Single().PurchasePreferences!.Languages!);
        Assert.Equal("nonfoil", variantDeck!.Lines.Single().PurchasePreferences!.Foil);
        Assert.Null((await clear.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!.Lines.Single().PurchasePreferences);
    }

    [Fact]
    public async Task WrongPreferencesGiveAValidationError()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Control", new { name = "Counterspell" });

        var response = await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/{deck.Lines.Single().Id}", new { purchasePreferences = new { foil = "shiny", minimumCondition = "new" } }, Cancel);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["purchasePreferences.foil", "purchasePreferences.minimumCondition"],
            problem.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()).Order());
    }

    [Fact]
    public async Task PurchaseListUsesTheAvailableCardsOfTheCollection()
    {
        using var client = await instance.NewUserAsync();
        var rack = await CreateLocationAsync(client, "Rack 2");
        var showcase = await CreateLocationAsync(client, "Showcase");
        await client.PatchAsJsonAsync($"/api/v1/collection/locations/{showcase}", new { pickable = false }, Cancel);
        await AddAsync(client, rack, new { name = "Counterspell" }, new { name = "Forest", quantity = 3 });
        await AddAsync(client, showcase, new { name = "Cultivate" });
        var other = await CreateDeckAsync(client, "Other", new { name = "Sol Ring" });
        await client.PutAsJsonAsync($"/api/v1/decks/{other.Id}/assembled", new { }, Cancel);
        var deck = await CreateDeckAsync(
            client,
            "Ramp",
            new { name = "Counterspell" },
            new { name = "Forest", quantity = 5 },
            new { name = "Cultivate" },
            new { name = "Sol Ring" },
            new { name = "Lightning Bolt", zone = "considering" });
        var counterspell = deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Counterspell");
        await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/{counterspell.Id}", new { purchasePreferences = new { languages = new[] { "fr" } } }, Cancel);

        var list = await client.GetFromJsonAsync<PurchaseListResponse>(new Uri($"/api/v1/decks/{deck.Id}/purchase-list", UriKind.Relative), Cancel);
        var stranger = await (await instance.NewUserAsync()).GetAsync(new Uri($"/api/v1/decks/{deck.Id}/purchase-list", UriKind.Relative), Cancel);

        PurchaseListLineResponse Line(string name) => list!.Lines.Single(line => list.Cards[line.OracleId].Name == name);
        Assert.Equal(4, list!.Lines.Count);
        Assert.Equal((1, "Rack 2", "language"), (Line("Counterspell").ToBuy, Line("Counterspell").Closest.Single().LocationName, string.Join(',', Line("Counterspell").Closest.Single().Reasons)));
        Assert.Equal((3, 2), (Line("Forest").FromCollection.Sum(copy => copy.Quantity), Line("Forest").ToBuy));
        Assert.Equal(("Showcase", "unavailable"), (Line("Cultivate").Closest.Single().LocationName, Line("Cultivate").Closest.Single().Reasons.Single()));
        Assert.Equal(("Other", "unavailable"), (Line("Sol Ring").Closest.Single().LocationName, Line("Sol Ring").Closest.Single().Reasons.Single()));
        Assert.Equal(1 + 2 + 1 + 1, list.TotalToBuy);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
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
