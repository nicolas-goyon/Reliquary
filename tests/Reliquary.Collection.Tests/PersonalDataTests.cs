using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Reliquary.Accounts.PersonalData;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.BuildingBlocks.Jobs;
using static Reliquary.Collection.Tests.CollectionApi;

namespace Reliquary.Collection.Tests;

/// <summary>
/// The export and the deletion of an account, with the data of the Collection component.
/// </summary>
[Collection(CollectionGroup.Name)]
public sealed class PersonalDataTests(CollectionInstance instance)
{
    [Fact]
    public async Task ArchiveContainsTheCollection()
    {
        using var client = await instance.NewUserAsync();
        var rack = await client.CreateLocationAsync("Rack 1", "rack");
        var box = await client.CreateLocationAsync("Box A", null, rack.Id);
        await client.AddAsync(rack.Id, new { name = "Sol Ring", printingId = (await instance.PrintingAsync("slz", "114")).PrintingId, finish = "nonfoil" });
        await client.AddAsync(box.Id, new { name = "Forest", quantity = 12 });

        await client.PostAsync(new Uri("/api/v1/me/exports", UriKind.Relative), null, Cancel);
        await RunPersonalDataJobAsync();
        var exports = await client.GetFromJsonAsync<List<DataExportResponse>>(new Uri("/api/v1/me/exports", UriKind.Relative), Cancel);
        var download = await client.GetAsync(new Uri(Assert.Single(exports!).DownloadPath!, UriKind.Relative), Cancel);
        await using var stream = await download.Content.ReadAsStreamAsync(Cancel);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(entry => entry.FullName).Where(name => name.StartsWith("collection/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        var text = await new StreamReader(zip.GetEntry("collection/text/Rack 1.txt")!.Open()).ReadToEndAsync(Cancel);
        var locations = JsonDocument.Parse(await new StreamReader(zip.GetEntry("collection/locations.json")!.Open()).ReadToEndAsync(Cancel)).RootElement;

        Assert.Equal(
            ["collection/deck-tokens.json", "collection/inventories.json", "collection/locations.json", "collection/movements.json", "collection/settings.json", "collection/shares.json", "collection/storage-rules.json", "collection/text/Rack 1.txt"],
            entries);
        Assert.Equal("1 Sol Ring (SLZ) 114\n\n// Box A\n12 Forest\n", text);
        Assert.Equal("Box A", locations[0].GetProperty("sections")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task DeletionRemovesTheOwnerFromAllTheTables()
    {
        using var client = await instance.NewUserAsync();
        var ownerId = await client.AccountIdAsync();
        var rack = await client.CreateLocationAsync("Rack", "rack");
        var box = await client.CreateLocationAsync("Box", null, rack.Id);
        await client.AddAsync(box.Id, new { name = "Forest", quantity = 3 });
        var decks = (await instance.GetAsync()).Services.GetRequiredService<IAssembledDeckLocations>();
        var deckId = Guid.CreateVersion7();
        await decks.EnsureLocationAsync(ownerId, deckId, "Deck", Cancel);
        await decks.SetContentAsync(ownerId, deckId, [new(await instance.OracleIdAsync("Sol Ring"), null, "en", null, 1), new(await instance.OracleIdAsync("Goblin"), null, "en", null, 1)], Cancel);
        await using (var connection = new NpgsqlConnection(instance.ConnectionString))
        {
            await connection.ExecuteAsync(
                "insert into users.collection_shares (id, owner_id, guest_id, location_ids, rights, created_at) values (@id, @ownerId, @guestId, array[@locationId], 'read', now())",
                new { id = Guid.CreateVersion7(), ownerId, guestId = Guid.CreateVersion7(), locationId = rack.Id });
        }

        await client.PostAsJsonAsync("/api/v1/me/deletion", new { password = "correct horse battery" }, Cancel);
        await MakeDeletionDueAsync(ownerId);
        await RunPersonalDataJobAsync();

        Assert.Empty(await FindOwnerAsync(ownerId));
    }

    /// <summary>
    /// Runs the personal data job of the worker one time. The job makes the pending exports and deletes the accounts after the delay.
    /// The test uses the public job, because the tests of a core component do not see the internal types of another core component.
    /// </summary>
    private async Task RunPersonalDataJobAsync()
    {
        await instance.EnsureWorkerAsync();
        var job = instance.Worker.GetServices<IScheduledJob>().Single(job => job.Name == "accounts.personal_data");
        await job.RunAsync(new JobRun(Force: false), Cancel);
    }

    /// <summary>
    /// Moves the deletion request of an account to a time before the deletion delay, so that the next job run deletes the account.
    /// </summary>
    private async Task MakeDeletionDueAsync(Guid accountId)
    {
        await using var connection = new NpgsqlConnection(instance.ConnectionString);
        var changed = await connection.ExecuteAsync(
            "update users.accounts set deletion_requested_at = deletion_requested_at - interval '8 days' where id = @accountId and deletion_requested_at is not null",
            new { accountId });
        Assert.Equal(1, changed);
    }

    /// <summary>
    /// Finds the columns that contain the owner ID, in all the tables except the outboxes.
    /// </summary>
    private async Task<List<string>> FindOwnerAsync(Guid ownerId)
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
            var condition = type == "uuid" ? $"\"{column}\" = @ownerId" : $"\"{column}\" && array[@ownerId]::uuid[]";
            if (await connection.ExecuteScalarAsync<bool>($"select exists (select 1 from \"{schema}\".\"{table}\" where {condition})", new { ownerId }))
            {
                found.Add($"{schema}.{table}.{column}");
            }
        }

        Assert.Contains(columns, column => column.Table == "location_entries");
        return found;
    }
}
