using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Decks.Api;
using Reliquary.Decks.Exploration;
using Reliquary.Decks.Versions;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class DeckVersionTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachVersionKeepsTheLinesOfItsCreation()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "History", new { name = "Sol Ring", tags = new[] { "Fast" }, categories = new[] { "ramp" } });

        var first = await CommitAsync(client, deck.Id, "First list");
        await AddAsync(client, deck.Id, new { name = "Forest", quantity = 10 });
        var second = await CommitAsync(client, deck.Id, "Lands");
        await SetTextAsync(client, deck.Id, "Main\n1 Sol Ring #Fast #@ramp\n12 Forest\n1 Counterspell\n");
        var third = await CommitAsync(client, deck.Id, "More");
        var again = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/versions", new { message = "Nothing" }, Cancel);
        var history = await client.GetFromJsonAsync<DeckHistoryResponse>(new Uri($"/api/v1/decks/{deck.Id}/versions", UriKind.Relative), Cancel);

        Assert.Equal((1, 2, 3), (first.Number, second.Number, third.Number));
        Assert.Equal([1], Quantities(await GetVersionAsync(client, deck.Id, first.Id)));
        Assert.Equal([1, 10], Quantities(await GetVersionAsync(client, deck.Id, second.Id)));
        var thirdLines = await GetVersionAsync(client, deck.Id, third.Id);
        Assert.Equal([1, 12, 1], Quantities(thirdLines));
        Assert.Contains(thirdLines.Lines, line => line.Tags.SequenceEqual(["Fast"]) && line.Categories.SequenceEqual(["ramp"]));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(DeckErrorCodes.NoChanges, (await again.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
        Assert.Equal([3, 2, 1], history!.Versions.Select(version => version.Number));
        Assert.Equal([second.Id], history.Versions[0].ParentIds);
        Assert.False(history.WorkingListChanged);
    }

    [Fact]
    public async Task DatabaseRefusesAChangeOfAVersion()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Frozen", new { name = "Sol Ring" });
        var version = await CommitAsync(client, deck.Id, "First");
        await using var connection = new NpgsqlConnection(instance.ConnectionString);

        var update = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("update content.deck_versions set message = 'changed' where id = @id", new { id = version.Id }));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("delete from content.deck_version_lines where version_id = @id", new { id = version.Id }));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, update.SqlState);
        Assert.Equal(PostgresErrorCodes.RestrictViolation, delete.SqlState);
    }

    [Fact]
    public async Task ChangesSeparateThePrintingsAndRestoreGivesANewVersion()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Changes", new { name = "Sol Ring" }, new { name = "Forest", quantity = 4 });
        var solRing = deck.Lines.Single(line => deck.Cards[line.OracleId].Name == "Sol Ring");
        var printing = deck.Cards[solRing.OracleId].DefaultPrintingId!.Value;
        var first = await CommitAsync(client, deck.Id, "First");
        await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/{solRing.Id}", new { printingId = printing, finish = "foil" }, Cancel);
        await AddAsync(client, deck.Id, new { name = "Forest", quantity = 2 }, new { name = "Counterspell" });
        var second = await CommitAsync(client, deck.Id, "Second");

        var changes = await client.GetFromJsonAsync<ListChangesResponse>(new Uri($"/api/v1/decks/{deck.Id}/versions/{second.Id}/changes", UriKind.Relative), Cancel);
        var restore = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/versions/{first.Id}/restore", new { }, Cancel);
        var restored = (await restore.Content.ReadFromJsonAsync<DeckVersionResponse>(Cancel))!;
        var working = await client.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);

        Assert.Equal(
            [("Counterspell", 0, 1), ("Forest", 4, 6)],
            changes!.Quantities.Select(change => (changes.Cards[change.OracleId].Name, change.Before, change.After)).Order());
        var printingChange = Assert.Single(changes.Printings);
        Assert.Equal(("Sol Ring", "foil"), (changes.Cards[printingChange.OracleId].Name, printingChange.After.Single().Finish));
        Assert.Equal((3, "restore", "Restore of version 1"), (restored.Number, restored.Kind, restored.Message));
        Assert.Equal([("Forest", 4), ("Sol Ring", 1)], working!.Lines.Select(line => (working.Cards[line.OracleId].Name, line.Quantity)).Order());
        Assert.Equal(restored.Id, working.HeadVersionId);
    }

    [Fact]
    public async Task UserMakesAVariantMergesItAndRevertsTheMerge()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, "Atraxa", new { name = "Sol Ring" }, new { name = "Forest", quantity = 10 });

        var created = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/variants", new { name = "Budget" }, Cancel);
        var variant = (await created.Content.ReadFromJsonAsync<DeckVariantResponse>(Cancel))!;
        await SetTextAsync(client, variant.Id, "Main\n1 Sol Ring\n7 Forest\n1 Counterspell\n1 Lightning Bolt\n1 Cultivate\n");
        var merge = await client.PostAsJsonAsync($"/api/v1/decks/{variant.Id}/merge", new { message = "Take the budget list" }, Cancel);
        var merged = (await merge.Content.ReadFromJsonAsync<DeckVersionResponse>(Cancel))!;
        var afterMerge = await GetDeckAsync(client, deck.Id);
        var revert = await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/versions/{merged.Id}/revert", new { }, Cancel);
        var afterRevert = await GetDeckAsync(client, deck.Id);
        var history = await client.GetFromJsonAsync<DeckHistoryResponse>(new Uri($"/api/v1/decks/{deck.Id}/versions", UriKind.Relative), Cancel);
        var variants = await client.GetFromJsonAsync<List<DeckVariantResponse>>(new Uri($"/api/v1/decks/{deck.Id}/variants", UriKind.Relative), Cancel);
        var list = await client.GetFromJsonAsync<Page<DeckSummaryResponse>>(new Uri("/api/v1/decks", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(("merge", 2), (merged.Kind, merged.ParentIds.Count));
        Assert.Equal(11, afterMerge.Lines.Sum(line => line.Quantity));
        Assert.Equal(HttpStatusCode.Created, revert.StatusCode);
        Assert.Equal([("Forest", 10), ("Sol Ring", 1)], afterRevert.Lines.Select(line => (afterRevert.Cards[line.OracleId].Name, line.Quantity)).Order());
        Assert.Equal(["revert", "merge", "commit"], history!.Versions.Select(version => version.Kind));
        Assert.Equal(("Budget", history.Versions[^1].Id), (Assert.Single(variants!).Name, variants![0].BaseVersionId!.Value));
        Assert.Equal(deck.Id, Assert.Single(list!.Items).Id);
    }

    [Fact]
    public async Task VariantFollowsItsMainDeck()
    {
        using var client = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(client, $"Main {Guid.NewGuid():N}", new { name = "Sol Ring" });
        var variant = (await (await client.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/variants", new { name = "cEDH" }, Cancel)).Content.ReadFromJsonAsync<DeckVariantResponse>(Cancel))!;

        var refused = await client.PatchAsJsonAsync($"/api/v1/decks/{variant.Id}", new { visibility = "public" }, Cancel);
        await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}", new { visibility = "public", format = "commander" }, Cancel);
        var variantDeck = await GetDeckAsync(client, variant.Id);
        await instance.PublishEventsAsync();
        var search = await client.GetFromJsonAsync<Page<PublicDeckResponse>>(new Uri($"/api/v1/public-decks?q={Uri.EscapeDataString(deck.Name)}", UriKind.Relative), Cancel);
        var nested = await client.PostAsJsonAsync($"/api/v1/decks/{variant.Id}/variants", new { name = "Nested" }, Cancel);
        await client.DeleteAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var gone = await client.GetAsync(new Uri($"/api/v1/decks/{variant.Id}", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(("public", deck.Id), (variantDeck.Visibility, variantDeck.VariantOf));
        Assert.Equal(deck.Id, Assert.Single(search!.Items).Id);
        Assert.Equal(HttpStatusCode.BadRequest, nested.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    private static List<int> Quantities(DeckVersionDetailResponse version) =>
        [.. version.Lines.OrderBy(line => version.Cards[line.OracleId].Name == "Sol Ring" ? 0 : version.Cards[line.OracleId].Name == "Forest" ? 1 : 2).Select(line => line.Quantity)];

    private static async Task<DeckVersionResponse> CommitAsync(HttpClient client, Guid deckId, string message)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/decks/{deckId}/versions", new { message }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckVersionResponse>(Cancel))!;
    }

    private static async Task<DeckVersionDetailResponse> GetVersionAsync(HttpClient client, Guid deckId, Guid versionId) =>
        (await client.GetFromJsonAsync<DeckVersionDetailResponse>(new Uri($"/api/v1/decks/{deckId}/versions/{versionId}", UriKind.Relative), Cancel))!;

    private static async Task<DeckResponse> GetDeckAsync(HttpClient client, Guid deckId) =>
        (await client.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{deckId}", UriKind.Relative), Cancel))!;

    private static async Task AddAsync(HttpClient client, Guid deckId, params object[] lines)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/decks/{deckId}/lines", new { lines }, Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
    }

    private static async Task SetTextAsync(HttpClient client, Guid deckId, string text)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/decks/{deckId}/text", new { text }, Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
    }

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string name, params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name, lines }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}

[Collection(DecksGroup.Name)]
public sealed class CompareListsTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ComparisonOfTwoVersionsGivesTheQuantitiesAndTheEffect()
    {
        using var client = await instance.NewUserAsync();
        var created = await client.PostAsJsonAsync("/api/v1/decks", new { name = "Compare", lines = new object[] { new { name = "Sol Ring", note = "Fast" }, new { name = "Forest", quantity = 10 }, new { name = "Counterspell", quantity = 2 } } }, Cancel);
        var deck = (await created.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
        var first = await CommitAsync(client, deck.Id, "First");
        await client.PutAsJsonAsync($"/api/v1/decks/{deck.Id}/text", new { text = "Main\n1 Sol Ring -- Turn one\n12 Forest\n1 Lightning Bolt\n" }, Cancel);
        var second = await CommitAsync(client, deck.Id, "Second");
        using var other = await instance.NewUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/lists/compare", new { left = new { deckId = deck.Id, versionId = first.Id }, right = new { deckId = deck.Id, versionId = second.Id } }, Cancel);
        var comparison = (await response.Content.ReadFromJsonAsync<CompareListsResponse>(Cancel))!;
        var working = await client.PostAsJsonAsync("/api/v1/lists/compare", new { left = new { deckId = deck.Id, versionId = second.Id }, right = new { deckId = deck.Id } }, Cancel);
        var refused = await other.PostAsJsonAsync("/api/v1/lists/compare", new { left = new { deckId = deck.Id }, right = new { deckId = deck.Id } }, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var changes = comparison.Changes;
        Assert.Equal(
            [("Counterspell", 2, 0), ("Forest", 10, 12), ("Lightning Bolt", 0, 1)],
            changes.Quantities.Select(change => (changes.Cards[change.OracleId].Name, change.Before, change.After)).Order());
        Assert.Equal(("Fast", "Turn one"), (Assert.Single(changes.Notes).Before, changes.Notes[0].After));
        Assert.Equal((13, 14), (comparison.Left.CardCount, comparison.Right.CardCount));
        Assert.Equal((10, 12), (comparison.Left.LandCount, comparison.Right.LandCount));
        Assert.Empty((await working.Content.ReadFromJsonAsync<CompareListsResponse>(Cancel))!.Changes.Quantities);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    private static async Task<DeckVersionResponse> CommitAsync(HttpClient client, Guid deckId, string message)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/decks/{deckId}/versions", new { message }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckVersionResponse>(Cancel))!;
    }
}
