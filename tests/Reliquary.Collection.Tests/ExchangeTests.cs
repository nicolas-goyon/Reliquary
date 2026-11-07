using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Collection.Api;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

[Collection(CollectionGroup.Name)]
public sealed class ExchangeTests(CollectionInstance instance)
{
    private static readonly string[] AllFields = ["finish", "condition", "purchasePrice", "acquiredOn", "note"];

    /// <summary>
    /// The example file of each format, and the entries that the import of the file must make.
    /// Each entry is "quantity|name|set|number|language|finish|condition|price|currency|date|note".
    /// </summary>
    public static TheoryData<string, string, string[]> Files { get; } = new()
    {
        {
            "text", "text.txt",
            ["4|Sol Ring|slz|114|en||||||", "1|Counterspell|dmr|45|en|foil|||||", "10|Forest|||en||||||"]
        },
        {
            "csv", "generic.csv",
            ["2|Sol Ring|ltc|284|fr|nonfoil|near_mint|1.50|EUR|2026-10-01|From a friend", "1|Lightning Bolt|2x2|361|fr|foil|excellent|0.80|EUR|2026-10-02|", "3|Forest|||en||||||"]
        },
        {
            "dek", "deck.dek",
            ["1|Sol Ring|||en||||||", "20|Forest|||en||||||", "2|Counterspell|||en||||||"]
        },
        {
            "mtga", "mtga.txt",
            ["1|Atraxa, Praetors' Voice|mul|163|en||||||", "1|Swords to Plowshares|fic|256|en||||||", "1|Cultivate|afc|155|en||||||"]
        },
        {
            "mtgo", "mtgo.txt",
            ["1|Sol Ring|||en||||||", "4|Lightning Bolt|||en||||||", "2|Counterspell|||en||||||"]
        },
        {
            "moxfield", "moxfield.csv",
            ["2|Sol Ring|ltc|284|fr||near_mint|1.20|USD||", "1|Counterspell|dmr|45|en|foil|light_played||||", "1|Swords to Plowshares|fic|256|ja||poor||||"]
        },
        {
            "archidekt", "archidekt.txt",
            ["1|Sol Ring|slz|114|en||||||", "1|Cultivate|fic|300|en|foil|||||", "2|Forest|blb|280|en||||||"]
        },
        {
            "deckbox", "deckbox.csv",
            ["3|Lightning Bolt|2x2|361|fr|foil|near_mint||||", "1|Sol Ring|slz|114|en||good||||"]
        },
        {
            "manabox", "manabox.csv",
            ["4|Forest|dmu|276|fr|foil|near_mint|0.10|EUR||", "1|Cultivate|fic|300|en|nonfoil|light_played|0.25|EUR||", "1|Counterspell|dmr|281|ja|nonfoil|mint||||"]
        },
        {
            "dragonshield", "dragonshield.csv",
            ["1|Atraxa, Praetors' Voice|mul|33|fr|foil|near_mint|12.00|USD|2026-09-15|", "2|Swords to Plowshares|fic|256|en|nonfoil|excellent|1.00|USD|2026-09-20|"]
        },
    };

