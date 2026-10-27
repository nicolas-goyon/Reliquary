using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class DeckOrganizationTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GroupActionMovesFiveDecksToAFolder()
    {
        using var client = await instance.NewUserAsync();
        var folder = await CreateFolderAsync(client, "Commander", null);
        var decks = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            decks.Add((await CreateDeckAsync(client, $"Deck {index}")).Id);
        }

        var response = await client.PostAsJsonAsync("/api/v1/decks/group", new { deckIds = decks.Take(5), action = "move", folderId = folder.Id }, Cancel);
        var inFolder = await ListAsync(client, $"folderId={folder.Id}");
        var noFolder = await ListAsync(client, "folderId=none");
        var folders = await client.GetFromJsonAsync<List<FolderResponse>>(new Uri("/api/v1/deck-folders", UriKind.Relative), Cancel);

        Assert.Equal(5, (await response.Content.ReadFromJsonAsync<DeckGroupResponse>(Cancel))!.Count);
        Assert.Equal(decks.Take(5).Order(), inFolder.Select(deck => deck.Id).Order());
        Assert.Equal(decks[5], Assert.Single(noFolder).Id);
        Assert.Equal(5, Assert.Single(folders!).DeckCount);
    }

    [Fact]
    public async Task GroupActionWithADeckOfAnotherUserChangesNothing()
    {
        using var client = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var mine = await CreateDeckAsync(client, "Mine");
        var theirs = await CreateDeckAsync(other, "Theirs");

        var response = await client.PostAsJsonAsync("/api/v1/decks/group", new { deckIds = new[] { mine.Id, theirs.Id }, action = "pin" }, Cancel);
        var after = await ListAsync(client, string.Empty);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(Assert.Single(after).Pinned);
    }

    [Fact]
    public async Task ListFiltersByFormatColorsTagAndArchive()
    {
        using var client = await instance.NewUserAsync();
        var atraxa = await CreateDeckAsync(client, "Atraxa", "commander", new { name = "Atraxa, Praetors' Voice", zone = "commander" }, new { name = "Sol Ring" });
        var burn = await CreateDeckAsync(client, "Burn", "modern", new { name = "Lightning Bolt", quantity = 4 });
        var old = await CreateDeckAsync(client, "Old", "modern");
        await client.PatchAsJsonAsync($"/api/v1/decks/{atraxa.Id}", new { tags = new[] { "Casual", " casual ", "Superfriends" } }, Cancel);
        await client.PostAsJsonAsync("/api/v1/decks/group", new { deckIds = new[] { burn.Id }, action = "tag", tag = "Competitive" }, Cancel);
        await client.PostAsJsonAsync("/api/v1/decks/group", new { deckIds = new[] { old.Id }, action = "archive" }, Cancel);
        await client.PostAsJsonAsync("/api/v1/decks/group", new { deckIds = new[] { burn.Id }, action = "pin" }, Cancel);

        var all = await ListAsync(client, "sort=name");
        var tags = await client.GetFromJsonAsync<List<DeckTagSummaryResponse>>(new Uri("/api/v1/decks/tags", UriKind.Relative), Cancel);

        Assert.Equal(["Burn", "Atraxa"], all.Select(deck => deck.Name));
        Assert.Equal(("WUBG", "R"), (all[1].ColorIdentity, all[0].ColorIdentity));
        Assert.Equal(["Casual", "Superfriends"], all[1].Tags);
        Assert.Equal(["Atraxa"], (await ListAsync(client, "colors=gbwu")).Select(deck => deck.Name));
        Assert.Equal(["Old"], (await ListAsync(client, "colors=c&archived=only")).Select(deck => deck.Name));
        Assert.Equal(["Burn"], (await ListAsync(client, "format=modern")).Select(deck => deck.Name));
        Assert.Equal(["Atraxa"], (await ListAsync(client, "tag=CASUAL")).Select(deck => deck.Name));
        Assert.Equal(3, (await ListAsync(client, "archived=include")).Count);
        Assert.Equal([("Casual", 1), ("Competitive", 1), ("Superfriends", 1)], tags!.Select(tag => (tag.Name, tag.DeckCount)));
    }

    [Fact]
    public async Task ListPagesWithTheCursor()
    {
        using var client = await instance.NewUserAsync();
        for (var index = 0; index < 3; index++)
        {
            await CreateDeckAsync(client, $"Deck {index}");
        }

        var first = await client.GetFromJsonAsync<Page<DeckSummaryResponse>>(new Uri("/api/v1/decks?limit=2&sort=name", UriKind.Relative), Cancel);
        var second = await client.GetFromJsonAsync<Page<DeckSummaryResponse>>(new Uri($"/api/v1/decks?limit=2&sort=name&cursor={first!.NextCursor}", UriKind.Relative), Cancel);
        var invalid = await client.GetAsync(new Uri("/api/v1/decks?colors=xyz&sort=price", UriKind.Relative), Cancel);

        Assert.Equal(["Deck 0", "Deck 1"], first.Items.Select(deck => deck.Name));
        Assert.Equal(["Deck 2"], second!.Items.Select(deck => deck.Name));
        Assert.Null(second.NextCursor);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var body = await invalid.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("\"colors\"", body, StringComparison.Ordinal);
        Assert.Contains("\"sort\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FoldersNestAndRefuseACycle()
    {
        using var client = await instance.NewUserAsync();
        var top = await CreateFolderAsync(client, "Top", null);
        var middle = await CreateFolderAsync(client, "Middle", top.Id);
        var bottom = await CreateFolderAsync(client, "Bottom", middle.Id);
        var deck = await CreateDeckAsync(client, "Deck");
        await client.PostAsJsonAsync("/api/v1/decks/group", new { deckIds = new[] { deck.Id }, action = "move", folderId = bottom.Id }, Cancel);

        var cycle = await client.PatchAsJsonAsync($"/api/v1/deck-folders/{top.Id}", new { parentId = bottom.Id }, Cancel);
        var rename = await client.PatchAsJsonAsync($"/api/v1/deck-folders/{bottom.Id}", new { name = "Moved", moveToTop = true }, Cancel);
        var delete = await client.DeleteAsync(new Uri($"/api/v1/deck-folders/{top.Id}", UriKind.Relative), Cancel);
        var folders = await client.GetFromJsonAsync<List<FolderResponse>>(new Uri("/api/v1/deck-folders", UriKind.Relative), Cancel);
        var problem = await cycle.Content.ReadFromJsonAsync<JsonElement>(Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
        Assert.Equal("parentId", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        var renamed = (await rename.Content.ReadFromJsonAsync<FolderResponse>(Cancel))!;
        Assert.Equal((null, "Moved"), (renamed.ParentId, renamed.Name));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(("Moved", 1), (Assert.Single(folders!).Name, folders![0].DeckCount));
    }

    [Fact]
    public async Task ExportOfSeveralDecksGivesOneZipFile()
    {
        using var client = await instance.NewUserAsync();
        var first = await CreateDeckAsync(client, "Burn", "modern", new { name = "Lightning Bolt", quantity = 4 });
        var second = await CreateDeckAsync(client, "Burn", "modern", new { name = "Sol Ring" });

        var response = await client.PostAsJsonAsync("/api/v1/decks/export", new { deckIds = new[] { first.Id, second.Id }, format = "mtgo" }, Cancel);
        await using var stream = await response.Content.ReadAsStreamAsync(Cancel);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(["Burn (2).txt", "Burn.txt"], archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        var texts = archive.Entries.Select(entry => new StreamReader(entry.Open()).ReadToEnd()).ToList();
        Assert.Contains(texts, text => text.StartsWith("4 Lightning Bolt\n", StringComparison.Ordinal));
    }

    private static async Task<List<DeckSummaryResponse>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<Page<DeckSummaryResponse>>(new Uri($"/api/v1/decks?{query}", UriKind.Relative), Cancel))!.Items.ToList();

    private static async Task<FolderResponse> CreateFolderAsync(HttpClient client, string name, Guid? parentId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/deck-folders", new { name, parentId }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<FolderResponse>(Cancel))!;
    }

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string name, string format = "commander", params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name, format, lines }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}
