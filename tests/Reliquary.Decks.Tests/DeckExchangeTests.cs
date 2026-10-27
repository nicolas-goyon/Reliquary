using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Decks.Api;
using Reliquary.Decks.Text;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class DeckExchangeTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>
    /// The gate of milestone M1: import a Moxfield deck, edit it, and export it again. The quantities, printings, zones and tags do not change.
    /// </summary>
    [Fact]
    public async Task MoxfieldRoundTripLosesNoData()
    {
        using var client = await instance.NewUserAsync();
        var printings = await PrintingsAsync(client, "Atraxa, Praetors' Voice", "Sol Ring", "Counterspell", "Fire // Ice");
        var moxfield = $"""
            Commander
            1 Atraxa, Praetors' Voice {printings["Atraxa, Praetors' Voice"]} *F*

            Mainboard
            1 Sol Ring {printings["Sol Ring"]} #Ramp #!Mana rock
            1 Counterspell {printings["Counterspell"]} #Interaction
            1 Fire // Ice {printings["Fire // Ice"]} *E* #Interaction
            1 Lightning Bolt
            30 Forest

            Sideboard
            1 Swords to Plowshares #Removal

            Maybeboard
            1 Cultivate #Ramp
            """;

        var import = await client.PostAsJsonAsync("/api/v1/decks/import", new { format = "moxfield", content = moxfield, name = "Atraxa" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, import.StatusCode);
        var deck = (await import.Content.ReadFromJsonAsync<ImportDeckResponse>(Cancel))!;
        Assert.Empty(deck.Warnings);
        var before = await ExportAsync(client, deck.Deck.Id);

        // Edit: one more card, and a new quantity of the forests.
        var forest = deck.Deck.Lines.Single(line => deck.Deck.Cards[line.OracleId].Name == "Forest");
        await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Deck.Id}/lines/{forest.Id}", new { quantity = 29 }, Cancel);
        await client.PostAsJsonAsync($"/api/v1/decks/{deck.Deck.Id}/lines", new { lines = new[] { new { name = "Llanowar Elves", tags = new[] { "Ramp" } } } }, Cancel);
        var after = await ExportAsync(client, deck.Deck.Id);

        var original = Lines(DeckText.Parse(moxfield));
        var first = Lines(DeckText.Parse(before));
        var second = Lines(DeckText.Parse(after));
        Assert.Equal(original, first);
        Assert.Equal(
            first.Where(line => !line.StartsWith("30|Forest", StringComparison.Ordinal)).Append("1|Llanowar Elves||||Ramp||main").Order(StringComparer.Ordinal),
            second.Where(line => !line.StartsWith("29|Forest", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Contains("29|Forest||||||main", second);
    }

    [Theory]
    [InlineData("mtga", 6, "Commander\n1 Atraxa, Praetors' Voice\n\nDeck\n1 Sol Ring\n4 Forest\n\nSideboard\n1 Counterspell\n")]
    [InlineData("mtgo", 5, "1 Sol Ring\n4 Forest\n\n1 Counterspell\n")]
    [InlineData("archidekt", 6, "1x Atraxa, Praetors' Voice [Commander{top}]\n1x Sol Ring [Ramp]\n4x Forest\n1x Counterspell [Sideboard]\n")]
    [InlineData("dek", 5, "<Deck><Cards Quantity=\"1\" Sideboard=\"false\" Name=\"Sol Ring\" /><Cards Quantity=\"4\" Sideboard=\"false\" Name=\"Forest\" /><Cards Quantity=\"1\" Sideboard=\"true\" Name=\"Counterspell\" /></Deck>")]
    public async Task EachFormatImportsAndExports(string format, int deckCards, string content)
    {
        using var client = await instance.NewUserAsync();

        var import = await client.PostAsJsonAsync("/api/v1/decks/import", new { format, content, name = format, deckFormat = "freeform" }, Cancel);
        var deck = (await import.Content.ReadFromJsonAsync<ImportDeckResponse>(Cancel))!.Deck;
        var export = await client.GetFromJsonAsync<ExportDeckResponse>(new Uri($"/api/v1/decks/{deck.Id}/export?format={format}", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Created, import.StatusCode);
        Assert.Equal(deckCards, deck.Counts["main"] + deck.Counts["commander"]);
        Assert.Equal(1, deck.Counts["sideboard"]);
        Assert.Contains("Sol Ring", export!.Content, StringComparison.Ordinal);
        Assert.StartsWith(format, export.FileName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("deckbox", "Count,Name,Edition Code,Card Number,Foil\n1,Sol Ring,,,foil\n4,Forest,,,\n")]
    [InlineData("manabox", "Name,Set code,Collector number,Foil,Quantity\nSol Ring,,,foil,1\nForest,,,normal,4\n")]
    [InlineData("dragonshield", "\"sep=,\"\nFolder Name,Quantity,Card Name,Set Code,Card Number,Printing\nDeck,1,Sol Ring,,,Foil\nDeck,4,Forest,,,Normal\n")]
    [InlineData("csv", "Quantity,Name,Finish,Zone\n1,Sol Ring,foil,main\n4,Forest,,main\n")]
    public async Task EachCsvFormatImportsAndExports(string format, string content)
    {
        using var client = await instance.NewUserAsync();

        var import = await client.PostAsJsonAsync("/api/v1/decks/import", new { format, content, name = format, deckFormat = "freeform" }, Cancel);
        var deck = (await import.Content.ReadFromJsonAsync<ImportDeckResponse>(Cancel))!.Deck;
        var export = await client.GetFromJsonAsync<ExportDeckResponse>(new Uri($"/api/v1/decks/{deck.Id}/export?format={format}", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Created, import.StatusCode);
        Assert.Equal(5, deck.Counts["main"]);
        Assert.Equal("foil", deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Sol Ring").Finish);
        Assert.Equal(("text/csv", $"{format}.csv"), (export!.ContentType, export.FileName));
        Assert.Contains("Sol Ring", export.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ColumnMapIsOnlyForTheGenericCsv()
    {
        using var client = await instance.NewUserAsync();

        var mapped = await client.PostAsJsonAsync("/api/v1/decks/import", new { format = "csv", content = "Card;Qty\nSol Ring;1\n", columns = new { name = "Card" } }, Cancel);
        var refused = await client.PostAsJsonAsync("/api/v1/decks/import", new { format = "manabox", content = "Name\nSol Ring\n", columns = new { name = "Card" } }, Cancel);
        var unknown = await client.PostAsJsonAsync("/api/v1/decks/import", new { format = "csv", content = "Card\nSol Ring\n", columns = new { price = "Card" } }, Cancel);

        Assert.Equal(HttpStatusCode.Created, mapped.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("\"columns\"", await refused.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Contains("columns.price", await unknown.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportReportsEachLineAndMakesNoDeck()
    {
        using var client = await instance.NewUserAsync();

        var import = await client.PostAsJsonAsync("/api/v1/decks/import", new { format = "moxfield", content = "1 Sol Ring\n1 No such card\n1 Counterspell (zzz) 999\n" }, Cancel);
        var problem = await import.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var decks = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/decks", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, import.StatusCode);
        Assert.Equal(["2 card_not_found"], problem.GetProperty("lineErrors").EnumerateArray().Select(error => $"{error.GetProperty("line").GetInt32()} {error.GetProperty("code").GetString()}"));
        Assert.Equal(0, decks.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task UnknownPrintingStaysWithAWarning()
    {
        using var client = await instance.NewUserAsync();

        var import = await client.PostAsJsonAsync("/api/v1/decks/import", new { format = "mtga", content = "Deck\n1 Counterspell (XYZ) 999\n" }, Cancel);
        var result = (await import.Content.ReadFromJsonAsync<ImportDeckResponse>(Cancel))!;

        Assert.Equal(HttpStatusCode.Created, import.StatusCode);
        Assert.Null(Assert.Single(result.Deck.Lines).PrintingId);
        Assert.Equal((2, "printing_not_found"), (Assert.Single(result.Warnings).Line, result.Warnings[0].Code));
    }

    private static async Task<string> ExportAsync(HttpClient client, Guid deckId) =>
        (await client.GetFromJsonAsync<ExportDeckResponse>(new Uri($"/api/v1/decks/{deckId}/export?format=moxfield", UriKind.Relative), Cancel))!.Content;

    /// <summary>
    /// Gets the set code and the collector number of the default printing of each card, from a temporary deck.
    /// </summary>
    private static async Task<Dictionary<string, string>> PrintingsAsync(HttpClient client, params string[] names)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name = "Printings", lines = names.Select(name => new { name }) }, Cancel);
        var deck = (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
        await client.DeleteAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        return deck.Cards.Values.ToDictionary(card => card.Name, card =>
        {
            var printing = deck.Printings[card.DefaultPrintingId!.Value];
            return $"({printing.SetCode.ToUpperInvariant()}) {printing.CollectorNumber}";
        });
    }

    /// <summary>The comparable content of each line: quantity, name, printing, finish, tags and zone.</summary>
    private static List<string> Lines(DeckTextResult result)
    {
        Assert.Empty(result.Errors);
        return [.. result.Lines
            .Select(line => $"{line.Quantity}|{line.Name}|{line.SetCode}|{line.CollectorNumber}|{line.Finish}|{string.Join(',', line.Tags.Order(StringComparer.Ordinal))}|{string.Join(',', line.GlobalTags)}|{line.Zone}")
            .Order(StringComparer.Ordinal)];
    }
}
