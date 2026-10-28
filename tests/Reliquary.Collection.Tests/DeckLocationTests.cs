using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.Collection.Api;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

/// <summary>
/// The interfaces that the Decks component uses: <see cref="IAssembledDeckLocations"/> and <see cref="ICollectionCatalog"/>.
/// </summary>
[Collection(CollectionGroup.Name)]
public sealed class DeckLocationTests(CollectionInstance instance)
{
    private async Task<(IAssembledDeckLocations Decks, ICollectionCatalog Catalog)> ServicesAsync()
    {
        var services = (await instance.GetAsync()).Services;
        return (services.GetRequiredService<IAssembledDeckLocations>(), services.GetRequiredService<ICollectionCatalog>());
    }

    [Fact]
    public async Task AssembledDeckIsALocationWithNoAvailableCards()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var (decks, catalog) = await ServicesAsync();
        var solRing = await instance.OracleIdAsync("Sol Ring");
        var forest = await instance.OracleIdAsync("Forest");
        var goblin = await instance.OracleIdAsync("Goblin");
        var rack = await client.CreateLocationAsync("Rack 1", "rack");
        await client.AddAsync(rack.Id, new { oracleId = solRing, quantity = 2 });
        var deckId = Guid.CreateVersion7();

        var locationId = await decks.EnsureLocationAsync(ownerId, deckId, "Atraxa", Cancel);
        var again = await decks.EnsureLocationAsync(ownerId, deckId, "Atraxa superfriends", Cancel);
        await decks.SetContentAsync(ownerId, deckId, [new(solRing, null, "en", null, 1), new(forest, null, "en", null, 10), new(goblin, null, "en", null, 1)], Cancel);
        await decks.SetContentAsync(ownerId, deckId, [new(solRing, null, "en", null, 1), new(forest, null, "en", null, 8), new(goblin, null, "en", null, 1)], Cancel);
        var copies = await catalog.GetCopiesAsync(ownerId, [solRing, forest], Cancel);
        var location = await client.GetFromJsonAsync<LocationResponse>(new Uri($"/api/v1/collection/locations/{locationId}", UriKind.Relative), Cancel);
        var patch = await client.PatchAsJsonAsync($"/api/v1/collection/locations/{locationId}", new { name = "Other" }, Cancel);
        var add = await client.PostAsJsonAsync($"/api/v1/collection/locations/{locationId}/entries", new { entries = new[] { new { oracleId = solRing } } }, Cancel);

        Assert.Equal(locationId, again);
        Assert.Equal(("Atraxa superfriends", "deck", deckId), (location!.Name, location.Kind, location.DeckId));
        Assert.Equal(9, location.CardCount);
        Assert.Equal(3, copies.Count);
        Assert.True(copies.Single(copy => copy.LocationId == rack.Id).Available);
        Assert.All(copies.Where(copy => copy.LocationId == locationId), copy => Assert.False(copy.Available));
        Assert.Equal(8, copies.Single(copy => copy.OracleId == forest).Quantity);
        Assert.Equal(1, await instance.CountAsync("deck_tokens", ownerId, $"oracle_id = '{goblin}' and not owned"));
        Assert.Equal(1, await instance.CountAsync("movements", ownerId, "kind = 'remove' and destination_kind = 'nowhere' and quantity = 2"));
        Assert.Equal("deck_location", await patch.CodeAsync());
        Assert.Equal("deck_location", await add.CodeAsync());
    }

    [Fact]
    public async Task LockedDeckLocationRefusesNewContent()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var (decks, _) = await ServicesAsync();
        var solRing = await instance.OracleIdAsync("Sol Ring");
        var deckId = Guid.CreateVersion7();
        var locationId = await decks.EnsureLocationAsync(ownerId, deckId, "Showcase", Cancel);
        await client.PostAsync(new Uri($"/api/v1/collection/locations/{locationId}/lock", UriKind.Relative), null, Cancel);

        var error = await Assert.ThrowsAsync<LocationLockedException>(() => decks.SetContentAsync(ownerId, deckId, [new(solRing, null, "en", null, 1)], Cancel));

        Assert.Equal(locationId, error.LocationId);
    }

    [Fact]
    public async Task ReleaseMovesTheCardsToTheTarget()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var (decks, catalog) = await ServicesAsync();
        var solRing = await instance.OracleIdAsync("Sol Ring");
        var binder = await client.CreateLocationAsync("Binder", "binder");
        await client.PutAsJsonAsync("/api/v1/collection/settings", new { advancedFields = new[] { "finish" } }, Cancel);
        var deckId = Guid.CreateVersion7();
        var locationId = await decks.EnsureLocationAsync(ownerId, deckId, "Old deck", Cancel);
        await decks.SetContentAsync(ownerId, deckId, [new(solRing, null, "en", "foil", 1)], Cancel);

        await decks.ReleaseAsync(ownerId, deckId, binder.Id, Cancel);
        await decks.ReleaseAsync(ownerId, deckId, binder.Id, Cancel);
        var copy = Assert.Single(await catalog.GetCopiesAsync(ownerId, [solRing], Cancel));
        var gone = await client.GetAsync(new Uri($"/api/v1/collection/locations/{locationId}", UriKind.Relative), Cancel);

        Assert.Equal((binder.Id, "foil", true), (copy.LocationId, copy.Finish, copy.Available));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(1, await instance.CountAsync("movements", ownerId, $"kind = 'move' and destination_location_id = '{binder.Id}'"));
        Assert.Equal(1, await instance.CountAsync("outbox", ownerId, "event_type = 'collection.location.deleted'"));
    }

    [Fact]
    public async Task CatalogGivesTheSectionAndTheFlagsOfTheParent()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var (_, catalog) = await ServicesAsync();
        var bolt = await instance.OracleIdAsync("Lightning Bolt");
        var rack = await client.CreateLocationAsync("Rack 2", "rack");
        var box = await client.CreateLocationAsync("Red", null, rack.Id);
        await client.AddAsync(box.Id, new { oracleId = bolt, quantity = 4 });
        await client.PatchAsJsonAsync($"/api/v1/collection/locations/{rack.Id}", new { pickable = false }, Cancel);

        var copy = Assert.Single(await catalog.GetCopiesAsync(ownerId, [bolt], Cancel));

        Assert.Equal(("Rack 2", "Red", "rack", 4), (copy.LocationName, copy.SectionName, copy.LocationKind, copy.Quantity));
        Assert.False(copy.Pickable);
        Assert.False(copy.Available);
    }
}
