using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Cards.Api;
using Reliquary.Cards.Search;

namespace Reliquary.Cards.Tests.Search;

[Collection(ImportedCardsGroup.Name)]
public sealed class CardSearchEndpointTests(ImportedCardsFixture fixture) : IAsyncDisposable
{
    private WebApplicationFactory<Program>? factory;

    [Fact]
    public async Task SearchGivesTheCardsWithTheirChosenPrinting()
    {
        using var client = await CreateClientAsync();

        var page = await GetPageAsync(client, "/api/v1/cards/search?q=" + Uri.EscapeDataString("!\"Fire // Ice\""));

        var item = Assert.Single(page.Items);
        Assert.Equal("Fire // Ice", item.Card.Name);
        Assert.Equal(["Fire", "Ice"], item.Card.Faces.Select(face => face.Name));
        Assert.NotEqual(Guid.Empty, item.Printing.Id);
        Assert.NotEmpty(item.Printing.ImageUris);
        Assert.Equal(1, page.Total);
        Assert.Null(page.NextCursor);
        Assert.Empty(page.Warnings);
    }

    [Fact]
    public async Task LocalizedNameFindsTheCard()
    {
        using var client = await CreateClientAsync();

        var page = await GetPageAsync(client, "/api/v1/cards/search?q=foudre");

        var item = Assert.Single(page.Items);
        Assert.Equal("Lightning Bolt", item.Card.Name);
        Assert.Equal("fr", item.Printing.Lang);
        Assert.Equal("Foudre", item.Printing.PrintedName);
    }

    [Fact]
    public async Task CursorGivesAllThePages()
    {
        using var client = await CreateClientAsync();
        var all = await GetPageAsync(client, "/api/v1/cards/search?q=mv>=0&limit=100");
        var ids = new List<Guid>();
        string? cursor = null;

        do
        {
            var page = await GetPageAsync(client, "/api/v1/cards/search?q=mv>=0&limit=7" + (cursor is null ? string.Empty : "&cursor=" + cursor));
            Assert.Equal(all.Total, page.Total);
            ids.AddRange(page.Items.Select(item => item.Card.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.True(all.Total > 7);
        Assert.Equal(all.Items.Select(item => item.Card.Id), ids);
    }

    [Fact]
    public async Task UniqueParameterGivesThePrintings()
    {
        using var client = await CreateClientAsync();

        var cards = await GetPageAsync(client, "/api/v1/cards/search?q=" + Uri.EscapeDataString("!\"Lightning Bolt\""));
        var prints = await GetPageAsync(client, "/api/v1/cards/search?unique=prints&q=" + Uri.EscapeDataString("!\"Lightning Bolt\""));
        var keyword = await GetPageAsync(client, "/api/v1/cards/search?unique=cards&q=" + Uri.EscapeDataString("!\"Lightning Bolt\" unique:prints"));

        Assert.Single(cards.Items);
        Assert.True(prints.Items.Count > 1);
        Assert.Equal(prints.Total, keyword.Total);
    }

    [Fact]
    public async Task UnknownKeywordGivesAWarning()
    {
        using var client = await CreateClientAsync();

        var page = await GetPageAsync(client, "/api/v1/cards/search?q=" + Uri.EscapeDataString("bolt foo:bar"));

        Assert.Single(page.Items);
        var warning = Assert.Single(page.Warnings);
        Assert.Equal(SearchWarningCodes.UnknownKeyword, warning.Code);
        Assert.Equal(5, warning.Position);
        Assert.Equal("foo:bar", warning.Term);
    }

    [Theory]
    [InlineData("t:goblin (", 9)]
    [InlineData("o:\"draw", 2)]
    [InlineData("t:creature o:/(abc/", 11)]
    public async Task SyntaxErrorIsAProblemWithItsPosition(string query, int position)
    {
        using var client = await CreateClientAsync();

        var response = await client.GetAsync(new Uri("/api/v1/cards/search?q=" + Uri.EscapeDataString(query), UriKind.Relative), TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CardSearchErrorCodes.SyntaxError, problem.GetProperty("code").GetString());
        Assert.Equal(position, problem.GetProperty("position").GetInt32());
        Assert.Contains($"position {position}", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryWithOnlyIgnoredTermsIsAProblem()
    {
        using var client = await CreateClientAsync();

        var response = await client.GetAsync(new Uri("/api/v1/cards/search?q=foo:bar", UriKind.Relative), TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CardSearchErrorCodes.EmptyQuery, problem.GetProperty("code").GetString());
        Assert.Equal("foo:bar", problem.GetProperty("warnings")[0].GetProperty("term").GetString());
    }

    [Theory]
    [InlineData("/api/v1/cards/search", "q", FieldErrorCodes.Required)]
    [InlineData("/api/v1/cards/search?q=bolt&unique=all", "unique", FieldErrorCodes.NotAllowed)]
    [InlineData("/api/v1/cards/search?q=bolt&order=size", "order", FieldErrorCodes.NotAllowed)]
    [InlineData("/api/v1/cards/search?q=bolt&dir=up", "dir", FieldErrorCodes.NotAllowed)]
    [InlineData("/api/v1/cards/search?q=bolt&limit=101", "limit", FieldErrorCodes.OutOfRange)]
    [InlineData("/api/v1/cards/search?q=bolt&cursor=abc", "cursor", FieldErrorCodes.InvalidFormat)]
    public async Task ParameterThatIsNotValidIsAValidationError(string url, string field, string code)
    {
        using var client = await CreateClientAsync();

        var response = await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
        var error = Assert.Single(problem.GetProperty("errors").EnumerateArray());
        Assert.Equal(field, error.GetProperty("field").GetString());
        Assert.Equal(code, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task QueryLongerThanTheLimitIsAValidationError()
    {
        using var client = await CreateClientAsync();

        var response = await client.GetAsync(new Uri("/api/v1/cards/search?q=" + new string('a', 1001), UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }
    }

    private static async Task<CardSearchResponse> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonSerializer.Deserialize<CardSearchResponse>(body, JsonSerializerOptions.Web)!;
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        var cards = await fixture.GetAsync();
        factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Users", cards.Database.ConnectionString);
        });
        return factory.CreateClient();
    }
}
