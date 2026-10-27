using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.Cards.Import;

namespace Reliquary.Cards.Tests;

/// <summary>
/// The import of the combos of Commander Spellbook from the fixture <c>variants.json</c>, and the combo search.
/// </summary>
[Collection(ImportedCardsGroup.Name)]
public sealed class ComboImportTests(ImportedCardsFixture fixture)
{
    private static readonly Guid ThassasOracle = Guid.Parse("1de1b591-a73f-4974-b507-8c63e07a0868");
    private static readonly Guid DemonicConsultation = Guid.Parse("9a1412db-45ad-46ea-8f12-a85d203113d8");
    private static readonly Guid SolRing = Guid.Parse("6ad8011d-3471-4369-9d68-b264cc027487");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ImportKeepsOnlyTheCombosOfKnownCardsWithNoTemplate()
    {
        var cards = await fixture.GetAsync();
        await using var connection = new NpgsqlConnection(cards.Database.ConnectionString);

        // The fixture has three combos: Thassa's Oracle and Demonic Consultation, a combo with Tainted Pact (not a fixture card)
        // and a combo with a template.
        var rows = (await connection.QueryAsync<(string Id, Guid[] OracleIds, int[] Quantities, string[] Results, bool GameEnding, string ManaNeeded, int ManaValueNeeded)>(
            "select id, oracle_ids, quantities, results, game_ending, mana_needed, mana_value_needed from reference.combos")).ToList();
        var run = await connection.QuerySingleAsync<(string Status, int ItemCount)>(
            "select status, item_count from reference.import_runs where source = 'local:combos' order by started_at desc limit 1");

        var combo = Assert.Single(rows);
        Assert.Equal("742-1295", combo.Id);
        Assert.Equal([DemonicConsultation, ThassasOracle], combo.OracleIds);
        Assert.Equal([1, 1], combo.Quantities);
        Assert.Equal(["Exile your library", "Win the game"], combo.Results);
        Assert.True(combo.GameEnding);
        Assert.Equal(("{U}{U}{B}", 3), (combo.ManaNeeded, combo.ManaValueNeeded));
        Assert.Equal(("succeeded", 1), run);
    }

    [Fact]
    public async Task CatalogFindsTheCompleteComboAndTheComboThatNeedsOneMoreCard()
    {
        var catalog = (await fixture.GetAsync()).Services.GetRequiredService<IComboCatalog>();

        var complete = await catalog.FindCombosAsync([ThassasOracle, DemonicConsultation, SolRing], Cancel);
        var near = await catalog.FindCombosAsync([ThassasOracle], Cancel);
        var none = await catalog.FindCombosAsync([SolRing], Cancel);

        Assert.True(complete.Available);
        Assert.Equal("742-1295", Assert.Single(complete.Combos).Id);
        Assert.Equal(new Uri("https://commanderspellbook.com/combo/742-1295/"), complete.Combos[0].Url);
        Assert.Equal("742-1295", Assert.Single(near.Combos).Id);
        Assert.True(none.Available);
        Assert.Empty(none.Combos);
    }

    [Fact]
    public async Task CatalogTellsWhenTheInstanceHasNoComboData()
    {
        await using var cards = await CardsDatabase.CreateAsync();
        var catalog = cards.Services.GetRequiredService<IComboCatalog>();

        var search = await catalog.FindCombosAsync([ThassasOracle], Cancel);

        Assert.False(search.Available);
        Assert.Empty(search.Combos);
    }

    [Fact]
    public async Task SecondImportOfTheSameFileChangesNothing()
    {
        await using var cards = await CardsDatabase.CreateAsync();
        await cards.ImportAsync();
        var before = await CombosAsync(cards.Database.ConnectionString);

        await cards.ImportAsync();
        var after = await CombosAsync(cards.Database.ConnectionString);

        Assert.Single(before);
        Assert.Equal(before.Select(combo => combo.Text), after.Select(combo => combo.Text));
    }

