using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Decks.Api;
using Reliquary.Decks.Exploration;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class PublicDeckTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublicDeckShowsInTheSearchInLessThanTenSeconds()
    {
        using var author = await instance.NewUserAsync();
        var name = $"Atraxa {Guid.NewGuid():N}";
        var deck = await CreateDeckAsync(author, name, "private", new { name = "Atraxa, Praetors' Voice", zone = "commander" }, new { name = "Sol Ring" });
        await instance.PublishEventsAsync();
        Assert.Empty(await SearchAsync(author, $"q={name}"));

        var watch = Stopwatch.StartNew();
        await author.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}", new { visibility = "public" }, Cancel);
        await instance.PublishEventsAsync();
        var found = await SearchAsync(author, $"q={name}");
        watch.Stop();

        var result = Assert.Single(found);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(("WUBG", 2, "Tester"), (result.ColorIdentity, result.CardCount, result.Author.DisplayName));
        Assert.Equal("Atraxa, Praetors' Voice", Assert.Single(result.Commanders).Name);
    }

    [Fact]
    public async Task SearchFiltersByCommanderCardColorsAndFormatAndAVisitorCanSearch()
    {
        using var author = await instance.NewUserAsync();
        using var visitor = (await instance.GetAsync()).CreateClient();
        var tag = Guid.NewGuid().ToString("N");
        await CreateDeckAsync(author, $"Atraxa {tag}", "public", new { name = "Atraxa, Praetors' Voice", zone = "commander" }, new { name = "Sol Ring" });
        await CreateDeckAsync(author, $"Burn {tag}", "public", "modern", new { name = "Lightning Bolt", quantity = 4 });
        await CreateDeckAsync(author, $"Secret {tag}", "unlisted", new { name = "Sol Ring" });
        await instance.PublishEventsAsync();

        Assert.Equal([$"Atraxa {tag}"], (await SearchAsync(visitor, $"q={tag}&commander=Atraxa, Praetors' Voice")).Select(deck => deck.Name));
        Assert.Equal([$"Atraxa {tag}"], (await SearchAsync(visitor, $"q={tag}&card=Sol Ring")).Select(deck => deck.Name));
        Assert.Equal([$"Burn {tag}"], (await SearchAsync(visitor, $"q={tag}&colors=r&format=modern")).Select(deck => deck.Name));
        Assert.Equal(2, (await SearchAsync(visitor, $"q={tag}")).Count);
        var unknown = await visitor.GetAsync(new Uri("/api/v1/public-decks?card=No such card", UriKind.Relative), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task FavoritesCopiesAndFollowsChangeThePopularity()
    {
        using var author = await instance.NewUserAsync();
        using var reader = await instance.NewUserAsync();
        var tag = Guid.NewGuid().ToString("N");
        var quiet = await CreateDeckAsync(author, $"Quiet {tag}", "public", new { name = "Sol Ring" });
        var popular = await CreateDeckAsync(author, $"Popular {tag}", "public", new { name = "Sol Ring" });
        await instance.PublishEventsAsync();

        var favorite = await reader.PutAsync(new Uri($"/api/v1/public-decks/{popular.Id}/favorite", UriKind.Relative), null, Cancel);
        await reader.PostAsJsonAsync($"/api/v1/decks/{popular.Id}/copy", new { }, Cancel);
        var follow = await reader.PutAsync(new Uri($"/api/v1/authors/{popular.OwnerId}/follow", UriKind.Relative), null, Cancel);
        await instance.PublishEventsAsync();

        var popularFirst = await SearchAsync(reader, $"q={tag}&sort=popular");
        var favorites = (await reader.GetFromJsonAsync<Page<PublicDeckResponse>>(new Uri("/api/v1/public-decks/favorites", UriKind.Relative), Cancel))!.Items;
        var following = (await reader.GetFromJsonAsync<Page<PublicDeckResponse>>(new Uri("/api/v1/public-decks/following", UriKind.Relative), Cancel))!.Items;
        var profile = await reader.GetFromJsonAsync<AuthorResponse>(new Uri($"/api/v1/authors/{popular.OwnerId}", UriKind.Relative), Cancel);
        var social = await reader.GetFromJsonAsync<PublicDeckSocialResponse>(new Uri($"/api/v1/public-decks/{popular.Id}/social", UriKind.Relative), Cancel);
        var self = await author.PutAsync(new Uri($"/api/v1/authors/{popular.OwnerId}/follow", UriKind.Relative), null, Cancel);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (favorite.StatusCode, follow.StatusCode));
        Assert.Equal([popular.Id, quiet.Id], popularFirst.Select(deck => deck.Id));
        Assert.Equal((1, 1), (popularFirst[0].CopyCount, popularFirst[0].FavoriteCount));
        Assert.Equal(popular.Id, Assert.Single(favorites).Id);
        Assert.Equal(2, following.Count);
        Assert.Equal((2, 1, true), (profile!.DeckCount, profile.FollowerCount, profile.Following));
        Assert.True(social!.Favorite);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }

    [Fact]
    public async Task DeletedOrPrivateDeckLeavesTheReadModel()
    {
        using var author = await instance.NewUserAsync();
        using var reader = await instance.NewUserAsync();
        var tag = Guid.NewGuid().ToString("N");
        var first = await CreateDeckAsync(author, $"One {tag}", "public", new { name = "Sol Ring" });
        var second = await CreateDeckAsync(author, $"Two {tag}", "public", new { name = "Sol Ring" });
        await instance.PublishEventsAsync();

        await author.PatchAsJsonAsync($"/api/v1/decks/{first.Id}", new { visibility = "private" }, Cancel);
        await author.DeleteAsync(new Uri($"/api/v1/decks/{second.Id}", UriKind.Relative), Cancel);
        await instance.PublishEventsAsync();
        var favorite = await reader.PutAsync(new Uri($"/api/v1/public-decks/{first.Id}/favorite", UriKind.Relative), null, Cancel);

        Assert.Empty(await SearchAsync(reader, $"q={tag}"));
        Assert.Equal(HttpStatusCode.NotFound, favorite.StatusCode);
    }

    [Fact]
    public async Task VisitorOpensAPublicDeckWithItsLink()
    {
        using var author = await instance.NewUserAsync();
        using var visitor = (await instance.GetAsync()).CreateClient();
        var deck = await CreateDeckAsync(author, "Shared", "unlisted", new { name = "Sol Ring" });

        var response = await visitor.GetAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var opened = await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(opened!.IsOwner);
        Assert.Empty(opened.DeckTags);
    }

    private static async Task<List<PublicDeckResponse>> SearchAsync(HttpClient client, string query) =>
        [.. (await client.GetFromJsonAsync<Page<PublicDeckResponse>>(new Uri($"/api/v1/public-decks?{query}", UriKind.Relative), Cancel))!.Items];

    private static Task<DeckResponse> CreateDeckAsync(HttpClient client, string name, string visibility, params object[] lines) =>
        CreateDeckAsync(client, name, visibility, "commander", lines);

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string name, string visibility, string format, params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name, format, visibility, lines }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}
