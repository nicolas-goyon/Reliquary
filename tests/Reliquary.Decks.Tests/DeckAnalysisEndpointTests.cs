using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Reliquary.Decks.Analysis;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class DeckAnalysisEndpointTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EndpointAnalyzesTheDeckWithTheCardData()
    {
        using var owner = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var created = await owner.PostAsJsonAsync("/api/v1/decks", new
        {
            name = "Atraxa",
            lines = new object[]
            {
                new { name = "Atraxa, Praetors' Voice", zone = "commander" },
                new { name = "Sol Ring", categories = new[] { "ramp" } },
                new { name = "Llanowar Elves", categories = new[] { "ramp" } },
                new { name = "Cultivate", categories = new[] { "ramp" } },
                new { name = "Swords to Plowshares", categories = new[] { "targeted_interaction" } },
                new { name = "Fire // Ice", categories = new[] { "targeted_interaction" } },
                new { name = "Counterspell" },
                new { name = "Command Tower" },
                new { name = "Forest", quantity = 30 },
                new { name = "Lightning Bolt", zone = "sideboard" },
            },
        }, Cancel);
        created.EnsureSuccessStatusCode();
        var deck = await created.Content.ReadFromJsonAsync<DeckResponse>(Cancel);
        var path = new Uri($"/api/v1/decks/{deck!.Id}/analysis", UriKind.Relative);

        var analysis = await owner.GetFromJsonAsync<DeckAnalysis>(path, Cancel);
        var watch = Stopwatch.StartNew();
        (await owner.GetAsync(path, Cancel)).EnsureSuccessStatusCode();
        watch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"The second request to the analysis endpoint took {watch.Elapsed.TotalMilliseconds:F1} ms.");
        var forOther = await other.GetAsync(path, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, forOther.StatusCode);
        Assert.NotNull(analysis);
        Assert.Equal(38, analysis.CardCount);
        Assert.Equal(31, analysis.LandCount);
        Assert.Equal(7, analysis.SpellCount);
        Assert.Equal([0, 3, 2, 1, 1, 0, 0, 0], analysis.ManaCurve.Select(point => point.Count));
        Assert.Equal(31, analysis.Categories.Single(entry => entry.Key == "land").Count);
        Assert.Equal(2, analysis.Categories.Single(entry => entry.Key == DeckAnalyzer.Uncategorized).Count); // Atraxa and Counterspell.

        // Llanowar Elves (G) needs 19 green sources on turn 1: the 30 Forests and Command Tower give 31.
        // Atraxa (GWUB, mana value 4) needs only 16 + 1, so Llanowar Elves gives the requirement of the color.
        var green = analysis.Mana.Colors.Single(color => color.Color == "G");
        Assert.Equal((31, 31m, 19, DeckAnalyzer.Ok), (green.LandSources, green.Sources, green.Required!.Value, green.Status));
        Assert.Equal(1, green.OtherSources);

        // Swords to Plowshares needs 19 white sources. Command Tower is the only one.
        var white = analysis.Mana.Colors.Single(color => color.Color == "W");
        Assert.Equal((1, 19, DeckAnalyzer.Low), (white.LandSources, white.Required!.Value, white.Status));

        Assert.Equal(37, analysis.Draws.LibrarySize);
        Assert.Equal(31, analysis.Draws.Lands);
        Assert.Equal(CommanderNeeds.Low, analysis.Needs!.Single(need => need.Category == "ramp").Status);
        Assert.Equal(1, analysis.Bracket!.Bracket);
        Assert.Equal([CommanderBracket.TwoCardCombos], analysis.Bracket.Unchecked);
    }

    [Fact]
    public async Task UnknownDeckGivesNotFound()
    {
        using var user = await instance.NewUserAsync();

        var response = await user.GetAsync(new Uri($"/api/v1/decks/{Guid.NewGuid()}/analysis", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
