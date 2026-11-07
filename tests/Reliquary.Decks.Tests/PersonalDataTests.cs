using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Reliquary.Accounts.PersonalData;
using Reliquary.Accounts.ServiceAccounts;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Tests;

/// <summary>
/// The export and the deletion of an account, with the data of the Decks component.
/// The tests are here because this project has the API, the worker and the decks.
/// </summary>
[Collection(DecksGroup.Name)]
public sealed class PersonalDataTests(DecksInstance instance)
{
    private static readonly string[] DecksRead = ["decks:read"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ArchiveContainsAllTheDataOfTheAccount()
    {
        using var client = await instance.NewUserAsync();
        await client.PostAsJsonAsync("/api/v1/deck-folders", new { name = "Commander" }, Cancel);
        await CreateDeckAsync(client, "Atraxa", new { name = "Sol Ring", tags = new[] { "Fast" }, globalTags = new[] { "Mana rock" } });
        await CreateDeckAsync(client, "Atraxa", new { name = "Forest", quantity = 4 });
        var serviceAccount = await DelegateAsync(client);

        var request = await client.PostAsync(new Uri("/api/v1/me/exports", UriKind.Relative), null, Cancel);
        await (await ServiceAsync()).ProcessExportsAsync(Cancel);
        var exports = await client.GetFromJsonAsync<List<DataExportResponse>>(new Uri("/api/v1/me/exports", UriKind.Relative), Cancel);
        var ready = Assert.Single(exports!);
        var download = await client.GetAsync(new Uri(ready.DownloadPath!, UriKind.Relative), Cancel);
        await using var stream = await download.Content.ReadAsStreamAsync(Cancel);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToList();
        var account = JsonDocument.Parse(await new StreamReader(zip.GetEntry("account/account.json")!.Open()).ReadToEndAsync(Cancel)).RootElement;
        var text = await new StreamReader(zip.GetEntry("decks/text/Atraxa.txt")!.Open()).ReadToEndAsync(Cancel);

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal("ready", ready.Status);
        Assert.Equal("application/zip", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(
            [
                "README.txt",
                "account/account.json",
                "collection/deck-tokens.json",
                "collection/inventories.json",
                "collection/locations.json",
                "collection/movements.json",
                "collection/settings.json",
                "collection/shares.json",
                "collection/storage-rules.json",
                "decks/category-rules.json",
                "decks/decks.json",
                "decks/favorites.json",
                "decks/folders.json",
                "decks/following.json",
                "decks/global-tags.json",
                "decks/text/Atraxa (2).txt",
                "decks/text/Atraxa.txt",
                "moderation/hidden-content.json",
                "moderation/moderation-actions.json",
                "moderation/reports.json",
            ],
            entries);
        Assert.Equal("Tester", account.GetProperty("displayName").GetString());
        Assert.False(account.TryGetProperty("passwordHash", out _));
        var delegation = Assert.Single(account.GetProperty("delegations").EnumerateArray());
        Assert.Equal(serviceAccount, delegation.GetProperty("serviceAccount").GetString());
        Assert.Contains("1 Sol Ring", text, StringComparison.Ordinal);
        Assert.Contains("#Fast", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletionRemovesTheOwnerFromAllTheTables()
    {
        using var client = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var me = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/me", UriKind.Relative), Cancel);
        var accountId = me.GetProperty("account").GetProperty("id").GetGuid();
        await client.PostAsJsonAsync("/api/v1/deck-folders", new { name = "Folder" }, Cancel);
        var deck = await CreateDeckAsync(client, "Public", new { name = "Sol Ring", globalTags = new[] { "Ramp" } }, "public");
        var line = Assert.Single(deck.Lines);
        await client.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}/lines/{line.Id}", new { categories = new[] { "ramp" } }, Cancel);
        var theirs = await CreateDeckAsync(other, "Theirs", new { name = "Sol Ring" }, "public");
        await client.PostAsJsonAsync("/api/v1/me/api-keys", new { name = "Script" }, Cancel);
        await DelegateAsync(client);
        await instance.PublishEventsAsync();
        await client.PutAsync(new Uri($"/api/v1/public-decks/{theirs.Id}/favorite", UriKind.Relative), null, Cancel);
        await other.PutAsync(new Uri($"/api/v1/public-decks/{deck.Id}/favorite", UriKind.Relative), null, Cancel);
        await other.PutAsync(new Uri($"/api/v1/authors/{accountId}/follow", UriKind.Relative), null, Cancel);
        await other.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/copy", new { }, Cancel);
        await instance.PublishEventsAsync();

        // The moderation data: a report by the user, a report about the user, and a hidden deck with its log entries.
        using var administrator = await instance.AdministratorAsync();
        var reportByUser = await client.PostAsJsonAsync("/api/v1/reports", new { itemType = "deck", itemId = theirs.Id, reason = "spam" }, Cancel);
        var reportAboutUser = await other.PostAsJsonAsync("/api/v1/reports", new { itemType = "author", itemId = accountId, reason = "other" }, Cancel);
        await other.PostAsJsonAsync("/api/v1/reports", new { itemType = "deck", itemId = deck.Id, reason = "offensive" }, Cancel);
        var hide = await administrator.PutAsJsonAsync($"/api/v1/moderation/hidden/deck/{deck.Id}", new { reason = "Offensive" }, Cancel);
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.NoContent), (reportByUser.StatusCode, reportAboutUser.StatusCode, hide.StatusCode));

        var wrong = await client.PostAsJsonAsync("/api/v1/me/deletion", new { password = "wrong password" }, Cancel);
        var requested = await client.PostAsJsonAsync("/api/v1/me/deletion", new { password = "correct horse battery" }, Cancel);
        var deletion = (await requested.Content.ReadFromJsonAsync<DeletionResponse>(Cancel))!;
        await (await ServiceAsync()).DeleteAccountAsync(accountId, Cancel);
        await instance.PublishEventsAsync();

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(TimeSpan.FromDays(7), deletion.DeleteAfter - deletion.RequestedAt);
        Assert.Empty(await FindOwnerAsync(accountId, deck.Id));
        var search = await other.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/public-decks?q=Public", UriKind.Relative), Cancel);
        Assert.DoesNotContain(search.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == deck.Id);
    }

    [Fact]
    public async Task CancelledDeletionKeepsTheAccount()
    {
        using var client = await instance.NewUserAsync();

        await client.PostAsJsonAsync("/api/v1/me/deletion", new { password = "correct horse battery" }, Cancel);
        var cancel = await client.DeleteAsync(new Uri("/api/v1/me/deletion", UriKind.Relative), Cancel);
        var state = await client.GetFromJsonAsync<DeletionResponse>(new Uri("/api/v1/me/deletion", UriKind.Relative), Cancel);
        var deleted = await (await ServiceAsync()).ProcessDeletionsAsync(Cancel);

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Null(state!.RequestedAt);
        Assert.Equal(0, deleted);
    }

    /// <summary>
    /// Creates a service account and gives it a delegation of the user.
    /// </summary>
    /// <returns>The name of the service account.</returns>
    private async Task<string> DelegateAsync(HttpClient client)
    {
        var api = await instance.GetAsync();
        var name = $"export-{Guid.NewGuid():N}"[..20];
        var created = (await api.Services.GetRequiredService<ServiceAccountService>().CreateAsync(name, "A test module.", Cancel)).Created!;
        var response = await client.PostAsJsonAsync("/api/v1/me/delegations", new { serviceAccountId = created.ServiceAccount.Id, scopes = DecksRead }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return name;
    }

    private async Task<PersonalDataService> ServiceAsync()
    {
        await instance.EnsureWorkerAsync();
        return instance.Worker.GetRequiredService<PersonalDataService>();
    }

    /// <summary>
    /// Finds the columns that contain the owner ID or the ID of a deck of the owner, in all the tables except the outboxes.
    /// </summary>
    private async Task<List<string>> FindOwnerAsync(Guid ownerId, Guid deckId)
    {
        await using var connection = new NpgsqlConnection(instance.ConnectionString);
        await connection.OpenAsync(Cancel);
        var columns = await connection.QueryAsync<(string Schema, string Table, string Column, string Type)>(
            """
            select c.table_schema, c.table_name, c.column_name, c.data_type
            from information_schema.columns c
            join information_schema.tables t on t.table_schema = c.table_schema and t.table_name = c.table_name and t.table_type = 'BASE TABLE'
            where c.table_schema in ('users', 'content', 'reference')
                and (c.data_type = 'uuid' or (c.data_type = 'ARRAY' and c.udt_name = '_uuid'))
                and c.table_name <> 'outbox'
            """);
        var found = new List<string>();
        foreach (var (schema, table, column, type) in columns)
        {
            var condition = type == "uuid" ? $"\"{column}\" in (@ownerId, @deckId)" : $"\"{column}\" && array[@ownerId, @deckId]::uuid[]";
            if (await connection.ExecuteScalarAsync<bool>($"select exists (select 1 from \"{schema}\".\"{table}\" where {condition})", new { ownerId, deckId }))
            {
                found.Add($"{schema}.{table}.{column}");
            }
        }

        return found;
    }

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string name, object line, string visibility = "private")
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name, visibility, lines = new[] { line } }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}
