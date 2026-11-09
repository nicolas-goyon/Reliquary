using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Decks.Api;
using Reliquary.Decks.Exploration;

namespace Reliquary.Decks.Tests;

/// <summary>
/// The endpoints of the deck sharing: the public deck, the link preview and oEmbed. See docs/decisions/0046-deck-image-and-embed.md.
/// </summary>
[Collection(DecksGroup.Name)]
public sealed class DeckSharingTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VisitorGetsAPublicDeckAndItsPreview()
    {
        using var author = await instance.NewUserAsync();
        using var visitor = (await instance.GetAsync()).CreateClient();
        var deck = await CreateDeckAsync(author, "Atraxa <script>", "public", "A \"fun\" deck.\nWith counters.");
        await instance.PublishEventsAsync();

        var publicDeck = await visitor.GetFromJsonAsync<PublicDeckResponse>(new Uri($"/api/v1/public-decks/{deck.Id}", UriKind.Relative), Cancel);
        var preview = await visitor.GetAsync(new Uri($"/api/v1/public-decks/{deck.Id}/preview", UriKind.Relative), Cancel);
        var html = await preview.Content.ReadAsStringAsync(Cancel);

        Assert.Equal(("Atraxa <script>", 2), (publicDeck!.Name, publicDeck.CardCount));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("text/html", preview.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<meta property=\"og:title\" content=\"Atraxa &lt;script&gt;\">", html, StringComparison.Ordinal);
        Assert.Contains("Commander deck by Tester · 2 cards · Atraxa, Praetors&#39; Voice. A &quot;fun&quot; deck. With counters.", html, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:url\" content=\"https://reliquary.test/decks/{deck.Id}\">", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"https://reliquary.test/api/v1/oembed?url=https%3A%2F%2Freliquary.test%2Fdecks%2F{deck.Id}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("unlisted")]
    public async Task DeckThatIsNotPublicHasNoPublicDeckPreviewOrOEmbed(string visibility)
    {
        using var author = await instance.NewUserAsync();
        using var visitor = (await instance.GetAsync()).CreateClient();
        var deck = await CreateDeckAsync(author, "Secret", visibility, string.Empty);
        await instance.PublishEventsAsync();

        // The owner also gets 404: these endpoints show only the read model of the public decks.
        var publicDeck = await author.GetAsync(new Uri($"/api/v1/public-decks/{deck.Id}", UriKind.Relative), Cancel);
        var preview = await visitor.GetAsync(new Uri($"/api/v1/public-decks/{deck.Id}/preview", UriKind.Relative), Cancel);
        var oembed = await visitor.GetAsync(new Uri($"/api/v1/oembed?url={Uri.EscapeDataString($"https://reliquary.test/decks/{deck.Id}")}", UriKind.Relative), Cancel);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (publicDeck.StatusCode, preview.StatusCode, oembed.StatusCode));
        Assert.Equal(ErrorCodes.NotFound, await CodeAsync(oembed));
    }

    [Fact]
    public async Task DeckThatBecomesPrivateLosesItsPreview()
    {
        using var author = await instance.NewUserAsync();
        using var visitor = (await instance.GetAsync()).CreateClient();
        var deck = await CreateDeckAsync(author, "Soon private", "public", string.Empty);
        await instance.PublishEventsAsync();
        var before = await visitor.GetAsync(new Uri($"/api/v1/public-decks/{deck.Id}/preview", UriKind.Relative), Cancel);

        await author.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}", new { visibility = "private" }, Cancel);
        await instance.PublishEventsAsync();
        var after = await visitor.GetAsync(new Uri($"/api/v1/public-decks/{deck.Id}/preview", UriKind.Relative), Cancel);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (before.StatusCode, after.StatusCode));
    }

    [Fact]
    public async Task OEmbedGivesAnIframeOnTheEmbedPage()
    {
        using var author = await instance.NewUserAsync();
        using var visitor = (await instance.GetAsync()).CreateClient();
        var deck = await CreateDeckAsync(author, "Embed me", "public", string.Empty);
        await instance.PublishEventsAsync();
        var link = Uri.EscapeDataString($"https://other-host.example/embed/decks/{deck.Id}");

        var response = await visitor.GetAsync(new Uri($"/api/v1/oembed?url={link}&maxwidth=400&maxheight=50", UriKind.Relative), Cancel);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("rich", "1.0", "Embed me", "Reliquary"), (root.GetProperty("type").GetString(), root.GetProperty("version").GetString(), root.GetProperty("title").GetString(), root.GetProperty("provider_name").GetString()));
        Assert.Equal((400, 200), (root.GetProperty("width").GetInt32(), root.GetProperty("height").GetInt32()));
        Assert.Equal("Tester", root.GetProperty("author_name").GetString());
        Assert.Equal(
            $"<iframe src=\"https://reliquary.test/embed/decks/{deck.Id}\" title=\"Embed me · Reliquary\" width=\"400\" height=\"200\" style=\"border:0\" loading=\"lazy\"></iframe>",
            root.GetProperty("html").GetString());
    }

    [Theory]
    [InlineData("url=not-a-link", HttpStatusCode.BadRequest, "validation_failed")]
    [InlineData("url=https%3A%2F%2Freliquary.test%2Fcards%2F0190a000-0000-7000-8000-0000000000c1", HttpStatusCode.BadRequest, "validation_failed")]
    [InlineData("url=https%3A%2F%2Freliquary.test%2Fdecks%2F0190a000-0000-7000-8000-0000000000d1&format=xml", HttpStatusCode.NotImplemented, DeckErrorCodes.OEmbedFormatNotAvailable)]
    [InlineData("url=https%3A%2F%2Freliquary.test%2Fdecks%2F0190a000-0000-7000-8000-0000000000d1", HttpStatusCode.NotFound, "not_found")]
    public async Task OEmbedRefusesWrongRequests(string query, HttpStatusCode status, string code)
    {
        using var visitor = (await instance.GetAsync()).CreateClient();

        var response = await visitor.GetAsync(new Uri($"/api/v1/oembed?{query}", UriKind.Relative), Cancel);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
    }

    [Theory]
    [InlineData("https://example.org/decks/0190a000-0000-7000-8000-0000000000d1", true)]
    [InlineData("http://example.org/embed/decks/0190a000-0000-7000-8000-0000000000d1/", true)]
    [InlineData("https://example.org/decks/0190a000-0000-7000-8000-0000000000d1/test", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/decks/0190a000-0000-7000-8000-0000000000d1", false)]
    [InlineData(null, false)]
    public void ReadDeckLinkAcceptsOnlyLinksToADeck(string? url, bool found) =>
        Assert.Equal(found, DeckSharing.ReadDeckLink(url) is not null);

    [Fact]
    public void DescriptionIsShortAndNamesTheFormat()
    {
        var deck = new PublicDeckResponse(Guid.NewGuid(), "Deck", "standardbrawl", "W", [], new AuthorSummaryResponse(Guid.NewGuid(), "Ana"), 60, 0, 0, DateTimeOffset.UnixEpoch);

        var text = DeckSharing.Description(deck, new string('x', 1000));

        Assert.StartsWith("Standard Brawl deck by Ana · 60 cards. xxx", text, StringComparison.Ordinal);
        Assert.Equal(DeckSharing.MaxDescriptionLength, text.Length);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        return document.RootElement.GetProperty("code").GetString();
    }

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string name, string visibility, string description)
    {
        var lines = new object[] { new { name = "Atraxa, Praetors' Voice", zone = "commander" }, new { name = "Sol Ring" } };
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name, format = "commander", visibility, description, lines }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}
