using System.Net;
using System.Net.Http.Json;
using Reliquary.Collection.Api;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

[Collection(CollectionGroup.Name)]
public sealed class LocationTests(CollectionInstance instance)
{
    [Fact]
    public async Task LocationHasTwoLevelsAtMost()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack 1", "rack");
        var box = await client.CreateLocationAsync("Box A", null, rack.Id);

        var deeper = await client.PostAsJsonAsync("/api/v1/collection/locations", new { name = "Too deep", parentId = box.Id }, Cancel);
        var otherKind = await client.PostAsJsonAsync("/api/v1/collection/locations", new { name = "Binder in a rack", kind = "binder", parentId = rack.Id }, Cancel);
        var deck = await client.PostAsJsonAsync("/api/v1/collection/locations", new { name = "My deck", kind = "deck" }, Cancel);
        var noKind = await client.PostAsJsonAsync("/api/v1/collection/locations", new { name = "No kind" }, Cancel);
        var list = await client.GetFromJsonAsync<List<LocationResponse>>(new Uri("/api/v1/collection/locations", UriKind.Relative), Cancel);

        Assert.Equal("rack", box.Kind);
        Assert.Equal(rack.Id, box.ParentId);
        Assert.Equal(HttpStatusCode.BadRequest, deeper.StatusCode);
        Assert.Equal("parentId", await deeper.FieldAsync());
        Assert.Equal("kind", await otherKind.FieldAsync());
        Assert.Equal("kind", await deck.FieldAsync());
        Assert.Equal("kind", await noKind.FieldAsync());
        var root = Assert.Single(list!);
        Assert.Equal("Box A", Assert.Single(root.Sections).Name);
    }

    [Fact]
    public async Task ChangeOfKindChangesTheSections()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Shelf", "rack");
        var section = await client.CreateLocationAsync("Top", null, rack.Id);

        var changed = await client.PatchAsJsonAsync($"/api/v1/collection/locations/{rack.Id}", new { name = "Big box", kind = "box", pickable = false, position = 3 }, Cancel);
        var sectionKind = await client.PatchAsJsonAsync($"/api/v1/collection/locations/{section.Id}", new { kind = "cube" }, Cancel);
        var location = (await changed.Content.ReadFromJsonAsync<LocationResponse>(Cancel))!;

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(("Big box", "box", false, 3), (location.Name, location.Kind, location.Pickable, location.Position));
        Assert.Equal("box", Assert.Single(location.Sections).Kind);
        Assert.Equal(HttpStatusCode.BadRequest, sectionKind.StatusCode);
    }

    [Fact]
    public async Task LockedLocationRefusesEachChangeOfItsContent()
    {
        using var client = await instance.NewUserAsync();
        var solRing = await instance.OracleIdAsync("Sol Ring");
        var cube = await client.CreateLocationAsync("Cube", "cube");
        var section = await client.CreateLocationAsync("Artifacts", null, cube.Id);
        var added = await client.AddAsync(cube.Id, new { oracleId = solRing });
        var entry = Assert.Single(added.Entries);

        var locked = await client.PostAsync(new Uri($"/api/v1/collection/locations/{cube.Id}/lock", UriKind.Relative), null, Cancel);
        var add = await client.PostAsJsonAsync($"/api/v1/collection/locations/{cube.Id}/entries", new { entries = new[] { new { oracleId = solRing } } }, Cancel);
        var addToSection = await client.PostAsJsonAsync($"/api/v1/collection/locations/{section.Id}/entries", new { entries = new[] { new { oracleId = solRing } } }, Cancel);
        var update = await client.PatchAsJsonAsync($"/api/v1/collection/entries/{entry.Id}", new { quantity = 3 }, Cancel);
        var delete = await client.DeleteAsync(new Uri($"/api/v1/collection/entries/{entry.Id}", UriKind.Relative), Cancel);
        var deleteLocation = await client.DeleteAsync(new Uri($"/api/v1/collection/locations/{cube.Id}", UriKind.Relative), Cancel);
        var unlocked = await client.PostAsync(new Uri($"/api/v1/collection/locations/{cube.Id}/unlock", UriKind.Relative), null, Cancel);
        var after = await client.PatchAsJsonAsync($"/api/v1/collection/entries/{entry.Id}", new { quantity = 3 }, Cancel);

        Assert.True((await locked.Content.ReadFromJsonAsync<LocationResponse>(Cancel))!.Locked);
        foreach (var refused in new[] { add, addToSection, update, delete, deleteLocation })
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("location_locked", await refused.CodeAsync());
        }

        Assert.False((await unlocked.Content.ReadFromJsonAsync<LocationResponse>(Cancel))!.Locked);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task DeletionRefusesLocationWithCards()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var section = await client.CreateLocationAsync("Box", null, rack.Id);
        var empty = await client.CreateLocationAsync("Empty", "binder");
        await client.AddAsync(section.Id, new { name = "Forest", quantity = 10 });

        var refused = await client.DeleteAsync(new Uri($"/api/v1/collection/locations/{rack.Id}", UriKind.Relative), Cancel);
        var deleted = await client.DeleteAsync(new Uri($"/api/v1/collection/locations/{empty.Id}", UriKind.Relative), Cancel);
        var missing = await client.GetAsync(new Uri($"/api/v1/collection/locations/{empty.Id}", UriKind.Relative), Cancel);
        var list = await client.GetFromJsonAsync<List<LocationResponse>>(new Uri("/api/v1/collection/locations", UriKind.Relative), Cancel);

        Assert.Equal("location_not_empty", await refused.CodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var root = Assert.Single(list!);
        Assert.Equal((0, 10), (root.CardCount, root.TotalCardCount));
    }

    [Fact]
    public async Task ChangesWriteDomainEvents()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        await client.AddAsync(rack.Id, new { name = "Sol Ring" });
        await client.PostAsync(new Uri($"/api/v1/collection/locations/{rack.Id}/lock", UriKind.Relative), null, Cancel);

        Assert.Equal(1, await instance.CountAsync("outbox", ownerId, "event_type = 'collection.location.created'"));
        Assert.Equal(1, await instance.CountAsync("outbox", ownerId, "event_type = 'collection.entries.changed'"));
        Assert.Equal(1, await instance.CountAsync("outbox", ownerId, "event_type = 'collection.location.updated'"));
    }

    [Fact]
    public async Task OtherUserCannotSeeTheLocation()
    {
        using var client = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Private rack", "rack");

        var get = await other.GetAsync(new Uri($"/api/v1/collection/locations/{rack.Id}", UriKind.Relative), Cancel);
        var add = await other.PostAsJsonAsync($"/api/v1/collection/locations/{rack.Id}/entries", new { entries = new[] { new { name = "Sol Ring" } } }, Cancel);
        var anonymous = await (await instance.GetAsync()).CreateClient().GetAsync(new Uri("/api/v1/collection/locations", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
