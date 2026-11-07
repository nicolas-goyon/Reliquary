using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class LineGroupActionTests(DecksInstance instance)
{
    private static readonly string[] TenCards =
    [
        "Sol Ring", "Counterspell", "Cultivate", "Lightning Bolt", "Black Lotus",
        "Mana Crypt", "Aether Vial", "Command Tower", "Swords to Plowshares", "Krenko, Mob Boss",
    ];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserMovesTenCardsToTheConsideringZoneInOneRequest()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, [.. TenCards.Select(name => new { name }), new { name = "Forest", quantity = 5 }]);
        var forest = deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Forest");
        var ids = deck.Lines.Where(line => line.Id != forest.Id).Select(line => line.Id).ToList();

        var moved = await GroupAsync(client, deck.Id, new { lineIds = ids, action = "move", zone = "considering", expectedVersion = deck.Version });

        Assert.Equal(deck.Version + 1, moved.Version);
        Assert.Equal(10, moved.Counts["considering"]);
        Assert.Equal(5, moved.Counts["main"]);
        Assert.All(moved.Lines.Where(line => ids.Contains(line.Id)), line => Assert.Equal("considering", line.Zone));
        await using var connection = new NpgsqlConnection(instance.ConnectionString);
        var updates = await connection.ExecuteScalarAsync<int>(
            "select count(*) from content.outbox where event_type = 'decks.deck.updated' and payload ->> 'deckId' = @id", new { id = deck.Id.ToString() });
        Assert.Equal(1, updates);
    }

    [Fact]
    public async Task LinesWithTheSameKeyAfterTheActionBecomeOneLine()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring", quantity = 2 }, new { name = "Sol Ring", zone = "sideboard" }, new { name = "Counterspell", zone = "sideboard" });
        var main = deck.Lines.Single(line => line.Zone == "main");
        var sideboard = deck.Lines.Where(line => line.Zone == "sideboard").Select(line => line.Id).ToList();

        var moved = await GroupAsync(client, deck.Id, new { lineIds = sideboard, action = "move", zone = "main" });

        Assert.Equal(2, moved.Lines.Count);
        var solRing = moved.Lines.Single(line => line.OracleId == main.OracleId);
        Assert.Equal(main.Id, solRing.Id);
        Assert.Equal(3, solRing.Quantity);
        Assert.Equal(0, moved.Counts["sideboard"]);
    }

    [Fact]
    public async Task UserAddsAndRemovesTagsAndCategories()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring", tags = new[] { "Fast mana" } }, new { name = "Cultivate" });
        var ids = deck.Lines.Select(line => line.Id).ToList();

        var tagged = await GroupAsync(client, deck.Id, new { lineIds = ids, action = "tag", tags = new[] { "Combo" }, globalTags = new[] { "Staple" }, categories = new[] { "ramp" } });

        Assert.All(tagged.Lines, line =>
        {
            var names = line.TagIds.Select(id => tagged.Tags[id]).ToList();
            Assert.Contains(names, tag => tag is { Name: "Combo", Global: false });
            Assert.Contains(names, tag => tag is { Name: "Staple", Global: true });
            Assert.Equal(["ramp"], line.Categories);
        });

        var untagged = await GroupAsync(client, deck.Id, new { lineIds = ids, action = "untag", tags = new[] { "combo" }, globalTags = new[] { "Staple" }, categories = new[] { "ramp" } });

        var solRing = untagged.Lines.Single(line => untagged.Cards[line.OracleId].Name == "Sol Ring");
        Assert.Equal(["Fast mana"], solRing.TagIds.Select(id => untagged.Tags[id].Name));
        Assert.Empty(untagged.Lines.Single(line => line.Id != solRing.Id).TagIds);
        Assert.All(untagged.Lines, line => Assert.Empty(line.Categories));
    }

    [Fact]
    public async Task UserClearsThePrintingsAndSetsTheFinish()
    {
        using var client = await instance.NewUserAsync();
        var plain = await CreateAsync(client, new { name = "Sol Ring" });
        var printingId = plain.Cards.Values.Single().DefaultPrintingId!.Value;
        var deck = await CreateAsync(client, new { name = "Sol Ring", printingId, finish = "foil" }, new { name = "Sol Ring", quantity = 2 }, new { name = "Counterspell" });
        var printed = deck.Lines.Single(line => line.PrintingId is not null);

        var cleared = await GroupAsync(client, deck.Id, new { lineIds = new[] { printed.Id }, action = "printing", clearPrinting = true });
        var line = cleared.Lines.Single(line => line.Id == printed.Id);
        Assert.Null(line.PrintingId);
        Assert.Equal("foil", line.Finish);

        var finished = await GroupAsync(client, deck.Id, new { lineIds = cleared.Lines.Select(line => line.Id), action = "printing", finish = "nonfoil" });

        Assert.All(finished.Lines, line => Assert.Equal("nonfoil", line.Finish));
        Assert.Equal(3, finished.Lines.Single(line => finished.Cards[line.OracleId].Name == "Sol Ring").Quantity);
    }

    [Fact]
    public async Task UserChangesTheQuantitiesAndRemovesLines()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Forest", quantity = 3 }, new { name = "Sol Ring" }, new { name = "Counterspell" }, new { name = "Cultivate" });
        var forest = deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Forest");
        var solRing = deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Sol Ring");
        var counterspell = deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Counterspell");

        var added = await GroupAsync(client, deck.Id, new { lineIds = new[] { forest.Id, solRing.Id }, action = "quantity", quantityDelta = 2 });
        Assert.Equal(5, added.Lines.Single(line => line.Id == forest.Id).Quantity);
        Assert.Equal(3, added.Lines.Single(line => line.Id == solRing.Id).Quantity);

        var removed = await GroupAsync(client, deck.Id, new { lineIds = new[] { forest.Id, counterspell.Id }, action = "quantity", quantityDelta = -1 });
        Assert.Equal(4, removed.Lines.Single(line => line.Id == forest.Id).Quantity);
        Assert.DoesNotContain(removed.Lines, line => line.Id == counterspell.Id);

        var left = await GroupAsync(client, deck.Id, new { lineIds = new[] { forest.Id, solRing.Id }, action = "remove" });
        var cultivate = Assert.Single(left.Lines);
        Assert.Equal("Cultivate", left.Cards[cultivate.OracleId].Name);
        Assert.Equal(deck.Version + 3, left.Version);
    }

    [Fact]
    public async Task IncorrectRequestsGiveFieldErrorsAndUnknownLinesGiveNotFound()
    {
        using var client = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring" });
        var lineId = deck.Lines.Single().Id;

        var invalid = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/group", new { lineIds = Array.Empty<Guid>(), action = "burn" }, Cancel);
        var noZone = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/group", new { lineIds = new[] { lineId }, action = "move", zone = "graveyard" }, Cancel);
        var noTags = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/group", new { lineIds = new[] { lineId }, action = "tag", categories = new[] { "magic" } }, Cancel);
        var noFinish = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/group", new { lineIds = new[] { lineId }, action = "printing" }, Cancel);
        var noDelta = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/group", new { lineIds = new[] { lineId }, action = "quantity", quantityDelta = 0 }, Cancel);
        var unknownLine = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/group", new { lineIds = new[] { lineId, Guid.NewGuid() }, action = "remove" }, Cancel);
        var otherUser = await other.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/group", new { lineIds = new[] { lineId }, action = "remove" }, Cancel);
        var oldVersion = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/group", new { lineIds = new[] { lineId }, action = "remove", expectedVersion = deck.Version - 1 }, Cancel);
        var read = await client.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);

        await AssertFieldsAsync(invalid, "lineIds", "action");
        await AssertFieldsAsync(noZone, "zone");
        await AssertFieldsAsync(noTags, "categories[0]");
        await AssertFieldsAsync(noFinish, "finish");
        await AssertFieldsAsync(noDelta, "quantityDelta");
        Assert.Equal(HttpStatusCode.NotFound, unknownLine.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherUser.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, oldVersion.StatusCode);
        Assert.Equal(deck.Version, read!.Version);
        Assert.Single(read.Lines);
    }

    private static async Task AssertFieldsAsync(HttpResponseMessage response, params string[] fields)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var actual = body.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()).ToList();
        foreach (var field in fields)
        {
            Assert.Contains(field, actual);
        }
    }

    private static async Task<DeckResponse> CreateAsync(HttpClient client, params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name = "Group test", lines }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }

    private static async Task<DeckResponse> GroupAsync(HttpClient client, Guid deckId, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/decks/{deckId}/lines/group", body, Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}
