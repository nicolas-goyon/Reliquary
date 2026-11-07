using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class TagAndTextTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LinesHaveLocalTagsGlobalTagsAndCategories()
    {
        using var client = await instance.NewUserAsync();

        var deck = await CreateAsync(client,
            new { name = "Sol Ring", tags = new[] { "Fast mana" }, globalTags = new[] { "Mana rock" }, categories = new[] { "ramp" } },
            new { name = "Cultivate", globalTags = new[] { "mana ROCK" }, categories = new[] { "ramp", "land" } });
        var tags = await client.GetFromJsonAsync<List<TagResponse>>(new Uri("/api/v1/tags", UriKind.Relative), Cancel);

        var solRing = deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Sol Ring");
        Assert.Equal(["ramp"], solRing.Categories);
        Assert.Equal(["Fast mana", "Mana rock"], solRing.TagIds.Select(id => deck.Tags[id].Name).Order());
        Assert.Equal(2, deck.Tags.Count);
        Assert.False(deck.Tags.Values.Single(tag => tag.Name == "Fast mana").Global);
        var global = Assert.Single(tags!);
        Assert.Equal(("Mana rock", 2), (global.Name, global.LineCount));
    }

    [Fact]
    public async Task ChangeOfTheLocalTagsKeepsTheGlobalTags()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring", tags = new[] { "Old" }, globalTags = new[] { "Mana rock" } });
        var line = Assert.Single(deck.Lines);

        deck = await SendAsync(client, HttpMethod.Patch, $"/api/v1/decks/{deck.Id}/lines/{line.Id}", new { tags = new[] { "New" }, categories = new[] { "ramp" } });

        var changed = Assert.Single(deck.Lines);
        Assert.Equal(["Mana rock", "New"], changed.TagIds.Select(id => deck.Tags[id].Name).Order());
        Assert.Equal(["ramp"], changed.Categories);
    }

    [Fact]
    public async Task TextGivesTheSameDeckAfterARoundTrip()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring" });
        var printing = deck.Printings[deck.Cards.Values.Single().DefaultPrintingId!.Value];
        var text = $"""
            Commander
            1 Atraxa, Praetors' Voice

            Main
            1 Sol Ring ({printing.SetCode.ToUpperInvariant()}) {printing.CollectorNumber} *F* #Fast mana #!Mana rock #@ramp -- Turn one
            1 Foudre
            12 Forest

            Considering
            1 Fire /// Ice
            """;

        var set = await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/text", new { text, expectedVersion = deck.Version }, Cancel);
        var first = await client.GetFromJsonAsync<DeckTextResponse>(new Uri($"/api/v1/decks/{deck.Id}/text", UriKind.Relative), Cancel);
        var again = await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/text", new { text = first!.Text }, Cancel);
        var second = await client.GetFromJsonAsync<DeckTextResponse>(new Uri($"/api/v1/decks/{deck.Id}/text", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(first.Text, second!.Text);
        Assert.Contains($"1 Sol Ring ({printing.SetCode.ToUpperInvariant()}) {printing.CollectorNumber} *F* #Fast mana #!Mana rock #@ramp -- Turn one\n", first.Text, StringComparison.Ordinal);
        Assert.Contains("1 Lightning Bolt\n", first.Text, StringComparison.Ordinal);
        Assert.Contains("Considering\n1 Fire // Ice\n", first.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextWithErrorsChangesNothingAndGivesEachLine()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client, new { name = "Sol Ring" });

        var response = await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/text", new
        {
            text = "1 Sol Ring\n1 No such card\n0 Forest\n1 Counterspell (zzz) 1\n",
        }, Cancel);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var dryRun = await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/text", new { text = "4 Forest", dryRun = true }, Cancel);
        var unchanged = await client.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DeckErrorCodes.TextInvalid, problem.GetProperty("code").GetString());
        Assert.Equal(
            ["2 card_not_found", "3 quantity_out_of_range", "4 printing_not_found"],
            problem.GetProperty("lineErrors").EnumerateArray().Select(error => $"{error.GetProperty("line").GetInt32()} {error.GetProperty("code").GetString()}"));
        Assert.Equal(HttpStatusCode.NoContent, dryRun.StatusCode);
        Assert.Equal(deck.Version, unchanged!.Version);
    }

    [Fact]
    public async Task RenameToAnExistingNameMergesTheTagsAndDeleteRemovesATag()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateAsync(client,
            new { name = "Sol Ring", globalTags = new[] { "Ramp" } },
            new { name = "Cultivate", globalTags = new[] { "Mana" } },
            new { name = "Counterspell", globalTags = new[] { "Answer" } });
        var mana = deck.Tags.Single(tag => tag.Value.Name == "Mana").Key;
        var ramp = deck.Tags.Single(tag => tag.Value.Name == "Ramp").Key;
        var answer = deck.Tags.Single(tag => tag.Value.Name == "Answer").Key;

        var rename = await client.PatchAsJsonAsync($"/api/v1/tags/{mana}", new { name = "ramp" }, Cancel);
        var delete = await client.DeleteAsync(new Uri($"/api/v1/tags/{answer}", UriKind.Relative), Cancel);
        var after = await client.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var tags = await client.GetFromJsonAsync<List<TagResponse>>(new Uri("/api/v1/tags", UriKind.Relative), Cancel);

        Assert.Equal(ramp, (await rename.Content.ReadFromJsonAsync<RenamedTagResponse>(Cancel))!.Id);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(("Ramp", 2), (Assert.Single(tags!).Name, tags![0].LineCount));
        Assert.Empty(after!.Lines.Single(line => after.Cards[line.OracleId].Name == "Counterspell").TagIds);
        Assert.Equal(deck.Version + 2, after.Version);
    }

    [Fact]
    public async Task UnknownCategoryIsRefused()
    {
        using var client = await instance.NewUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name = "Test", lines = new[] { new { name = "Sol Ring", categories = new[] { "mana" } } } }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("lines[0].categories[0]", await response.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
    }

    private static async Task<DeckResponse> CreateAsync(HttpClient client, params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name = "Test deck", lines }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }

    private static async Task<DeckResponse> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        var response = await client.SendAsync(request, Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}

