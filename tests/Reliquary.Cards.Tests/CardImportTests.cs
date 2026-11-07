using System.IO.Compression;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace Reliquary.Cards.Tests;

[Collection(ImportedCardsGroup.Name)]
public sealed class CardImportTests(ImportedCardsFixture fixture)
{
    [Theory]
    [InlineData("Lightning Bolt", "normal", 1)]
    [InlineData("Delver of Secrets // Insectile Aberration", "transform", 2)]
    [InlineData("Shaile, Dean of Radiance // Embrose, Dean of Shadow", "modal_dfc", 2)]
    [InlineData("Fire // Ice", "split", 2)]
    [InlineData("Ticket Booth // Tunnel of Hate", "split", 2)]
    [InlineData("Bonecrusher Giant // Stomp", "adventure", 2)]
    [InlineData("Scathing Shadelock // Venomous Words", "prepare", 2)]
    [InlineData("Erayo, Soratami Ascendant // Erayo's Essence", "flip", 2)]
    [InlineData("Brisela, Voice of Nightmares", "meld", 1)]
    public async Task EachLayoutHasItsFaces(string name, string layout, int faceCount)
    {
        var cards = await fixture.GetAsync();
        await using var connection = new NpgsqlConnection(cards.Database.ConnectionString);

        var row = await connection.QuerySingleAsync<(string Layout, long Faces)>(
            """
            select o.layout, (select count(*) from reference.card_faces f where f.oracle_id = o.id)
            from reference.oracle_cards o where o.name = @name and not o.is_extra
            """,
            new { name });

        Assert.Equal(layout, row.Layout);
        Assert.Equal(faceCount, row.Faces);
    }

    [Fact]
    public async Task EachGameCardHasADefaultPrinting()
    {
        var cards = await fixture.GetAsync();
        await using var connection = new NpgsqlConnection(cards.Database.ConnectionString);

        var missing = await connection.ExecuteScalarAsync<long>("select count(*) from reference.oracle_cards where default_printing_id is null");

        Assert.Equal(0, missing);
    }

    [Fact]
    public async Task TagClosureContainsEachTagAndEachKnownParent()
    {
        var cards = await fixture.GetAsync();
        await using var connection = new NpgsqlConnection(cards.Database.ConnectionString);

        var missingSelf = await connection.ExecuteScalarAsync<long>(
            "select count(*) from reference.tags t where not exists (select 1 from reference.tag_closure c where c.ancestor_id = t.id and c.descendant_id = t.id)");
        var missingParent = await connection.ExecuteScalarAsync<long>(
            """
            select count(*) from reference.tags child
            join reference.tags parent on parent.id = any (child.parent_ids)
            where not exists (select 1 from reference.tag_closure c where c.ancestor_id = parent.id and c.descendant_id = child.id)
            """);
        var taggings = await connection.ExecuteScalarAsync<long>("select count(*) from reference.oracle_taggings");

        Assert.Equal(0, missingSelf);
        Assert.Equal(0, missingParent);
        Assert.True(taggings > 0);
    }

    [Fact]
    public async Task IdentifiersOfMtgjsonGoToThePrintings()
    {
        var cards = await fixture.GetAsync();
        await using var connection = new NpgsqlConnection(cards.Database.ConnectionString);

        var ids = await connection.QueryFirstAsync<string>(
            "select external_ids::text from reference.printings where external_ids ? 'mtgjsonUuid' limit 1");

        Assert.True(JsonDocument.Parse(ids).RootElement.TryGetProperty("mtgjsonUuid", out _));
    }

    [Fact]
    public async Task SecondImportOfTheSameFilesChangesNothing()
    {
        await using var cards = await CardsDatabase.CreateAsync();
        await cards.ImportAsync();
        var before = await ChecksumAsync(cards.Database.ConnectionString);

        await cards.ImportAsync();
        var after = await ChecksumAsync(cards.Database.ConnectionString);
        var lastEvent = await LastImportEventAsync(cards.Database.ConnectionString);

        Assert.Equal(before, after);
        Assert.Equal(0, lastEvent.GetProperty("changedCards").GetInt32());
        Assert.Equal(0, lastEvent.GetProperty("changedPrintings").GetInt32());
    }

    [Fact]
    public async Task ImportSkipsAFileThatDidNotChangeExceptWhenForced()
    {
        await using var cards = await CardsDatabase.CreateAsync();
        await cards.ImportAsync();

        var results = await cards.ImportAsync(force: false);

        Assert.All(results.Where(result => result.Status != "missing"), result => Assert.Equal("unchanged", result.Status));
    }

    [Theory]
    [InlineData(0.95, true)]
    [InlineData(0.5, false)]
    public async Task ImportDeletesMissingPrintingsOnlyWhenTheFileIsAlmostComplete(double share, bool expectDeletion)
    {
        var directory = Directory.CreateTempSubdirectory("reliquary-cards-");
        try
        {
            await using var cards = await CardsDatabase.CreateAsync(directory.FullName);
            var lines = await ReadFixtureLinesAsync();
            await File.WriteAllLinesAsync(Path.Combine(directory.FullName, "cards.jsonl"), lines, TestContext.Current.CancellationToken);
            await cards.ImportAsync();

            var kept = lines.Take((int)Math.Ceiling(lines.Count * share)).ToList();
            await File.WriteAllLinesAsync(Path.Combine(directory.FullName, "cards.jsonl"), kept, TestContext.Current.CancellationToken);
            await cards.ImportAsync();

            await using var connection = new NpgsqlConnection(cards.Database.ConnectionString);
            var count = await connection.ExecuteScalarAsync<long>("select count(*) from reference.printings");
            Assert.Equal(expectDeletion ? kept.Count : lines.Count, count);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<List<string>> ReadFixtureLinesAsync()
    {
        await using var file = File.OpenRead(Path.Combine(CardsDatabase.FixturesDirectory, "cards.jsonl.gz"));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static async Task<string> ChecksumAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<string>(
            """
            select md5(
                (select string_agg(t::text, '|' order by t.id) from (select id, content_hash, default_printing_id, is_extra from reference.oracle_cards) t) ||
                (select string_agg(t::text, '|' order by t.id) from (select id, content_hash, updated_at from reference.printings) t) ||
                (select string_agg(t::text, '|' order by t::text) from reference.card_names t) ||
                (select string_agg(t::text, '|' order by t::text) from reference.card_faces t))
            """) ?? string.Empty;
    }

    private static async Task<JsonElement> LastImportEventAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        var payload = await connection.QueryFirstAsync<string>(
            "select payload::text from reference.outbox where event_type = 'cards.card_data.imported' order by id desc limit 1");
        return JsonDocument.Parse(payload).RootElement.Clone();
    }
}
