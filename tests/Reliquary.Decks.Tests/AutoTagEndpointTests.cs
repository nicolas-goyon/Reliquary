using System.Net;
using System.Net.Http.Json;
using Reliquary.Decks.Api;
using Reliquary.Decks.AutoTag;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class AutoTagEndpointTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<DeckResponse> CreateAsync(HttpClient client, params object[] lines)
    {
        var created = await client.PostAsJsonAsync("/api/v1/decks", new { name = "Auto-tag", lines }, Cancel);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }

    private static Task<AutoTagResponse?> SuggestAsync(HttpClient client, Guid deckId) =>
        client.GetFromJsonAsync<AutoTagResponse>(new Uri($"/api/v1/decks/{deckId}/auto-tag", UriKind.Relative), Cancel);

    private static Task<HttpResponseMessage> ApplyAsync(HttpClient client, Guid deckId, object body) =>
        client.PostAsJsonAsync($"/api/v1/decks/{deckId}/auto-tag", body, Cancel);

    private static AutoTagLineResponse Line(AutoTagResponse response, string name) => response.Lines.Single(line => line.Name == name);

    [Fact]
    public async Task SuggestionsGiveTheCategoriesAndTheReasons()
    {
        using var owner = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var deck = await CreateAsync(
            owner,
            new { name = "Sol Ring" },
            new { name = "Swords to Plowshares", categories = new[] { "targeted_interaction" } },
            new { name = "Counterspell" },
            new { name = "Forest", quantity = 10 },
            new { name = "Krenko, Mob Boss", zone = "commander" });

        var suggestions = await SuggestAsync(owner, deck.Id);
        var forOther = await other.GetAsync(new Uri($"/api/v1/decks/{deck.Id}/auto-tag", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.NotFound, forOther.StatusCode);
        Assert.NotNull(suggestions);
        Assert.Equal(deck.Version, suggestions.Version);
        Assert.Equal(5, suggestions.Lines.Count);

        var solRing = Line(suggestions, "Sol Ring");
        Assert.Equal(["ramp"], solRing.Suggested);
        Assert.Equal(AutoTagSources.OracleTag, solRing.Source);
        Assert.Equal(new AutoTagReasonResponse("ramp", AutoTagSources.OracleTag, "mana-rock"), solRing.Reasons.Single());
        Assert.True(solRing.Changes);

        var swords = Line(suggestions, "Swords to Plowshares");
        Assert.Equal(["targeted_interaction"], swords.Suggested);
        Assert.False(swords.Changes);

        var forest = Line(suggestions, "Forest");
        Assert.Equal(["land"], forest.Suggested);
        Assert.Equal(AutoTagSources.TextRule, forest.Source);
        Assert.Equal(["targeted_interaction"], Line(suggestions, "Counterspell").Suggested);
    }

    [Fact]
    public async Task ApplyChangesTheLinesInOneVersionAndRecordsTheUserRules()
    {
        using var owner = await instance.NewUserAsync();
        var deck = await CreateAsync(owner, new { name = "Sol Ring" }, new { name = "Counterspell" }, new { name = "Forest" });
        var suggestions = (await SuggestAsync(owner, deck.Id))!;

        // The user accepts Sol Ring and Forest, and changes Counterspell.
        var applied = await ApplyAsync(owner, deck.Id, new
        {
            expectedVersion = suggestions.Version,
            lines = new object[]
            {
                new { lineId = Line(suggestions, "Sol Ring").LineId, categories = new[] { "ramp" } },
                new { lineId = Line(suggestions, "Forest").LineId, categories = new[] { "land" } },
                new { lineId = Line(suggestions, "Counterspell").LineId, categories = new[] { "protection", "targeted_interaction" } },
            },
        });
        applied.EnsureSuccessStatusCode();
        var changed = (await applied.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;

        Assert.Equal(deck.Version + 1, changed.Version);
        Assert.Equal(["ramp"], changed.Lines.Single(line => line.Id == Line(suggestions, "Sol Ring").LineId).Categories);
        Assert.Equal(["targeted_interaction", "protection"], changed.Lines.Single(line => line.Id == Line(suggestions, "Counterspell").LineId).Categories);

        // The next suggestions of another deck of the user use the rule for Counterspell, and only for Counterspell.
        var next = await CreateAsync(owner, new { name = "Counterspell" }, new { name = "Sol Ring" });
        var nextSuggestions = (await SuggestAsync(owner, next.Id))!;
        var counterspell = Line(nextSuggestions, "Counterspell");
        Assert.Equal(["targeted_interaction", "protection"], counterspell.Suggested);
        Assert.Equal(AutoTagSources.UserRule, counterspell.Source);
        Assert.Equal(AutoTagSources.OracleTag, Line(nextSuggestions, "Sol Ring").Source);

        // The rules of a user do not change the suggestions of another user.
        using var other = await instance.NewUserAsync();
        var otherDeck = await CreateAsync(other, new { name = "Counterspell" });
        Assert.Equal(AutoTagSources.OracleTag, Line((await SuggestAsync(other, otherDeck.Id))!, "Counterspell").Source);

        // A choice that is equal to the automatic suggestion removes the rule.
        (await ApplyAsync(owner, next.Id, new { lines = new[] { new { lineId = counterspell.LineId, categories = new[] { "targeted_interaction" } } } })).EnsureSuccessStatusCode();
        var afterReset = Line((await SuggestAsync(owner, next.Id))!, "Counterspell");
        Assert.Equal(AutoTagSources.OracleTag, afterReset.Source);
    }

    [Fact]
    public async Task LineEditOfTheCategoriesBecomesAUserRule()
    {
        using var owner = await instance.NewUserAsync();
        var deck = await CreateAsync(owner, new { name = "Sol Ring" });
        var line = deck.Lines.Single();

        // A change of the quantity only does not make a rule.
        (await owner.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/{line.Id}", new { quantity = 2 }, Cancel)).EnsureSuccessStatusCode();
        Assert.Equal(AutoTagSources.OracleTag, Line((await SuggestAsync(owner, deck.Id))!, "Sol Ring").Source);

        (await owner.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/{line.Id}", new { categories = new[] { "ramp", "synergy" } }, Cancel)).EnsureSuccessStatusCode();
        var other = await CreateAsync(owner, new { name = "Sol Ring" });
        var suggestion = Line((await SuggestAsync(owner, other.Id))!, "Sol Ring");

        Assert.Equal(["ramp", "synergy"], suggestion.Suggested);
        Assert.Equal(AutoTagSources.UserRule, suggestion.Source);
    }

    [Fact]
    public async Task ApplyRefusesAnInvalidRequest()
    {
        using var owner = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var deck = await CreateAsync(owner, new { name = "Sol Ring" });
        var lineId = deck.Lines.Single().Id;

        var empty = await ApplyAsync(owner, deck.Id, new { lines = Array.Empty<object>() });
        var badCategory = await ApplyAsync(owner, deck.Id, new { lines = new[] { new { lineId, categories = new[] { "mana" } } } });
        var unknownLine = await ApplyAsync(owner, deck.Id, new { lines = new[] { new { lineId = Guid.NewGuid(), categories = new[] { "ramp" } } } });
        var oldVersion = await ApplyAsync(owner, deck.Id, new { expectedVersion = deck.Version + 5, lines = new[] { new { lineId, categories = new[] { "ramp" } } } });
        var notOwner = await ApplyAsync(other, deck.Id, new { lines = new[] { new { lineId, categories = new[] { "ramp" } } } });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCategory.StatusCode);
        Assert.Contains("lines[0].categories[0]", await badCategory.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unknownLine.StatusCode);
        Assert.Contains("lines[0].lineId", await unknownLine.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, oldVersion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notOwner.StatusCode);

        var unchanged = await owner.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        Assert.Equal(deck.Version, unchanged!.Version);
    }
}
