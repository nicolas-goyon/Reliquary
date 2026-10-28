using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Collection.Api;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

[Collection(CollectionGroup.Name)]
public sealed class EntryTests(CollectionInstance instance)
{
    [Fact]
    public async Task SameCardAndFieldsMergeIntoOneEntry()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var printing = await instance.PrintingAsync("ltc", "284");

        await client.AddAsync(rack.Id, new { name = "Sol Ring", quantity = 2 }, new { name = "Sol Ring" });
        await client.AddAsync(rack.Id, new { name = "Sol Ring", language = "ja" });
        await client.AddAsync(rack.Id, new { name = "Sol Ring", printingId = printing.PrintingId });
        await client.AddAsync(rack.Id, new { name = "Sol Ring", printingId = printing.PrintingId, quantity = 4 });
        var entries = await client.EntriesAsync(rack.Id);

        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, entry => entry.PrintingId is null && entry.Language == "en" && entry.Quantity == 3);
        Assert.Contains(entries, entry => entry.PrintingId is null && entry.Language == "ja" && entry.Quantity == 1);
        var withPrinting = Assert.Single(entries, entry => entry.PrintingId == printing.PrintingId);
        Assert.Equal(("fr", 5, "ltc", "284"), (withPrinting.Language, withPrinting.Quantity, withPrinting.SetCode, withPrinting.CollectorNumber));
        Assert.All(entries, entry => Assert.Equal("Sol Ring", entry.Name));
    }

    [Fact]
    public async Task PrintingMustBeAPrintingOfTheCard()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var forest = await instance.PrintingAsync("blb", "280");
        var solRing = await instance.OracleIdAsync("Sol Ring");
        var frenchSolRing = await instance.PrintingAsync("ltc", "284");
        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = new[] { "finish" } }, Cancel);

        var wrong = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/entries", new { entries = new[] { new { oracleId = solRing, printingId = forest.PrintingId } } }, Cancel);
        var language = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/entries", new { entries = new object[] { new { oracleId = solRing, printingId = frenchSolRing.PrintingId, language = "en" } } }, Cancel);
        var unknown = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/entries", new { entries = new[] { new { name = "No such card" } } }, Cancel);
        var finish = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/entries", new { entries = new[] { new { oracleId = solRing, printingId = frenchSolRing.PrintingId, finish = "foil" } } }, Cancel);
        var quantity = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/entries", new { entries = new[] { new { oracleId = solRing, quantity = 0 } } }, Cancel);
        var origin = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/entries", new { entries = new[] { new { oracleId = solRing } }, origin = "sale" }, Cancel);

        Assert.Equal("entries[0].printingId", await wrong.FieldAsync());
        Assert.Equal("entries[0].language", await language.FieldAsync());
        Assert.Equal("entries[0].name", await unknown.FieldAsync());
        Assert.Equal("entries[0].finish", await finish.FieldAsync());
        Assert.Equal("entries[0].quantity", await quantity.FieldAsync());
        Assert.Equal("origin", await origin.FieldAsync());
        Assert.Empty(await client.EntriesAsync(rack.Id));
    }

    [Fact]
    public async Task ChangesOfAnEntryWriteMovements()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var printing = await instance.PrintingAsync("slz", "114");
        var added = await client.AddAsync(rack.Id, new { name = "Sol Ring", quantity = 2 });
        var entry = Assert.Single(added.Entries);

        var more = await client.PatchAsJsonAsync($"/api/v1/collection/entries/{entry.Id}", new { quantity = 5 }, Cancel);
        var fewer = await client.PatchAsJsonAsync($"/api/v1/collection/entries/{entry.Id}", new { quantity = 4 }, Cancel);
        var printed = await client.PatchAsJsonAsync($"/api/v1/collection/entries/{entry.Id}", new { printingId = printing.PrintingId }, Cancel);
        var moved = (await printed.Content.ReadFromJsonAsync<EntriesChangeResponse>(Cancel))!.Entries.Single();
        var removed = await client.DeleteAsync(new Uri($"/api/v1/collection/entries/{moved.Id}?destination=sale", UriKind.Relative), Cancel);

        Assert.Equal(5, (await more.Content.ReadFromJsonAsync<EntriesChangeResponse>(Cancel))!.Entries.Single().Quantity);
        Assert.Equal(4, (await fewer.Content.ReadFromJsonAsync<EntriesChangeResponse>(Cancel))!.Entries.Single().Quantity);
        Assert.Equal((4, "slz"), (moved.Quantity, moved.SetCode));
        Assert.NotEqual(entry.Id, moved.Id);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(await client.EntriesAsync(rack.Id));
        Assert.Equal(3, await instance.CountAsync("movements", ownerId, "kind = 'add'"));
        Assert.Equal(3, await instance.CountAsync("movements", ownerId, "kind = 'remove'"));
        Assert.Equal(1, await instance.CountAsync("movements", ownerId, "destination_kind = 'sale' and quantity = 4"));
    }

    [Fact]
    public async Task LocationPageListsTheSectionsWithPaging()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var box = await client.CreateLocationAsync("Box", null, rack.Id);
        await client.AddAsync(rack.Id, new { name = "Sol Ring" }, new { name = "Counterspell" });
        await client.AddAsync(box.Id, new { name = "Forest", quantity = 20 }, new { name = "Lightning Bolt" });

        var own = await client.EntriesAsync(rack.Id);
        var all = await client.EntriesAsync(rack.Id, "&sections=true");
        var search = await client.EntriesAsync(rack.Id, "&sections=true&q=bolt");
        var first = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/collection/locations/{rack.Id}/entries?sections=true&limit=3", UriKind.Relative), Cancel);
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/collection/locations/{rack.Id}/entries?sections=true&limit=3&cursor={cursor}", UriKind.Relative), Cancel);

        Assert.Equal(["Counterspell", "Sol Ring"], own.Select(entry => entry.Name));
        Assert.Equal(["Counterspell", "Forest", "Lightning Bolt", "Sol Ring"], all.Select(entry => entry.Name));
        Assert.Equal("Lightning Bolt", Assert.Single(search).Name);
        Assert.Equal(3, first.GetProperty("items").GetArrayLength());
        Assert.Equal("Sol Ring", Assert.Single(second.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.All(all, entry => Assert.NotNull(entry.ImageUri));
    }
}