    [Fact]
    public async Task ImportFindsACardByItsNameAndReplacesTheCombos()
    {
        var directory = Directory.CreateTempSubdirectory("reliquary-combos-");
        try
        {
            CopyCardFixtures(directory.FullName);
            await using var cards = await CardsDatabase.CreateAsync(directory.FullName);

            // The file gives no Oracle ID for Thassa's Oracle, so the import uses the name.
            await WriteCombosAsync(directory.FullName, Combo("1", (null, "Thassa's Oracle"), (DemonicConsultation, "Demonic Consultation")));
            await cards.ImportAsync();
            var first = await CombosAsync(cards.Database.ConnectionString);

            await WriteCombosAsync(directory.FullName, Combo("2", (SolRing, "Sol Ring"), (ThassasOracle, "Thassa's Oracle")));
            await cards.ImportAsync();
            var second = await CombosAsync(cards.Database.ConnectionString);

            Assert.Equal("1", Assert.Single(first).Id);
            Assert.Equal([ThassasOracle, DemonicConsultation], first[0].OracleIds);
            Assert.Equal("2", Assert.Single(second).Id);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ImportDoesNotDeleteTheCombosWhenTheFileIsMuchSmaller()
    {
        var directory = Directory.CreateTempSubdirectory("reliquary-combos-");
        try
        {
            CopyCardFixtures(directory.FullName);
            await using var cards = await CardsDatabase.CreateAsync(directory.FullName);
            await WriteCombosAsync(
                directory.FullName,
                Combo("1", (ThassasOracle, "Thassa's Oracle"), (DemonicConsultation, "Demonic Consultation")),
                Combo("2", (SolRing, "Sol Ring"), (ThassasOracle, "Thassa's Oracle")));
            await cards.ImportAsync();

            await WriteCombosAsync(directory.FullName, Combo("3", (SolRing, "Sol Ring"), (DemonicConsultation, "Demonic Consultation")));
            await cards.ImportAsync();

            Assert.Equal(["1", "2", "3"], (await CombosAsync(cards.Database.ConnectionString)).Select(combo => combo.Id));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("S", "Win the game", true)]
    [InlineData("H", "Infinite storm count", true)]
    [InlineData("C", "Near-infinite lifegain triggers", true)]
    [InlineData("H", "Lock", false)]
    public void GameEndingComboHasAnInfiniteOrAStandaloneResult(string status, string name, bool expected) =>
        Assert.Equal(expected, ComboImporter.IsGameEnding([new Import.Spellbook.SpellbookFeature { Name = name, Status = status }]));

    [Fact]
    public async Task RemoteSourceDownloadsTheBulkFileOfCommanderSpellbook()
    {
        var modified = new DateTimeOffset(2026, 10, 27, 21, 12, 29, TimeSpan.Zero);
        var json = """{"variants":[]}"""u8.ToArray();
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            await gzip.WriteAsync(json, Cancel);
        }

        var requests = new List<(HttpMethod Method, Uri? Uri)>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.Method == HttpMethod.Head ? [] : compressed.ToArray()) };
            response.Content.Headers.LastModified = modified;
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return response;
        }));
        var options = Options.Create(new CardImportOptions { DownloadDirectory = Path.GetTempPath() });
        using var sources = new RemoteCardDataSources(http, options);

        var file = await sources.FindAsync(SourceKinds.Combos, Cancel);
        await using var content = await file!.Open(Cancel);
        using var reader = new StreamReader(content, Encoding.UTF8);

        Assert.Equal(("spellbook:variants", modified), (file.Name, file.UpdatedAt));
        Assert.Equal("""{"variants":[]}""", await reader.ReadToEndAsync(Cancel));
        Assert.Equal([(HttpMethod.Head, options.Value.SpellbookCombos), (HttpMethod.Get, options.Value.SpellbookCombos)], requests);
    }

    private static string Combo(string id, params (Guid? OracleId, string Name)[] cards) => JsonSerializer.Serialize(new
    {
        id,
        status = "OK",
        uses = cards.Select(card => new { card = new { name = card.Name, oracleId = card.OracleId }, quantity = 1 }),
        requires = Array.Empty<object>(),
        produces = new[] { new { feature = new { name = "Win the game", status = "S" } } },
        manaNeeded = "",
        manaValueNeeded = 0,
    });

    private static Task WriteCombosAsync(string directory, params string[] combos) =>
        File.WriteAllTextAsync(
            Path.Combine(directory, "variants.json"),
            $$"""{"timestamp":"2026-10-27T00:00:00Z","version":"test","variants":[{{string.Join(',', combos)}}],"aliases":[]}""",
            Cancel);

    private static void CopyCardFixtures(string directory)
    {
        foreach (var name in new[] { "sets.json", "cards.jsonl.gz" })
        {
            File.Copy(Path.Combine(CardsDatabase.FixturesDirectory, name), Path.Combine(directory, name));
        }
    }

    private static async Task<List<StoredCombo>> CombosAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        var rows = await connection.QueryAsync<(string Id, Guid[] OracleIds, string Text)>(
            "select id, oracle_ids, c::text from reference.combos c order by id");
        return [.. rows.Select(row => new StoredCombo(row.Id, row.OracleIds, row.Text))];
    }

    /// <summary>A stored combo. <paramref name="Text"/> contains all the columns, so two equal values tell that the row did not change.</summary>
    private sealed record StoredCombo(string Id, Guid[] OracleIds, string Text);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
