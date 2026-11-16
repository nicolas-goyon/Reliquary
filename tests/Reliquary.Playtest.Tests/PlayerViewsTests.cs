using System.Text.Json;
using System.Text.Json.Nodes;
using Reliquary.Playtest.Engine;
using Reliquary.Playtest.Views;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// Checks the C# player views, canonical JSON, view hashes and zone patches against the shared vectors of web/src/playtest/views.test.ts.
/// See tests/vectors/views/README.md.
/// </summary>
public sealed class PlayerViewsTests
{
    private static readonly string[] Viewers = ["p1", "p2", "p3", PlayerViews.PublicViewerId];

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "vectors", "views");

    private static JsonArray Items(string file) => LenientJson.Parse(File.ReadAllText(Path.Combine(Folder, file)))!["items"]!.AsArray();

    public static TheoryData<int> CanonicalItems() => new(Enumerable.Range(0, Items("canonical-json.json").Count));

    public static TheoryData<int> ViewItems() => new(Enumerable.Range(0, Items("views.json").Count));

    public static TheoryData<int> ProjectionItems() => new(Enumerable.Range(0, Items("projections.json").Count));

    public static TheoryData<int> PatchItems() => new(Enumerable.Range(0, Items("patches.json").Count));

    [Theory]
    [MemberData(nameof(CanonicalItems))]
    public void CanonicalJsonAndHashAreTheOnesOfTheTypeScriptCode(int index)
    {
        var item = Items("canonical-json.json")[index]!;

        var json = CanonicalJson.Write(item["value"]);

        Assert.Equal(item["canonicalJson"]!.GetValue<string>(), json);
        Assert.Equal(item["hash"]!.GetValue<string>(), CanonicalJson.Fnv1a64(json));
    }

    [Theory]
    [MemberData(nameof(ViewItems))]
    public void ViewHasTheCanonicalJsonAndTheHashOfTheTypeScriptCode(int index)
    {
        var item = Items("views.json")[index]!;
        var view = item["view"].Deserialize<TableState>(TableJson.Options)!;

        // The typed view gives the same canonical JSON as the JSON of the vector: no field is lost or added.
        Assert.Equal(item["canonicalJson"]!.GetValue<string>(), CanonicalJson.Write(PlayerViews.ToJson(view)));
        Assert.Equal(item["canonicalJson"]!.GetValue<string>(), CanonicalJson.Write(item["view"]));
        Assert.Equal(item["hash"]!.GetValue<string>(), PlayerViews.Hash(view));
    }

    [Theory]
    [MemberData(nameof(ProjectionItems))]
    public void ViewOfAFullStateIsTheViewOfTheTypeScriptCode(int index)
    {
        var item = Items("projections.json")[index]!;
        var state = item["state"].Deserialize<TableState>(TableJson.Options)!;
        var hashes = item["hashes"]!.AsObject();

        Assert.Equal(5, hashes.Count);
        foreach (var (viewerId, hash) in hashes)
        {
            Assert.Equal(hash!.GetValue<string>(), PlayerViews.Hash(PlayerViews.View(state, viewerId)));
        }
    }

    [Theory]
    [MemberData(nameof(PatchItems))]
    public void PatchIsThePatchOfTheTypeScriptCode(int index)
    {
        var item = Items("patches.json")[index]!;
        var before = item["before"].Deserialize<TableState>(TableJson.Options)!;
        var patch = item["patch"].Deserialize<List<ViewChange>>(TableJson.Options)!;

        var patched = PlayerViews.Apply(before, patch);

        Assert.Equal(item["afterHash"]!.GetValue<string>(), PlayerViews.Hash(patched));
        // The C# patch has the same JSON as the TypeScript patch.
        Assert.Equal(CanonicalJson.Write(item["patch"]), CanonicalJson.Write(JsonSerializer.SerializeToNode(patch, TableJson.Options)));
        if (item["after"] is { } afterJson)
        {
            Assert.Equal(CanonicalJson.Write(afterJson), CanonicalJson.Write(PlayerViews.ToJson(patched)));
        }

        // The patched view has the hash of the view after, so it is the view after. The C# diff must give the TypeScript patch, in the same order.
        var diff = PlayerViews.Diff(before, patched);
        Assert.Equal(CanonicalJson.Write(item["patch"]), CanonicalJson.Write(JsonSerializer.SerializeToNode(diff, TableJson.Options)));
    }

    [Fact]
    public void PatchVectorsHaveAllTheKindsOfChangeAndFullViews()
    {
        var items = Items("patches.json");
        var changes = items.SelectMany(item => item!["patch"]!.AsArray()).Select(change => change!.Deserialize<ViewChange>(TableJson.Options)).ToList();

        Assert.Contains(changes, change => change is ZoneChange);
        Assert.Contains(changes, change => change is PlayerFieldChange);
        Assert.Contains(changes, change => change is TableFieldChange);
        Assert.True(items.Count(item => item!["after"] is not null) >= 5);
    }

    [Fact]
    public void FnvHashGivesThePublishedTestValues()
    {
        Assert.Equal("cbf29ce484222325", CanonicalJson.Fnv1a64(string.Empty));
        Assert.Equal("af63dc4c8601ec8c", CanonicalJson.Fnv1a64("a"));
        Assert.Equal("85944171f73967e8", CanonicalJson.Fnv1a64("foobar"));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("9007199254740992")]
    [InlineData("1e300")]
    public void CanonicalJsonRefusesANumberThatIsNotASafeInteger(string number)
    {
        Assert.Throws<ArgumentException>(() => CanonicalJson.Write(JsonNode.Parse(number)));
    }

    [Fact]
    public void CanonicalJsonWritesMinusZeroAsZeroAndAcceptsDoubleValues()
    {
        Assert.Equal("[0,0,3,-7]", CanonicalJson.Write(new JsonArray(JsonValue.Create(-0.0), JsonNode.Parse("-0"), JsonValue.Create(3.0), JsonValue.Create(-7L))));
    }

    [Fact]
    public void CanonicalJsonEscapesAsJavaScriptJsonStringify()
    {
        // Values of JSON.stringify in Node.js.
        Assert.Equal("\"\\\"\\\\/\\b\\f\\n\\r\\t\\u0000\\u001f\u007f\"", CanonicalJson.Write(JsonValue.Create("\"\\/\b\f\n\r\t\u0000\u001f\u007f")));
        Assert.Equal("\"é🐉\u2028\u2029<>&'\"", CanonicalJson.Write(JsonValue.Create("é🐉\u2028\u2029<>&'")));
        Assert.Equal("\"a\\ud800\"", CanonicalJson.Write(JsonValue.Create("a\ud800")));
        Assert.Equal("\"\\udc00\\ud800\"", CanonicalJson.Write(JsonValue.Create("\udc00\ud800")));
    }

    [Fact]
    public void ViewHidesTheLibrariesTheOtherHandsTheFaceDownCardsAndTheSeed()
    {
        var state = Table();
        var id = state.Players[1].Zones.Hand[0].Id;
        state = TableEngine.Apply(state, new MoveAction("p2", id, TableZone.Battlefield));
        state = TableEngine.Apply(state, new FlipAction("p2", id));
        state = TableEngine.Apply(state, new TapAction("p2", id));
        state = TableEngine.Apply(state, new CounterAction("p2", id, 2));

        var view = PlayerViews.View(state, "p1");
        var own = PlayerViews.View(state, "p2");

        Assert.Equal(0, view.Seed);
        Assert.Equal(state.Players[0].Zones.Hand, view.Players[0].Zones.Hand);
        Assert.Equal(state.Players[1].Zones.Battlefield, own.Players[1].Zones.Battlefield);
        Assert.Equal(new TableCard("hidden-p2-battlefield-0", string.Empty, string.Empty, null, false, 0, false, true, true, 2), view.Players[1].Zones.Battlefield[0]);
        Assert.Equal("hidden-p2-hand-0", view.Players[1].Zones.Hand[0].Id);
        Assert.Equal("hidden-p1-library-0", own.Players[0].Zones.Library[0].Id);
        Assert.Equal(state.Players[1].Zones.Command, view.Players[1].Zones.Command);

        var json = JsonSerializer.Serialize(PlayerViews.View(state, PlayerViews.PublicViewerId), TableJson.Options);
        foreach (var card in state.Players.SelectMany(player => player.Zones.Library.Concat(player.Zones.Hand)))
        {
            Assert.DoesNotContain($"\"{card.Id}\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PatchGivesTheNextViewForEachViewerDuringARandomGame()
    {
        var state = Table();
        var random = 7u;
        int Next(int max)
        {
            random = unchecked((random * 1103515245) + 12345);
            return (int)(random % (uint)max);
        }

        for (var step = 0; step < 200; step++)
        {
            var player = state.Players[Next(state.Players.Count)];
            var zone = Enum.GetValues<TableZone>()[Next(6)];
            var cards = player.Zones.Get(zone);
            var cardId = cards.Count > 0 ? cards[Next(cards.Count)].Id : "none";
            TableAction[] choices =
            [
                new DrawAction(player.Id), new ShuffleAction(player.Id), new NextTurnAction(), new TokenAction(player.Id, "Treasure"),
                new LifeAction(player.Id, Next(7) - 4), new CommanderDamageAction(player.Id, state.Players[0].Id, 1),
                new MoveAction(player.Id, cardId, Enum.GetValues<TableZone>()[Next(6)]), new MoveAction(player.Id, cardId, TableZone.Battlefield),
                new FlipAction(player.Id, cardId), new TapAction(player.Id, cardId), new CounterAction(player.Id, cardId, 1),
            ];
            var next = TableEngine.Apply(state, choices[Next(choices.Length)]);
            foreach (var viewer in Viewers)
            {
                var before = PlayerViews.View(state, viewer);
                var after = PlayerViews.View(next, viewer);
                var patched = PlayerViews.Apply(before, PlayerViews.Diff(before, after));
                Assert.Equal(PlayerViews.Hash(after), PlayerViews.Hash(patched));
            }

            state = next;
        }
    }

    [Fact]
    public void DiffGivesTheChangesInTheOrderOfViewsTs()
    {
        // The shared vectors have no step with two changed fields of one player, so this test checks the order of the player fields.
        var state = Table();
        var view = PlayerViews.View(state, "p3");
        var card = state.Players[0].Zones.Hand[0];
        var next = TableEngine.Apply(state, new MoveAction("p1", card.Id, TableZone.Battlefield));
        next = TableEngine.Apply(next, new MulliganAction("p2"));
        next = TableEngine.Apply(next, new LifeAction("p2", -3));
        next = TableEngine.Apply(next, new CommanderDamageAction("p2", "p1", 2));
        next = TableEngine.Apply(next, new PlayerCounterAction("p2", PlayerCounter.Poison, 1));
        next = TableEngine.Apply(next, new NextTurnAction());

        var names = PlayerViews.Diff(view, PlayerViews.View(next, "p3")).Select(change => change switch
        {
            TableFieldChange table => table.Field,
            PlayerFieldChange player => $"{player.Seat}.{player.Field}",
            ZoneChange zone => $"{zone.Seat}.{zone.Zone}",
            _ => "?",
        });

        Assert.Equal(
            ["activePlayerId", "turn", "p1.Hand", "p1.Battlefield", "p2.life", "p2.commanderDamage", "p2.counters", "p2.mulligans", "p2.cardsToBottom", "p2.Library", "p2.Hand"],
            names);
    }

    [Fact]
    public void DiffGivesNoChangeForEqualViewsAndRefusesOtherPlayers()
    {
        var view = PlayerViews.View(Table(), "p1");

        Assert.Empty(PlayerViews.Diff(view, view with { Players = [.. view.Players] }));
        Assert.Throws<ArgumentException>(() => PlayerViews.Diff(view, view with { Players = view.Players.Skip(1).ToList() }));
        Assert.Throws<ArgumentException>(() => PlayerViews.Apply(view, [new PlayerFieldChange("p9", "life", 1)]));
        Assert.Throws<ArgumentException>(() => PlayerViews.Apply(view, [new PlayerFieldChange("p1", "zones", 1)]));
    }

    private static TableState Table()
    {
        static List<SetupCard> Cards(string prefix, int count) =>
            Enumerable.Range(0, count).Select(index => new SetupCard($"{prefix}-{index}", $"oracle-{prefix}-{index}", $"Card {prefix} {index}", $"https://img/{prefix}/{index}", index % 3 == 0, index % 5)).ToList();
        var setups = Viewers.Take(3).Select(id => new PlayerSetup(id, $"Name {id}", Cards(id, 20), Cards($"{id}-cmd", 1))).ToList();
        return TableEngine.CreateTable(setups, 3, 40, commander: true);
    }
}