[Collection(DecksGroup.Name)]
public sealed class DeckCopyTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserCopiesThePublicDeckOfAnotherUser()
    {
        using var owner = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var created = await owner.PostAsJsonAsync("/api/v1/decks", new
        {
            name = "Original",
            visibility = "public",
            lines = new object[]
            {
                new { name = "Sol Ring", note = "Turn one", tags = new[] { "Fast" }, globalTags = new[] { "Mana rock" }, categories = new[] { "ramp" } },
                new { name = "Forest", quantity = 4 },
            },
        }, Cancel);
        var original = (await created.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
        var privateDeck = (await (await owner.PostAsJsonAsync("/api/v1/decks", new { name = "Secret" }, Cancel)).Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;

        var response = await other.PostAsJsonAsync($"/api/v1/decks/{original.Id}/copy", new { name = "My copy" }, Cancel);
        var copy = (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
        var refused = await other.PostAsJsonAsync($"/api/v1/decks/{privateDeck.Id}/copy", new { }, Cancel);
        var ownCopy = (await (await owner.PostAsJsonAsync($"/api/v1/decks/{original.Id}/copy", new { }, Cancel)).Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("My copy", "private", original.Id), (copy.Name, copy.Visibility, copy.SourceDeckId));
        Assert.True(copy.IsOwner);
        var solRing = copy.Lines.Single(line => copy.Cards[line.OracleId].Name == "Sol Ring");
        Assert.Equal(("Turn one", "ramp"), (solRing.Note, solRing.Categories.Single()));
        Assert.All(solRing.TagIds, id => Assert.False(copy.Tags[id].Global));
        Assert.Equal(["Fast", "Mana rock"], solRing.TagIds.Select(id => copy.Tags[id].Name).Order());
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Null(ownCopy.SourceDeckId);
        Assert.Equal("Original", ownCopy.Name);
        Assert.Contains(ownCopy.Tags.Values, tag => tag is { Name: "Mana rock", Global: true });
    }
}
