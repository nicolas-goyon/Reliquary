using Reliquary.Playtest.Engine;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// Unit tests of the C# table engine. The conformance vectors (<see cref="TableEngineConformanceTests"/>) compare it with engine.ts.
/// </summary>
public sealed class TableEngineTests
{
    [Theory]
    // The values come from nextRandom of engine.ts in Node.js.
    [InlineData(0L, 0.26642920868471265, 1831565813L)]
    [InlineData(1L, 0.6270739405881613, 1831565814L)]
    [InlineData(42L, 0.6011037519201636, 1831565855L)]
    [InlineData(4294967295L, 0.8964226141106337, 1831565812L)]
    [InlineData(-1L, 0.8964226141106337, 1831565812L)]
    [InlineData(2147483648L, 0.8205775609239936, -315917835L)]
    [InlineData(-2147483648L, 0.8205775609239936, -315917835L)]
    public void RandomGeneratorGivesTheValuesOfTheTypeScriptEngine(long seed, double value, long next)
    {
        var random = TableEngine.NextRandom(seed);

        Assert.Equal(value, random.Value);
        Assert.Equal(next, random.Seed);
    }

    [Fact]
    public void ShuffleOfOneCardKeepsTheSeed()
    {
        var result = TableEngine.Shuffled(["a"], uint.MaxValue);

        Assert.Equal(uint.MaxValue, result.Seed);
    }

    [Fact]
    public void TableStartsWithAnOpeningHandAndACommandZone()
    {
        var state = Table(players: 2);

        Assert.All(state.Players, player =>
        {
            Assert.Equal(TableEngine.OpeningHandSize, player.Zones.Hand.Count);
            Assert.Equal(53, player.Zones.Library.Count);
            Assert.Single(player.Zones.Command);
        });
        Assert.Equal("p0", state.ActivePlayerId);
        Assert.Equal(1, state.Turn);
    }

    [Fact]
    public void SameSeedGivesTheSameTable()
    {
        Assert.Equal(
            TableStateTests.ToJson(Table(seed: 7)),
            TableStateTests.ToJson(Table(seed: 7)));
        Assert.NotEqual(
            Table(seed: 7).Players[0].Zones.Hand.Select(card => card.Id),
            Table(seed: 8).Players[0].Zones.Hand.Select(card => card.Id));
    }

    [Fact]
    public void ActionDoesNotChangeTheStateBefore()
    {
        var state = Table();
        var before = TableStateTests.ToJson(state);

        TableEngine.Apply(state, new MulliganAction("p0"));
        TableEngine.Apply(state, new MoveAction("p0", state.Players[0].Zones.Hand[0].Id, TableZone.Battlefield));
        TableEngine.Apply(state, new NextTurnAction());

        Assert.Equal(before, TableStateTests.ToJson(state));
    }

    [Fact]
    public void ActionOnAnUnknownPlayerOrCardDoesNotChangeTheState()
    {
        var state = Table();
        var before = TableStateTests.ToJson(state);

        Assert.Equal(before, TableStateTests.ToJson(TableEngine.Apply(state, new MoveAction("p0", "none", TableZone.Exile))));
        Assert.Equal(before, TableStateTests.ToJson(TableEngine.Apply(state, new ShuffleAction("nobody"))));
        Assert.Equal(before, TableStateTests.ToJson(TableEngine.Apply(state, new MulliganAction("nobody"))));
    }

    [Fact]
    public void DrawWithANegativeCountTakesTheCardsAsJavaScriptSlice()
    {
        // library.slice(0, -1) gives all the cards but the last one.
        var state = TableEngine.Apply(Table(), new DrawAction("p0", -1));

        Assert.Single(state.Players[0].Zones.Library);
        Assert.Equal(59, state.Players[0].Zones.Hand.Count);
    }

    [Fact]
    public void NextTurnOnATableWithNoPlayerDoesNotChangeTheState()
    {
        var state = TableEngine.CreateTable([], 1, 20);

        Assert.Same(state, TableEngine.Apply(state, new NextTurnAction()));
        Assert.Equal(string.Empty, state.ActivePlayerId);
    }

    private static TableState Table(int players = 1, long seed = 42)
    {
        static List<SetupCard> Cards(string prefix, int count) =>
            Enumerable.Range(0, count).Select(index => new SetupCard($"{prefix}-{index}", prefix, prefix, null, false, 1)).ToList();
        var setups = Enumerable.Range(0, players).Select(index => new PlayerSetup($"p{index}", $"P{index}", Cards($"c{index}", 60), Cards($"cmd{index}", 1))).ToList();
        return TableEngine.CreateTable(setups, seed, 20, commander: true);
    }
}
