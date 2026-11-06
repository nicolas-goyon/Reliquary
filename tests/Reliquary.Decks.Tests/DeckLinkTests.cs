using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Decks.Api;
using Reliquary.Decks.Exchange;
using Reliquary.Decks.Tests.Recommendations;
using Reliquary.Decks.Text;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class DeckLinkTests(DecksInstance instance)
{
    private const string MoxfieldDeck = """
        {
          "name": "Sauron",
          "boards": {
            "commanders": { "count": 1, "cards": { "a": { "quantity": 1, "finish": "foil", "card": { "name": "Atraxa, Praetors' Voice", "set": "2x2", "cn": "190" } } } },
            "mainboard": { "count": 33, "cards": {
              "b": { "quantity": 1, "finish": "nonFoil", "card": { "name": "Sol Ring", "set": "plst", "cn": "WAR-59" } },
              "c": { "quantity": 1, "finish": "etched", "card": { "name": "Fire // Ice", "set": "x", "cn": "1" } },
              "d": { "quantity": 30, "card": { "name": "Forest" } },
              "e": { "quantity": 1, "card": { "name": "Lightning Bolt #1 -- x" } }
            } },
            "maybeboard": { "count": 1, "cards": { "f": { "quantity": 1, "card": { "name": "Cultivate" } } } },
            "tokens": { "count": 1, "cards": { "g": { "quantity": 1, "card": { "name": "Soldier" } } } }
          }
        }
        """;

    private const string ArchidektDeck = """
        {
          "name": "Atraxa",
          "categories": [ { "name": "Maybe", "includedInDeck": false }, { "name": "Ramp", "includedInDeck": true } ],
          "cards": [
            { "quantity": 1, "modifier": "Foil", "categories": ["Commander"], "card": { "collectorNumber": "190", "edition": { "editioncode": "2x2" }, "oracleCard": { "name": "Atraxa, Praetors' Voice" } } },
            { "quantity": 1, "modifier": "Normal", "categories": ["Ramp"], "card": { "collectorNumber": "263", "edition": { "editioncode": "c21" }, "oracleCard": { "name": "Sol Ring" } } },
            { "quantity": 1, "categories": ["Maybe"], "card": { "oracleCard": { "name": "Cultivate" } } },
            { "quantity": 2, "categories": [], "card": { "oracleCard": { "name": "Forest" } } }
          ]
        }
        """;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://moxfield.com/decks/AbC-12_x", "moxfield", "AbC-12_x")]
    [InlineData("https://www.moxfield.com/decks/AbC-12_x/", "moxfield", "AbC-12_x")]
    [InlineData("http://archidekt.com/decks/123456/sauron_orcs", "archidekt", "123456")]
    [InlineData(" https://www.archidekt.com/decks/42 ", "archidekt", "42")]
    public void ParserReadsTheDeckPages(string address, string site, string id) =>
        Assert.Equal(new DeckLink(site, id), DeckLinks.Parse(address));

    [Theory]
    [InlineData(null)]
    [InlineData("moxfield.com/decks/abc")]
    [InlineData("https://moxfield.com/users/abc")]
    [InlineData("https://moxfield.com.evil.example/decks/abc")]
    [InlineData("https://evil.example/moxfield.com/decks/abc")]
    [InlineData("https://archidekt.com/decks/abc")]
    [InlineData("ftp://moxfield.com/decks/abc")]
    public void ParserRefusesOtherAddresses(string? address) => Assert.Null(DeckLinks.Parse(address));

    [Fact]
    public void MoxfieldDeckBecomesDeckText()
    {
        using var document = JsonDocument.Parse(MoxfieldDeck);
        var deck = DeckLinks.FromMoxfield(document.RootElement);
        var parsed = DeckText.Parse(deck.Text);

        Assert.Equal("Sauron", deck.Name);
        Assert.Empty(parsed.Errors);
        Assert.Collection(
            parsed.Lines,
            line => Assert.Equal(("Atraxa, Praetors' Voice", "2x2", "190", "foil", "commander"), (line.Name, line.SetCode, line.CollectorNumber, line.Finish, line.Zone)),
            line => Assert.Equal(("Sol Ring", "plst", "WAR-59", "main"), (line.Name, line.SetCode, line.CollectorNumber, line.Zone)),
            line => Assert.Equal(("Fire // Ice", null, "etched"), (line.Name, line.SetCode, line.Finish)),
            line => Assert.Equal(("Forest", 30), (line.Name, line.Quantity)),
            line => Assert.Equal(("Lightning Bolt 1 - x", 0), (line.Name, line.Tags.Count)),
            line => Assert.Equal(("Cultivate", "considering"), (line.Name, line.Zone)));
    }

    [Fact]
    public void ArchidektDeckBecomesDeckText()
    {
        using var document = JsonDocument.Parse(ArchidektDeck);
        var deck = DeckLinks.FromArchidekt(document.RootElement);
        var parsed = DeckText.Parse(deck.Text);

        Assert.Empty(parsed.Errors);
        Assert.Collection(
            parsed.Lines,
            line => Assert.Equal(("Atraxa, Praetors' Voice", "commander", "foil"), (line.Name, line.Zone, line.Finish)),
            line => Assert.Equal(("Sol Ring", "main", "c21", "Ramp"), (line.Name, line.Zone, line.SetCode, line.Tags.Single())),
            line => Assert.Equal(("Forest", 2, "main"), (line.Name, line.Quantity, line.Zone)),
            line => Assert.Equal(("Cultivate", "considering", 0), (line.Name, line.Zone, line.Tags.Count)));
    }

    [Fact]
    public async Task LinkImportMakesTheDeckFromMoxfieldWithTheAllowedUserAgent()
    {
        var handler = new RecordedHandler(MoxfieldDeck.Replace("Lightning Bolt #1 -- x", "Lightning Bolt", StringComparison.Ordinal).Replace("\"set\": \"plst\", \"cn\": \"WAR-59\"", "\"set\": \"zz\"", StringComparison.Ordinal));
        await using var api = WithHandler(await instance.GetAsync(), handler, "Approved-Agent/1.0");
        using var client = await SignInAsync(api);

        var response = await client.PostAsJsonAsync("/api/v1/decks/import-link", new { url = "https://moxfield.com/decks/AbC-12_x" }, Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ImportDeckResponse>(Cancel))!;
        Assert.Equal("Sauron", result.Deck.Name);
        Assert.Equal(35, result.Deck.Lines.Sum(line => line.Quantity));
        Assert.DoesNotContain(result.Deck.Lines, line => line.Zone == "tokens");
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api2.moxfield.com/v3/decks/all/AbC-12_x", request.Uri!.ToString());
        Assert.Equal("Approved-Agent/1.0", handler.UserAgents.Single());
    }

    [Fact]
    public async Task LinkImportMakesTheDeckFromArchidekt()
    {
        var handler = new RecordedHandler(ArchidektDeck);
        await using var api = WithHandler(await instance.GetAsync(), handler, null);
        using var client = await SignInAsync(api);

        var response = await client.PostAsJsonAsync("/api/v1/decks/import-link", new { url = "https://archidekt.com/decks/42/atraxa", name = "My copy" }, Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ImportDeckResponse>(Cancel))!;
        Assert.Equal("My copy", result.Deck.Name);
        Assert.Contains(result.Deck.Lines, line => line.Zone == "commander");
        Assert.Equal("https://archidekt.com/api/decks/42/", Assert.Single(handler.Requests).Uri!.ToString());
    }

    [Fact]
    public async Task LinkImportGivesAClearErrorForEachProblem()
    {
        var notFound = new RecordedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var api = WithHandler(await instance.GetAsync(), notFound, null);
        using var client = await SignInAsync(api);

        var invalid = await client.PostAsJsonAsync("/api/v1/decks/import-link", new { url = "https://example.com/decks/1" }, Cancel);
        var moxfield = await client.PostAsJsonAsync("/api/v1/decks/import-link", new { url = "https://moxfield.com/decks/abc" }, Cancel);
        var missing = await client.PostAsJsonAsync("/api/v1/decks/import-link", new { url = "https://archidekt.com/decks/1" }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, moxfield.StatusCode);
        Assert.Equal(DeckErrorCodes.LinkSiteUnavailable, await CodeAsync(moxfield));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Equal(DeckErrorCodes.LinkDeckNotFound, await CodeAsync(missing));
        Assert.Single(notFound.Requests);

        var broken = new RecordedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") });
        await using var brokenApi = WithHandler(await instance.GetAsync(), broken, null);
        using var other = await SignInAsync(brokenApi);
        var failed = await other.PostAsJsonAsync("/api/v1/decks/import-link", new { url = "https://archidekt.com/decks/1" }, Cancel);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(DeckErrorCodes.LinkSiteFailed, await CodeAsync(failed));
    }

    private static WebApplicationFactory<Program> WithHandler(WebApplicationFactory<Program> factory, RecordedHandler handler, string? moxfieldUserAgent) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (moxfieldUserAgent is not null)
            {
                builder.UseSetting("Decks:LinkImport:MoxfieldUserAgent", moxfieldUserAgent);
            }

            builder.ConfigureTestServices(services => services
                .AddHttpClient(DeckLinkReader.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler));
        });

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString();

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> api)
    {
        var client = api.CreateClient();
        var email = $"links-{Guid.NewGuid():N}@example.org";
        var credentials = new { email, password = "correct horse battery", displayName = "Tester", language = "en" };
        (await client.PostAsJsonAsync("/api/v1/auth/register", credentials, Cancel)).EnsureSuccessStatusCode();
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/sign-in", credentials, Cancel);
        signIn.EnsureSuccessStatusCode();
        var session = await signIn.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }
}
