using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class DeckEndpointTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserCreatesADeckWithLinesFromAllKindsOfNames()
    {
        using var client = await instance.NewUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/decks", new
        {
            name = "  Atraxa superfriends ",
            lines = new object[]
            {
                new { name = "Atraxa, Praetors' Voice", zone = "commander" },
                new { name = "Foudre", quantity = 1 },
                new { name = "Sol Ring" },
                new { name = "Forest", quantity = 10 },
                new { name = "Fire /// Ice", zone = "considering" },
            },
        }, Cancel);
        var deck = await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Atraxa superfriends", deck!.Name);
        Assert.Equal("commander", deck.Format);
        Assert.Equal("private", deck.Visibility);
        Assert.True(deck.IsOwner);
        Assert.Equal(1, deck.Counts["commander"]);
        Assert.Equal(12, deck.Counts["main"]);
        Assert.Equal(1, deck.Counts["considering"]);
        Assert.Contains(deck.Cards.Values, card => card.Name == "Lightning Bolt");
        var fireIce = deck.Cards.Values.Single(card => card.Name == "Fire // Ice");
        Assert.Equal(4, fireIce.ManaValue);
        Assert.Equal(2, fireIce.CurveManaValue);
        Assert.Equal(2, fireIce.Faces.Count);
        Assert.All(deck.Cards.Values.Where(card => card.DefaultPrintingId is not null), card => Assert.True(deck.Printings.ContainsKey(card.DefaultPrintingId!.Value)));
    }

    [Fact]
    public async Task SameCardInTheSameZoneMergesAndAMoveMergesWithTheOtherLine()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring" }, new { name = "Sol Ring", zone = "sideboard" });

        deck = await SendAsync(client, HttpMethod.Post, $"/api/v1/decks/{deck.Id}/lines", new { lines = new[] { new { name = "Sol Ring", quantity = 2 } } });
        var main = deck.Lines.Single(line => line.Zone == "main");
        var sideboard = deck.Lines.Single(line => line.Zone == "sideboard");
        Assert.Equal(3, main.Quantity);

        deck = await SendAsync(client, HttpMethod.Patch, $"/api/v1/decks/{deck.Id}/lines/{sideboard.Id}", new { zone = "main" });
        var merged = Assert.Single(deck.Lines);
        Assert.Equal(4, merged.Quantity);
        Assert.Equal(main.Id, merged.Id);

        deck = await SendAsync(client, HttpMethod.Patch, $"/api/v1/decks/{deck.Id}/lines/{merged.Id}", new { quantity = 0 });
        Assert.Empty(deck.Lines);
        Assert.Equal(4, deck.Version);
    }

    [Fact]
    public async Task ReplaceKeepsTheUnchangedLinesAndRefusesAnOldVersion()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring" }, new { name = "Counterspell" });
        var solRing = deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Sol Ring");

        var replaced = await SendAsync(client, HttpMethod.Put, $"/api/v1/decks/{deck.Id}/lines", new
        {
            expectedVersion = deck.Version,
            lines = new object[] { new { name = "Sol Ring", note = "Turn one" }, new { name = "Cultivate", quantity = 2 } },
        });
        var conflict = await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/lines", new { expectedVersion = deck.Version, lines = Array.Empty<object>() }, Cancel);

        Assert.Equal(2, replaced.Lines.Count);
        Assert.Equal(solRing.Id, replaced.Lines.Single(line => line.OracleId == solRing.OracleId).Id);
        Assert.Equal("Turn one", replaced.Lines.Single(line => line.OracleId == solRing.OracleId).Note);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(DeckErrorCodes.VersionConflict, await CodeAsync(conflict));
    }

    [Fact]
    public async Task OtherUsersSeeOnlyTheUnlistedAndPublicDecks()
    {
        using var owner = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var api = await instance.GetAsync();
        using var visitor = api.CreateClient();
        var deck = await CreateAsync(owner, new { name = "Sol Ring" });

        var privateForOther = await other.GetAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var privateForVisitor = await visitor.GetAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        await SendAsync(owner, HttpMethod.Patch, $"/api/v1/decks/{deck.Id}", new { visibility = "unlisted" });
        var unlisted = await visitor.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var change = await other.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}", new { name = "Mine now" }, Cancel);
        var delete = await other.DeleteAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.NotFound, privateForOther.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, privateForVisitor.StatusCode);
        Assert.False(unlisted!.IsOwner);
        Assert.Single(unlisted.Lines);
        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task IncorrectLinesGiveFieldErrors()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring" });

        var response = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/lines", new
        {
            lines = new object[]
            {
                new { name = "No such card" },
                new { name = "Sol Ring", zone = "graveyard" },
                new { name = "Sol Ring", quantity = 0 },
                new { oracleId = Guid.NewGuid() },
            },
        }, Cancel);
        var body = await response.Content.ReadAsStringAsync(Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"field\":\"lines[0].name\",\"code\":\"card_not_found\"", body, StringComparison.Ordinal);
        Assert.Contains("\"field\":\"lines[1].zone\"", body, StringComparison.Ordinal);
        Assert.Contains("\"field\":\"lines[2].quantity\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintingMustBelongToTheCardOfTheLine()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring" }, new { name = "Counterspell" });
        var solRing = deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Sol Ring");
        var counterspellPrinting = deck.Cards.Values.Single(card => card.Name == "Counterspell").DefaultPrintingId!.Value;
        var solRingPrinting = deck.Cards[solRing.OracleId].DefaultPrintingId!.Value;

        var wrong = await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/{solRing.Id}", new { printingId = counterspellPrinting }, Cancel);
        var right = await SendAsync(client, HttpMethod.Patch, $"/api/v1/decks/{deck.Id}/lines/{solRing.Id}", new { printingId = solRingPrinting, finish = "foil" });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var line = right.Lines.Single(candidate => candidate.Id == solRing.Id);
        Assert.Equal(solRingPrinting, line.PrintingId);
        Assert.Equal("foil", line.Finish);
    }

    [Fact]
    public async Task DeckListHasPagesAndTheMostRecentChangeFirst()
    {
        using var client = await instance.NewUserAsync();
        for (var index = 0; index < 3; index++)
        {
            await CreateAsync(client, $"Deck {index}");
        }

        var first = await client.GetFromJsonAsync<Page<DeckSummaryResponse>>(new Uri("/api/v1/decks?limit=2", UriKind.Relative), Cancel);
        var second = await client.GetFromJsonAsync<Page<DeckSummaryResponse>>(new Uri($"/api/v1/decks?limit=2&cursor={first!.NextCursor}", UriKind.Relative), Cancel);

        Assert.Equal(["Deck 2", "Deck 1"], first.Items.Select(deck => deck.Name));
        Assert.Equal(["Deck 0"], second!.Items.Select(deck => deck.Name));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task DeleteRemovesTheDeckAndWritesTheEvents()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring" });

        var delete = await client.DeleteAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var read = await client.GetAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        await using var connection = new NpgsqlConnection(instance.ConnectionString);
        var events = (await connection.QueryAsync<string>(
            "select event_type from content.outbox where payload ->> 'deckId' = @id order by occurred_at, id", new { id = deck.Id.ToString() })).ToList();
        var directory = await connection.ExecuteScalarAsync<int>("select count(*) from users.deck_directory where deck_id = @id", new { id = deck.Id });
        Assert.Equal(["decks.deck.created", "decks.deck.deleted"], events);
        Assert.Equal(0, directory);
    }

    private static Task<DeckResponse> CreateAsync(HttpClient client, params object[] lines) =>
        CreateAsync(client, "Test deck", lines);

    private static async Task<DeckResponse> CreateAsync(HttpClient client, string name, params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name, lines }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }

    private static async Task<DeckResponse> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        var response = await client.SendAsync(request, Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString();
}
