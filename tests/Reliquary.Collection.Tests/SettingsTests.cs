using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Collection.Api;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

/// <summary>
/// The advanced fields: the API ignores a disabled field in a request and never returns it.
/// </summary>
[Collection(CollectionGroup.Name)]
public sealed class SettingsTests(CollectionInstance instance)
{
    [Fact]
    public async Task DisabledFieldsAreIgnoredAndHidden()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");

        var defaults = await client.GetFromJsonAsync<CollectionSettingsResponse>(new Uri("/api/v1/collection/settings", UriKind.Relative), Cancel);
        await client.AddAsync(rack.Id, new { name = "Sol Ring", finish = "foil", condition = "bad value", note = "Signed" });
        var page = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/collection/locations/{rack.Id}/entries", UriKind.Relative), Cancel);
        var entry = page.GetProperty("items")[0];

        Assert.Empty(defaults!.AdvancedFields);
        Assert.False(entry.TryGetProperty("finish", out _));
        Assert.False(entry.TryGetProperty("condition", out _));
        Assert.False(entry.TryGetProperty("note", out _));
        Assert.Equal(1, entry.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task EnabledFieldsAreStoredAndReturned()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Binder", "binder");

        var invalid = await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = new[] { "finish", "color" } }, Cancel);
        var saved = await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = new[] { "note", "finish", "purchasePrice", "acquiredOn", "condition" } }, Cancel);
        var added = await client.AddAsync(
            rack.Id,
            new { name = "Sol Ring", finish = "foil", condition = "near_mint", purchasePrice = 1.5m, purchaseCurrency = "eur", acquiredOn = "2026-10-01", note = "Signed" },
            new { name = "Sol Ring", finish = "nonfoil" });
        var missingCurrency = await client.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/entries", new { entries = new[] { new { name = "Sol Ring", purchasePrice = 2m } } }, Cancel);
        var foil = added.Entries.Single(entry => entry.Finish == "foil");

        Assert.Equal("advancedFields[1]", await invalid.FieldAsync());
        Assert.Equal(["finish", "condition", "purchasePrice", "acquiredOn", "note"], (await saved.Content.ReadFromJsonAsync<CollectionSettingsResponse>(Cancel))!.AdvancedFields);
        Assert.Equal(2, added.Entries.Count);
        Assert.Equal(("near_mint", 1.5m, "EUR", new DateOnly(2026, 10, 1), "Signed"), (foil.Condition, foil.PurchasePrice!.Value, foil.PurchaseCurrency, foil.AcquiredOn!.Value, foil.Note));
        Assert.Equal("entries[0].purchaseCurrency", await missingCurrency.FieldAsync());
    }

    [Fact]
    public async Task DisabledFieldKeepsItsStoredValue()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Box", "box");
        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = new[] { "finish", "note" } }, Cancel);
        var entry = (await client.AddAsync(rack.Id, new { name = "Sol Ring", finish = "foil", note = "Gift from Léa" })).Entries.Single();

        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = new[] { "note" } }, Cancel);
        var hidden = (await client.EntriesAsync(rack.Id)).Single();
        var changed = await client.PatchAsJsonAsync($"/api/v1/collection/entries/{entry.Id}", new { finish = "etched", note = "Gift", quantity = 2 }, Cancel);
        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = new[] { "finish", "note" } }, Cancel);
        var shown = (await client.EntriesAsync(rack.Id)).Single();

        Assert.Null(hidden.Finish);
        Assert.Equal("Gift from Léa", hidden.Note);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(("foil", "Gift", 2), (shown.Finish, shown.Note, shown.Quantity));
    }
}
