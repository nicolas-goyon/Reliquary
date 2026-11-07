using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Cards.Api;

namespace Reliquary.Cards.Tests;

[Collection(ImportedCardsGroup.Name)]
public sealed class CardEndpointTests(ImportedCardsFixture fixture)
{
    [Theory]
    [InlineData("Lightning Bolt", "Lightning Bolt")]
    [InlineData("LIGHTNING BOLT", "Lightning Bolt")]
    [InlineData("Foudre", "Lightning Bolt")]
    [InlineData("稲妻", "Lightning Bolt")]
    [InlineData("insectile aberration", "Delver of Secrets // Insectile Aberration")]
    [InlineData("Atraxa, Praetors’ Voice", "Atraxa, Praetors' Voice")]
    [InlineData("Jotun Grunt", "Jötun Grunt")]
    public async Task AnyNameOfACardFindsItsOracleCard(string name, string expected)
    {
        using var client = await CreateClientAsync();

        var card = await client.GetFromJsonAsync<CardResponse>(
            new Uri($"/api/v1/cards/named?name={Uri.EscapeDataString(name)}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expected, card!.Name);
    }

    [Fact]
    public async Task AlternateNameFindsTheCard()
    {
        using var client = await CreateClientAsync();
        var zilortha = await client.GetFromJsonAsync<CardResponse>(
            new Uri("/api/v1/cards/named?name=Zilortha, Strength Incarnate", UriKind.Relative), TestContext.Current.CancellationToken);
        var flavorName = zilortha!.Names.First(name => name.Kind == "flavor").Name;

        var card = await client.GetFromJsonAsync<CardResponse>(
            new Uri($"/api/v1/cards/named?name={Uri.EscapeDataString(flavorName)}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(zilortha.Id, card!.Id);
    }

    [Fact]
    public async Task FuzzySearchFindsANameWithATypingError()
    {
        using var client = await CreateClientAsync();

        var exact = await client.GetAsync(new Uri("/api/v1/cards/named?name=Lightnig Bolt", UriKind.Relative), TestContext.Current.CancellationToken);
        var fuzzy = await client.GetFromJsonAsync<CardResponse>(new Uri("/api/v1/cards/named?name=Lightnig Bolt&fuzzy=true", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, exact.StatusCode);
        Assert.Equal("Lightning Bolt", fuzzy!.Name);
    }

    [Fact]
    public async Task CardHasItsFacesAndItsDefaultPrinting()
    {
        using var client = await CreateClientAsync();
        var named = await client.GetFromJsonAsync<CardResponse>(new Uri("/api/v1/cards/named?name=Fire // Ice", UriKind.Relative), TestContext.Current.CancellationToken);

        var card = await client.GetFromJsonAsync<CardResponse>(new Uri($"/api/v1/cards/{named!.Id}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(["Fire", "Ice"], card!.Faces.Select(face => face.Name));
        Assert.NotNull(card.DefaultPrinting);
        Assert.NotEmpty(card.DefaultPrinting.ImageUris);
        Assert.Equal("legal", card.Legalities["commander"]);
    }

    [Fact]
    public async Task PrintingsHaveCursorPages()
    {
        using var client = await CreateClientAsync();
        var bolt = await client.GetFromJsonAsync<CardResponse>(new Uri("/api/v1/cards/named?name=Lightning Bolt", UriKind.Relative), TestContext.Current.CancellationToken);

        var first = await client.GetFromJsonAsync<Page<PrintingResponse>>(new Uri($"/api/v1/cards/{bolt!.Id}/printings?limit=2", UriKind.Relative), TestContext.Current.CancellationToken);
        var second = await client.GetFromJsonAsync<Page<PrintingResponse>>(
            new Uri($"/api/v1/cards/{bolt.Id}/printings?limit=2&cursor={first!.NextCursor}", UriKind.Relative), TestContext.Current.CancellationToken);
        var french = await client.GetFromJsonAsync<Page<PrintingResponse>>(new Uri($"/api/v1/cards/{bolt.Id}/printings?lang=fr", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second!.Items.Select(item => item.Id)));
        Assert.All(french!.Items, printing => Assert.Equal("fr", printing.Lang));
        Assert.NotEmpty(french.Items);
    }

    [Fact]
    public async Task UnknownCardIsNotFound()
    {
        using var client = await CreateClientAsync();

        var response = await client.GetAsync(new Uri($"/api/v1/cards/{Guid.NewGuid()}", UriKind.Relative), TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task NamedWithoutANameIsAValidationError()
    {
        using var client = await CreateClientAsync();

        var response = await client.GetAsync(new Uri("/api/v1/cards/named", UriKind.Relative), TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetsListContainsTheSetsOfThePrintings()
    {
        using var client = await CreateClientAsync();

        var sets = await client.GetFromJsonAsync<List<SetResponse>>(new Uri("/api/v1/sets", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Contains(sets!, set => set.Code == "fdn");
        Assert.True(sets!.Count > 10);
    }

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
