using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Reliquary.Cards.Api;

namespace Reliquary.Cards.Tests;

[Collection(ImportedCardsGroup.Name)]
public sealed class AutocompleteTests(ImportedCardsFixture fixture)
{
    [Fact]
    public async Task AutocompleteFindsTheStartOfANameFirst()
    {
        using var client = await CreateClientAsync();

        var items = await GetAsync(client, "light");

        Assert.Equal("Lightning Bolt", items[0].Name);
    }

    [Fact]
    public async Task AutocompleteFindsALocalizedNameAndShowsIt()
    {
        using var client = await CreateClientAsync();

        var items = await GetAsync(client, "foudr", "fr");

        var bolt = Assert.Single(items, item => item.Name == "Lightning Bolt");
        Assert.Equal(("Foudre", "fr"), (bolt.MatchedName, bolt.Lang));
    }

    [Fact]
    public async Task AutocompleteFindsAPartOfAFaceName()
    {
        using var client = await CreateClientAsync();

        var items = await GetAsync(client, "aberration");

        Assert.Contains(items, item => item.Name == "Delver of Secrets // Insectile Aberration");
    }

    [Fact]
    public async Task AutocompleteNeedsTwoCharactersAndHidesTheExtraCards()
    {
        using var client = await CreateClientAsync();

        Assert.Empty(await GetAsync(client, "l"));
        Assert.Empty(await GetAsync(client, "gobli"));
    }

    private static async Task<List<AutocompleteItem>> GetAsync(HttpClient client, string text, string lang = "en") =>
        (await client.GetFromJsonAsync<List<AutocompleteItem>>(
            new Uri($"/api/v1/cards/autocomplete?q={Uri.EscapeDataString(text)}&lang={lang}", UriKind.Relative),
            TestContext.Current.CancellationToken))!;

    private async Task<HttpClient> CreateClientAsync()
    {
        var cards = await fixture.GetAsync();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Users", cards.Database.ConnectionString);
        });
        return factory.CreateClient();
    }
}