    [Theory]
    [MemberData(nameof(Files))]
    public async Task ImportsTheExampleFileOfEachFormat(string format, string file, string[] expected)
    {
        using var client = await instance.NewUserAsync();
        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = AllFields }, Cancel);
        var binder = await client.CreateLocationAsync("Binder", "binder");
        var content = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ImportFiles", file), Cancel);

        var result = await ImportAsync(client, binder.Id, new { format, content });
        var entries = await client.EntriesAsync(binder.Id);

        Assert.Equal(expected.Order(StringComparer.Ordinal), entries.Select(Describe).Order(StringComparer.Ordinal));
        Assert.Equal(expected.Length, result.LineCount);
        Assert.Empty(result.Warnings);
        if (format == "text")
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal((5, "card_not_found", "1 No Such Card"), (error.Line, error.Code, error.Text));
        }
        else
        {
            Assert.Empty(result.Errors);
        }
    }

    [Fact]
    public async Task ImportReportsEachLineThatItCannotRead()
    {
        using var client = await instance.NewUserAsync();
        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = AllFields }, Cancel);
        var rack = await client.CreateLocationAsync("Rack", "rack");
        const string csv = "Quantity,Name,Language,Condition,Purchase price,Entry date\n"
            + "1,Sol Ring,fr,mint,1.00,2026-10-01\n"
            + "1,Forest,Klingon,,,\n"
            + "1,Forest,,Very bad,,\n"
            + "1,Forest,,,cheap,\n"
            + "1,Forest,,,,yesterday\n"
            + "0,Forest,,,,\n"
            + ",,fr,,,\n";

        var text = await ImportAsync(client, rack.Id, new { format = "text", content = "1 Counterspell\n\n0 Forest\n1 Unknown Card\n1 Sol Ring (XYZ) 1\n" });
        var table = await ImportAsync(client, rack.Id, new { format = "csv", content = csv });
        var noColumn = await ImportAsync(client, rack.Id, new { format = "csv", content = "Card title,Count\nSol Ring,1\n" });
        var badXml = await ImportAsync(client, rack.Id, new { format = "dek", content = "<Deck>\n<Cards Quantity=\"1\"" });

        Assert.Equal([(3, "quantity_out_of_range"), (4, "card_not_found")], text.Errors.Select(error => (error.Line, error.Code)));
        Assert.Equal((5, "printing_not_found"), (Assert.Single(text.Warnings).Line, text.Warnings[0].Code));
        Assert.Equal((2, 2), (text.LineCount, text.CardCount));
        Assert.Equal(
            [(3, "unknown_language"), (4, "unknown_condition"), (5, "invalid_price"), (6, "invalid_date"), (7, "quantity_out_of_range"), (8, "missing_name")],
            table.Errors.Select(error => (error.Line, error.Code)));
        Assert.Equal("1,Forest,Klingon,,,", table.Errors[0].Text);
        Assert.Equal(1, table.CardCount);
        Assert.Equal((1, "missing_column", null as Guid?), (Assert.Single(noColumn.Errors).Line, noColumn.Errors[0].Code, noColumn.BatchId));
        Assert.Equal("invalid_file", Assert.Single(badXml.Errors).Code);
        var entries = await client.EntriesAsync(rack.Id);
        Assert.Equal(["1|Counterspell|||en||||||", "1|Sol Ring|||en||||||", "1|Sol Ring|||fr||mint|1.00|USD|2026-10-01|"], entries.Select(Describe).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ImportIgnoresTheDisabledFieldsAndWritesMovements()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        const string csv = "Name,Quantity,Condition,Foil,Purchase price,Note\nSol Ring,3,Very bad,foil,cheap,A note\n";

        var result = await ImportAsync(client, rack.Id, new { format = "csv", content = csv, origin = "purchase" });

        Assert.Empty(result.Errors);
        Assert.Equal("3|Sol Ring|||en||||||", Describe(Assert.Single(await client.EntriesAsync(rack.Id))));
        Assert.Equal(1, await instance.CountAsync("movements", ownerId, $"origin_kind = 'purchase' and quantity = 3 and batch_id = '{result.BatchId}'"));
    }

    [Fact]
    public async Task ImportChecksTheRequestAndTheLocation()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var locked = await client.CreateLocationAsync("Locked", "rack");
        (await client.PostAsync(new Uri($"/api/v1/collection/locations/{locked.Id}/lock", UriKind.Relative), null, Cancel)).EnsureSuccessStatusCode();

        var format = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/import", new { format = "pdf", content = "1 Sol Ring" }, Cancel);
        var origin = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/import", new { format = "text", content = "1 Sol Ring", origin = "sale" }, Cancel);
        var currency = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/import", new { format = "text", content = "1 Sol Ring", currency = "euro" }, Cancel);
        var lockedImport = await client.PostAsJsonAsync($"/api/v1/collection/locations/{locked.Id}/import", new { format = "text", content = "1 Sol Ring" }, Cancel);
        var missing = await client.PostAsJsonAsync($"/api/v1/collection/locations/{Guid.NewGuid()}/import", new { format = "text", content = "1 Sol Ring" }, Cancel);

        Assert.Equal("format", await format.FieldAsync());
        Assert.Equal("origin", await origin.FieldAsync());
        Assert.Equal("currency", await currency.FieldAsync());
        Assert.Equal(HttpStatusCode.Conflict, lockedImport.StatusCode);
        Assert.Equal("location_locked", await lockedImport.CodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(await client.EntriesAsync(rack.Id));
    }

    [Fact]
    public async Task ExportOfALocationWithItsSectionsImportsAgainWithNoLoss()
    {
        using var client = await instance.NewUserAsync();
        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = AllFields }, Cancel);
        var rack = await client.CreateLocationAsync("Rack 1", "rack");
        var box = await client.CreateLocationAsync("Box A", null, rack.Id);
        var other = await client.CreateLocationAsync("Other", "box");
        var frenchSolRing = await instance.PrintingAsync("ltc", "284");
        await client.AddAsync(
            rack.Id,
            new { name = "Sol Ring", printingId = frenchSolRing.PrintingId, quantity = 2, condition = "good", purchasePrice = 1.5m, purchaseCurrency = "EUR", acquiredOn = "2026-10-01", note = "Gift, from \"Ann\"" },
            new { name = "Forest", quantity = 10 });
        await client.AddAsync(box.Id, new { name = "Counterspell", language = "ja", finish = "foil" });
        await client.AddAsync(other.Id, new { name = "Lightning Bolt" });

        var own = await ExportAsync(client, $"locations/{rack.Id}/export?format=csv");
        var withSections = await ExportAsync(client, $"locations/{rack.Id}/export?format=csv&sections=true");
        var copy = await client.CreateLocationAsync("Copy", "rack");
        var result = await ImportAsync(client, copy.Id, new { format = "csv", content = withSections.Content });

        Assert.Equal(("Rack 1.csv", "text/csv"), (own.FileName, own.ContentType));
        Assert.DoesNotContain("Counterspell", own.Content, StringComparison.Ordinal);
        Assert.Contains("Rack 1 / Box A", withSections.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Lightning Bolt", withSections.Content, StringComparison.Ordinal);
        Assert.Empty(result.Errors);
        var original = (await client.EntriesAsync(rack.Id, "&sections=true")).Select(Describe).Order(StringComparer.Ordinal);
        var imported = (await client.EntriesAsync(copy.Id)).Select(Describe).Order(StringComparer.Ordinal);
        Assert.Equal(original, imported);
    }

    [Fact]
    public async Task ExportOfAllTheCollectionGivesEachFormat()
    {
        using var client = await instance.NewUserAsync();
        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = AllFields }, Cancel);
        var rack = await client.CreateLocationAsync("Rack 1", "rack");
        var box = await client.CreateLocationAsync("Box A", null, rack.Id);
        var binder = await client.CreateLocationAsync("Binder", "binder");
        await client.AddAsync(rack.Id, new { name = "Sol Ring", quantity = 2 }, new { name = "Counterspell", printingId = (await instance.PrintingAsync("dmr", "45")).PrintingId, finish = "foil" });
        await client.AddAsync(box.Id, new { name = "Sol Ring", condition = "played" });
        await client.AddAsync(binder.Id, new { name = "Forest", quantity = 3, language = "fr" });

        var text = await ExportAsync(client, "export?format=text");
        var mtgo = await ExportAsync(client, "export?format=mtgo");
        var dek = await ExportAsync(client, "export?format=dek");
        var mtga = await ExportAsync(client, "export?format=mtga");
        var archidekt = await ExportAsync(client, "export?format=archidekt");
        var moxfield = await ExportAsync(client, "export?format=moxfield");
        var deckbox = await ExportAsync(client, "export?format=deckbox");
        var manabox = await ExportAsync(client, "export?format=manabox");
        var dragonShield = await ExportAsync(client, "export?format=dragonshield");
        var unknown = await client.GetAsync(new Uri("/api/v1/collection/export?format=pdf", UriKind.Relative), Cancel);

        Assert.Equal("// Binder\n3 Forest\n\n// Rack 1\n1 Counterspell (DMR) 45 *F*\n2 Sol Ring\n\n// Rack 1 / Box A\n1 Sol Ring\n", text.Content);
        Assert.Equal(("collection.txt", "3 Forest\n1 Counterspell\n3 Sol Ring\n"), (mtgo.FileName, mtgo.Content));
        Assert.Contains("Quantity=\"3\" Sideboard=\"false\" Name=\"Sol Ring\"", dek.Content, StringComparison.Ordinal);
        Assert.Equal("collection.dek", dek.FileName);
        Assert.Equal("Deck\n3 Forest\n1 Counterspell (DMR) 45\n3 Sol Ring\n", mtga.Content);
        Assert.Equal("3x Forest\n1x Counterspell (dmr) 45 *F*\n3x Sol Ring\n", archidekt.Content);
        Assert.Contains("1,0,Sol Ring,,Moderately Played,English,,,,,False,False,", moxfield.Content, StringComparison.Ordinal);
        Assert.Contains("3,0,Forest,,,,French,", deckbox.Content, StringComparison.Ordinal);
        Assert.Contains("Counterspell,DMR,45,foil,1,,,en,", manabox.Content, StringComparison.Ordinal);
        Assert.StartsWith("sep=,\nFolder Name,", dragonShield.Content, StringComparison.Ordinal);
        Assert.Contains("Rack 1 / Box A,1,0,Sol Ring,,,,Played,Normal,English,,", dragonShield.Content, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        foreach (var (format, file) in new[] { ("moxfield", moxfield), ("deckbox", deckbox), ("manabox", manabox), ("dragonshield", dragonShield) })
        {
            var copy = await client.CreateLocationAsync($"Copy {format}", "rack");
            var result = await ImportAsync(client, copy.Id, new { format, content = file.Content });
            Assert.Empty(result.Errors);
            Assert.Equal(7, result.CardCount);
        }
    }

    private static async Task<ImportEntriesResponse> ImportAsync(HttpClient client, Guid locationId, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/collection/locations/{locationId}/import", body, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<ImportEntriesResponse>(Cancel))!;
    }

    private static async Task<ExportCollectionResponse> ExportAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri($"/api/v1/collection/{path}", UriKind.Relative), Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<ExportCollectionResponse>(JsonSerializerOptions.Web, Cancel))!;
    }

    private static string Describe(EntryResponse entry) => string.Join(
        '|',
        entry.Quantity.ToString(CultureInfo.InvariantCulture),
        entry.Name,
        entry.SetCode,
        entry.CollectorNumber,
        entry.Language,
        entry.Finish,
        entry.Condition,
        entry.PurchasePrice?.ToString("0.00", CultureInfo.InvariantCulture),
        entry.PurchaseCurrency,
        entry.AcquiredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        entry.Note);
}
