using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.Collection.Api;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

/// <summary>
/// The global view: all the cards grouped by Oracle card, with the locations of the copies.
/// </summary>
[Collection(CollectionGroup.Name)]
public sealed class ViewTests(CollectionInstance instance)
{
    private static async Task<(List<CardViewResponse> Items, string? Next)> CardsAsync(HttpClient client, string query = "")
    {
        var page = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/collection/cards?{query}", UriKind.Relative), Cancel);
        var next = page.GetProperty("nextCursor").GetString();
        return (page.GetProperty("items").Deserialize<List<CardViewResponse>>(JsonSerializerOptions.Web)!, next);
    }

    [Fact]
    public async Task GlobalViewShowsTheCopiesOfEachCardInAllTheLocations()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var rack1 = await client.CreateLocationAsync("Rack 1", "rack");
        var rack2 = await client.CreateLocationAsync("Rack 2", "rack");
        var box = await client.CreateLocationAsync("Box", null, rack2.Id);
        var french = await instance.PrintingAsync("ltc", "284");
        await client.AddAsync(rack1.Id, new { name = "Sol Ring", quantity = 2 }, new { name = "Counterspell" });
        await client.AddAsync(box.Id, new { name = "Sol Ring", printingId = french.PrintingId }, new { name = "Forest", quantity = 30 });
        var decks = (await instance.GetAsync()).Services.GetRequiredService<IAssembledDeckLocations>();
        var deckId = Guid.CreateVersion7();
        await decks.EnsureLocationAsync(ownerId, deckId, "Atraxa", Cancel);
        await decks.SetContentAsync(ownerId, deckId, [new(await instance.OracleIdAsync("Sol Ring"), null, "en", null, 1)], Cancel);

        var (all, _) = await CardsAsync(client);
        var (bySearch, _) = await CardsAsync(client, "q=ring");
        var (byLanguage, _) = await CardsAsync(client, "language=fr");
        var (bySet, _) = await CardsAsync(client, "set=LTC");
        var (byLocation, _) = await CardsAsync(client, $"locationId={rack2.Id}");
        var (first, cursor) = await CardsAsync(client, "limit=2");
        var (second, last) = await CardsAsync(client, $"limit=2&cursor={cursor}");
        var unknown = await client.GetAsync(new Uri($"/api/v1/collection/cards?locationId={Guid.CreateVersion7()}", UriKind.Relative), Cancel);

        Assert.Equal(["Counterspell", "Forest", "Sol Ring"], all.Select(card => card.Name));
        var solRing = all[2];
        Assert.Equal(4, solRing.Quantity);
        Assert.Equal(
            [("Rack 1", null, 2), ("Rack 2", "Box", 1), ("Atraxa", null, 1)],
            solRing.Locations.Select(location => (location.LocationName, location.SectionName, location.Quantity)).ToList());
        Assert.NotNull(solRing.ImageUri);
        Assert.Equal("Sol Ring", Assert.Single(bySearch).Name);
        Assert.Equal(1, Assert.Single(byLanguage).Quantity);
        Assert.Equal("Sol Ring", Assert.Single(bySet).Name);
        Assert.Equal(["Forest", "Sol Ring"], byLocation.Select(card => card.Name));
        Assert.Equal(["Counterspell", "Forest"], first.Select(card => card.Name));
        Assert.Equal("Sol Ring", Assert.Single(second).Name);
        Assert.Null(last);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    /// <summary>
    /// Seeds 50,000 entries for one owner, then measures the first page of the global view through the API.
    /// </summary>
    [Fact]
    public async Task FirstPageOfFiftyThousandEntriesTakesLessThan500Milliseconds()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        await using (var connection = new NpgsqlConnection(instance.ConnectionString))
        {
            await connection.OpenAsync(Cancel);
            await connection.ExecuteAsync(
                """
                insert into content.locations (id, owner_id, name, kind, created_at, updated_at)
                select uuidv7(), @ownerId, 'Rack ' || n, 'rack', now(), now() from generate_series(1, 20) as n;

                -- 12,500 cards, each in 4 locations: 50,000 entries.
                with cards as (
                    select uuidv7() as oracle_id, 'Card ' || lpad(n::text, 5, '0') as name, n from generate_series(1, 12500) as n
                ), places as (
                    select id, row_number() over (order by id) as position from content.locations where owner_id = @ownerId
                )
                insert into content.location_entries (id, owner_id, location_id, oracle_id, card_name, set_code, language, quantity, created_at, updated_at)
                select uuidv7(), @ownerId, p.id, c.oracle_id, c.name, 'set' || (c.n % 50), case when copy = 1 then 'fr' else 'en' end, 1 + (c.n % 4), now(), now()
                from cards c cross join generate_series(1, 4) as copy
                join places p on p.position = 1 + ((c.n + copy * 5) % 20);

                analyze content.location_entries;
                """,
                new { ownerId });
        }

        Assert.Equal(50_000, await instance.CountAsync("location_entries", ownerId));
        await CardsAsync(client, "limit=50");

        var timings = new List<TimeSpan>();
        foreach (var query in new[] { "limit=50", "limit=100", "limit=50&q=card 1", "limit=50&language=fr&set=set7" })
        {
            var watch = Stopwatch.StartNew();
            var (items, next) = await CardsAsync(client, query);
            watch.Stop();
            timings.Add(watch.Elapsed);
            Assert.NotEmpty(items);
            Assert.NotNull(next);
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Global view with 50,000 entries: {string.Join(", ", timings.Select(time => $"{time.TotalMilliseconds:F0} ms"))}.");
        Assert.All(timings, time => Assert.True(time < TimeSpan.FromMilliseconds(500), $"The page took {time.TotalMilliseconds:F0} ms."));
    }
}
