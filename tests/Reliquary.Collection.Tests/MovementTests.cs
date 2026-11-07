using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Collection.Api;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

/// <summary>
/// The movements: add, remove and move cards in one request, and the history.
/// </summary>
[Collection(CollectionGroup.Name)]
public sealed class MovementTests(CollectionInstance instance)
{
    private static async Task<HttpResponseMessage> MoveAsync(HttpClient client, object origin, object destination, params object[] cards) =>
        await client.PostAsJsonAsync("/api/v1/collection/movements", new { origin, destination, cards }, Cancel);

    [Fact]
    public async Task MoveOfTenCardsIsOneBatchAndKeepsTheCardFields()
    {
        using var client = await instance.NewUserAsync();
        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = new[] { "finish", "condition" } }, Cancel);
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var binder = await client.CreateLocationAsync("Binder", "binder");
        var added = await client.AddAsync(
            rack.Id,
            new { name = "Sol Ring", finish = "foil", condition = "near_mint", quantity = 3 },
            new { name = "Forest", quantity = 8 },
            new { name = "Counterspell" });
        var solRing = added.Entries.Single(entry => entry.Name == "Sol Ring");
        var forest = added.Entries.Single(entry => entry.Name == "Forest");
        await client.AddAsync(binder.Id, new { name = "Sol Ring", finish = "foil", condition = "near_mint" });

        var response = await MoveAsync(
            client,
            new { kind = "location", locationId = rack.Id },
            new { kind = "location", locationId = binder.Id },
            new { entryId = solRing.Id, quantity = 2 },
            new { entryId = forest.Id });
        var batch = (await response.Content.ReadFromJsonAsync<MovementBatchResponse>(Cancel))!;
        var inRack = await client.EntriesAsync(rack.Id);
        var inBinder = await client.EntriesAsync(binder.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, batch.Movements.Count);
        Assert.All(batch.Movements, movement => Assert.Equal(("move", batch.BatchId, "Rack", "Binder"), (movement.Kind, movement.BatchId, movement.Origin.LocationName, movement.Destination.LocationName)));
        Assert.Equal(10, batch.Movements.Sum(movement => movement.Quantity));
        Assert.Equal(["Counterspell", "Sol Ring"], inRack.Select(entry => entry.Name));
        var moved = inBinder.Single(entry => entry.Name == "Sol Ring");
        Assert.Equal((3, "foil", "near_mint"), (moved.Quantity, moved.Finish, moved.Condition));
        Assert.Equal(8, inBinder.Single(entry => entry.Name == "Forest").Quantity);
    }

    [Fact]
    public async Task MovementsAddAndRemoveCards()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");

        var bought = await MoveAsync(client, new { kind = "purchase" }, new { kind = "location", locationId = rack.Id }, new { name = "Lightning Bolt", quantity = 4 });
        var entry = Assert.Single(await client.EntriesAsync(rack.Id));
        var sold = await MoveAsync(client, new { kind = "location", locationId = rack.Id }, new { kind = "sale" }, new { entryId = entry.Id, quantity = 3 });
        var outside = await MoveAsync(client, new { kind = "gift" }, new { kind = "sale" }, new { name = "Sol Ring" });
        var wrongOrigin = await MoveAsync(client, new { kind = "sale" }, new { kind = "location", locationId = rack.Id }, new { name = "Sol Ring" });
        var tooMany = await MoveAsync(client, new { kind = "location", locationId = rack.Id }, new { kind = "trade" }, new { entryId = entry.Id, quantity = 2 });

        var purchase = (await bought.Content.ReadFromJsonAsync<MovementBatchResponse>(Cancel))!.Movements.Single();
        Assert.Equal(("add", "purchase", "Rack"), (purchase.Kind, purchase.Origin.Kind, purchase.Destination.LocationName));
        Assert.Equal("remove", (await sold.Content.ReadFromJsonAsync<MovementBatchResponse>(Cancel))!.Movements.Single().Kind);
        Assert.Equal(1, Assert.Single(await client.EntriesAsync(rack.Id)).Quantity);
        Assert.Equal("destination", await outside.FieldAsync());
        Assert.Equal("origin.kind", await wrongOrigin.FieldAsync());
        Assert.Equal("not_enough_cards", await tooMany.CodeAsync());
        Assert.Equal(1, await instance.CountAsync("movements", ownerId, "origin_kind = 'purchase' and quantity = 4"));
        Assert.Equal(1, await instance.CountAsync("movements", ownerId, "destination_kind = 'sale' and quantity = 3"));
    }

    [Fact]
    public async Task LockedLocationRefusesMovements()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var cube = await client.CreateLocationAsync("Cube", "cube");
        var entry = (await client.AddAsync(rack.Id, new { name = "Sol Ring" })).Entries.Single();
        var other = await client.CreateLocationAsync("Other", "box");
        var foreign = (await client.AddAsync(other.Id, new { name = "Forest" })).Entries.Single();
        await client.PostAsync(new Uri($"/api/v1/collection/locations/{cube.Id}/lock", UriKind.Relative), null, Cancel);

        var into = await MoveAsync(client, new { kind = "location", locationId = rack.Id }, new { kind = "location", locationId = cube.Id }, new { entryId = entry.Id });
        var fromOther = await MoveAsync(client, new { kind = "location", locationId = rack.Id }, new { kind = "nowhere" }, new { entryId = foreign.Id });
        var same = await MoveAsync(client, new { kind = "location", locationId = rack.Id }, new { kind = "location", locationId = rack.Id }, new { entryId = entry.Id });

        Assert.Equal("location_locked", await into.CodeAsync());
        Assert.Equal("cards[0].entryId", await fromOther.FieldAsync());
        Assert.Equal("destination.locationId", await same.FieldAsync());
        Assert.Single(await client.EntriesAsync(rack.Id));
    }

    [Fact]
    public async Task HistoryListsTheMovementsOfALocationWithPaging()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var box = await client.CreateLocationAsync("Box", null, rack.Id);
        var binder = await client.CreateLocationAsync("Binder", "binder");
        await client.AddAsync(rack.Id, new { name = "Sol Ring" });
        await client.AddAsync(box.Id, new { name = "Forest" });
        await client.AddAsync(binder.Id, new { name = "Counterspell" });

        var all = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/collection/movements", UriKind.Relative), Cancel);
        var ofRack = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/collection/movements?locationId={rack.Id}", UriKind.Relative), Cancel);
        var first = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/collection/movements?limit=2", UriKind.Relative), Cancel);
        var second = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/collection/movements?limit=2&cursor={first.GetProperty("nextCursor").GetString()}", UriKind.Relative), Cancel);

        static List<string> Names(JsonElement page) => [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
        Assert.Equal(["Counterspell", "Forest", "Sol Ring"], Names(all));
        Assert.Equal(["Forest", "Sol Ring"], Names(ofRack));
        Assert.Equal("Rack / Box", ofRack.GetProperty("items")[0].GetProperty("destination").GetProperty("locationName").GetString());
        Assert.Equal(["Counterspell", "Forest"], Names(first));
        Assert.Equal(["Sol Ring"], Names(second));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
    }
}
