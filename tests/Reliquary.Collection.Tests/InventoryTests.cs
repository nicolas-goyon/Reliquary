using System.Net;
using System.Net.Http.Json;
using Reliquary.Collection.Api;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

/// <summary>
/// The inventories: the differences with the counted content, the answers, the movements and the history.
/// </summary>
[Collection(CollectionGroup.Name)]
public sealed class InventoryTests(CollectionInstance instance)
{
    private static async Task<InventoryResponse> StartAsync(HttpClient client, Guid locationId, params object[] entries)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/collection/locations/{locationId}/inventories", new { entries }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<InventoryResponse>(Cancel))!;
    }

    [Fact]
    public async Task InventoryWritesTheMovementsOfTheDifferencesAndKeepsTheContent()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var binder = await client.CreateLocationAsync("Binder", "binder");
        await client.AddAsync(rack.Id, new { name = "Sol Ring", quantity = 2 }, new { name = "Forest", quantity = 10 }, new { name = "Counterspell" });
        await client.AddAsync(binder.Id, new { name = "Sol Ring", quantity = 1 });

        var inventory = await StartAsync(client, rack.Id, new { name = "Sol Ring", quantity = 3 }, new { name = "Forest", quantity = 8 }, new { name = "Lightning Bolt" });
        var solRing = inventory.Differences.Single(difference => difference.Card.Name == "Sol Ring");
        var counterspell = inventory.Differences.Single(difference => difference.Card.Name == "Counterspell");
        var missing = await client.PostAsJsonAsync($"/api/v1/collection/inventories/{inventory.Id}/apply", new { answers = new[] { new { key = solRing.Key, kind = "location", locationId = binder.Id } } }, Cancel);
        var applied = await client.PostAsJsonAsync(
            $"/api/v1/collection/inventories/{inventory.Id}/apply",
            new
            {
                answers = new object[] { new { key = solRing.Key, kind = "location", locationId = binder.Id }, new { key = counterspell.Key, kind = "sale" } },
                origin = new { kind = "purchase" },
                destination = new { kind = "nowhere" },
            },
            Cancel);
        var result = (await applied.Content.ReadFromJsonAsync<InventoryResponse>(Cancel))!;
        var again = await client.PostAsJsonAsync($"/api/v1/collection/inventories/{inventory.Id}/apply", new { origin = new { kind = "purchase" } }, Cancel);
        await client.AddAsync(rack.Id, new { name = "Swords to Plowshares" });
        var history = await client.GetFromJsonAsync<List<InventorySummaryResponse>>(new Uri($"/api/v1/collection/locations/{rack.Id}/inventories", UriKind.Relative), Cancel);
        var frozen = await client.GetFromJsonAsync<InventoryResponse>(new Uri($"/api/v1/collection/inventories/{inventory.Id}", UriKind.Relative), Cancel);

        Assert.Equal(
            [("Counterspell", -1, 1, 0), ("Forest", -2, 10, 8), ("Lightning Bolt", 1, 0, 1), ("Sol Ring", 1, 2, 3)],
            inventory.Differences.Select(difference => (difference.Card.Name, difference.Card.Quantity, difference.Expected, difference.Counted)).ToList());
        Assert.Equal("answers", await missing.FieldAsync());
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal("applied", result.Status);
        Assert.Empty(result.Differences);
        Assert.Equal("inventory_applied", await again.CodeAsync());
        Assert.Equal(
            [("Forest", 8), ("Lightning Bolt", 1), ("Sol Ring", 3), ("Swords to Plowshares", 1)],
            (await client.EntriesAsync(rack.Id)).Select(entry => (entry.Name, entry.Quantity)).ToList());
        Assert.Empty(await client.EntriesAsync(binder.Id));
        Assert.Equal(4, await instance.CountAsync("movements", ownerId, $"inventory_id = '{inventory.Id}'"));
        Assert.Equal(1, await instance.CountAsync("movements", ownerId, $"inventory_id = '{inventory.Id}' and kind = 'move' and origin_location_id = '{binder.Id}'"));
        Assert.Equal(1, await instance.CountAsync("movements", ownerId, $"inventory_id = '{inventory.Id}' and destination_kind = 'sale'"));
        Assert.Equal(("applied", 12L), (Assert.Single(history!).Status, history![0].CardCount));
        Assert.Equal(["Forest", "Lightning Bolt", "Sol Ring"], frozen!.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task LockedLocationRefusesTheInventoryWithDifferences()
    {
        using var client = await instance.NewUserAsync();
        var cube = await client.CreateLocationAsync("Cube", "cube");
        await client.AddAsync(cube.Id, new { name = "Sol Ring" });
        await client.PostAsync(new Uri($"/api/v1/collection/locations/{cube.Id}/lock", UriKind.Relative), null, Cancel);

        var same = await StartAsync(client, cube.Id, new { name = "Sol Ring" });
        var applySame = await client.PostAsJsonAsync($"/api/v1/collection/inventories/{same.Id}/apply", new { }, Cancel);
        var empty = await StartAsync(client, cube.Id);
        var applyEmpty = await client.PostAsJsonAsync($"/api/v1/collection/inventories/{empty.Id}/apply", new { destination = new { kind = "nowhere" } }, Cancel);
        var cancelled = await client.DeleteAsync(new Uri($"/api/v1/collection/inventories/{empty.Id}", UriKind.Relative), Cancel);
        var history = await client.GetFromJsonAsync<List<InventorySummaryResponse>>(new Uri($"/api/v1/collection/locations/{cube.Id}/inventories", UriKind.Relative), Cancel);

        Assert.Empty(same.Differences);
        Assert.Equal(HttpStatusCode.OK, applySame.StatusCode);
        Assert.Equal(-1, Assert.Single(empty.Differences).Card.Quantity);
        Assert.Equal("location_locked", await applyEmpty.CodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal("applied", Assert.Single(history!).Status);
        Assert.Single(await client.EntriesAsync(cube.Id));
    }
}
