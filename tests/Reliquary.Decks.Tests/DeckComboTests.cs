using Reliquary.BuildingBlocks.Cards;
using Reliquary.Decks.Analysis;

namespace Reliquary.Decks.Tests;

public sealed class DeckComboTests
{
    private static readonly CardFacts Oracle = AnalysisCards.Normal("Thassa's Oracle", "{U}{U}", 2, "Creature — Merfolk Wizard", "", null, false, "U");
    private static readonly CardFacts Consultation = AnalysisCards.Normal("Demonic Consultation", "{B}", 1, "Instant", "", null, false, "B");
    private static readonly CardFacts Pact = AnalysisCards.Normal("Tainted Pact", "{1}{B}", 2, "Instant", "", null, false, "B");
    private static readonly CardFacts Titan = AnalysisCards.Normal("Expensive Titan", "{6}{G}", 7, "Creature — Giant", "", null, false, "G");
    private static readonly CardFacts Engine = AnalysisCards.Normal("Expensive Engine", "{6}", 6, "Artifact", "");

    private static ComboFacts Combo(string id, bool gameEnding = true, int manaValueNeeded = 0, int? popularity = null, params (CardFacts Card, int Quantity)[] cards) =>
        new(id, [.. cards.Select(card => new ComboCard(card.Card.OracleId, card.Quantity))], gameEnding ? ["Win the game"] : ["Lock"], gameEnding, null, manaValueNeeded, popularity);

    private static ComboFacts Combo(string id, params CardFacts[] cards) => Combo(id, true, 0, null, [.. cards.Select(card => (card, 1))]);

    private static DeckAnalysis Analyze(ComboSearch? search, params (CardFacts Card, AnalysisLine Line)[] lines)
    {
        var cards = lines.Select(line => line.Card)
            .Concat([Oracle, Consultation, Pact, Titan, Engine, AnalysisCards.SolRing, AnalysisCards.LlanowarElves])
            .DistinctBy(card => card.OracleId)
            .ToDictionary(card => card.OracleId);
        return DeckAnalyzer.Analyze("commander", lines.Select(line => line.Line), cards, search);
    }

    [Fact]
    public void CompleteComboUsesTheCommanderAndTheMainZones()
    {
        var search = new ComboSearch(true, [Combo("742-1295", Consultation, Oracle)]);

        var analysis = Analyze(
            search,
            AnalysisCards.Entry(Oracle, zone: "commander"),
            AnalysisCards.Entry(Consultation));

        Assert.True(analysis.Combos.Available);
        var combo = Assert.Single(analysis.Combos.Complete);
        Assert.Equal("742-1295", combo.Id);
        Assert.Equal(new Uri("https://commanderspellbook.com/combo/742-1295/"), combo.Url);
        Assert.Equal(["Demonic Consultation", "Thassa's Oracle"], combo.Cards.Select(card => card.Name));
        Assert.All(combo.Cards, card => Assert.True(card.InDeck));
        Assert.Null(combo.MissingOracleId);
        Assert.Empty(analysis.Combos.Near);
    }

    [Fact]
    public void ComboWithOneMissingCardIsANearCombo()
    {
        var search = new ComboSearch(true,
        [
            Combo("742-1295", Consultation, Oracle),
            Combo("1295-3093", Pact, Oracle),
            Combo("three", Consultation, Pact, Titan),
        ]);

        // The sideboard is outside the analysis, so Demonic Consultation is missing.
        var analysis = Analyze(
            search,
            AnalysisCards.Entry(Oracle),
            AnalysisCards.Entry(Consultation, zone: "sideboard"));

        Assert.Empty(analysis.Combos.Complete);
        Assert.Equal(["742-1295", "1295-3093"], analysis.Combos.Near.Select(combo => combo.Id));
        Assert.Equal(2, analysis.Combos.NearCount);
        var near = analysis.Combos.Near[0];
        Assert.Equal(Consultation.OracleId, near.MissingOracleId);
        Assert.False(near.Cards.Single(card => card.OracleId == Consultation.OracleId).InDeck);
        Assert.True(near.Cards.Single(card => card.OracleId == Oracle.OracleId).InDeck);
    }

    [Fact]
    public void ComboThatNeedsTwoCopiesNeedsTheQuantity()
    {
        var search = new ComboSearch(true, [Combo("copies", true, 0, null, (AnalysisCards.SolRing, 2), (AnalysisCards.LlanowarElves, 1))]);

        var one = Analyze(search, AnalysisCards.Entry(AnalysisCards.SolRing), AnalysisCards.Entry(AnalysisCards.LlanowarElves));
        var two = Analyze(search, AnalysisCards.Entry(AnalysisCards.SolRing, 2), AnalysisCards.Entry(AnalysisCards.LlanowarElves));

        Assert.Equal(AnalysisCards.SolRing.OracleId, Assert.Single(one.Combos.Near).MissingOracleId);
        Assert.Single(two.Combos.Complete);
    }

    [Fact]
    public void NearCombosHaveALimit()
    {
        var others = Enumerable.Range(0, DeckCombos.MaxNearCombos + 5)
            .Select(index => AnalysisCards.Normal($"Partner {index}", "{1}", 1, "Artifact"))
            .ToList();
        var search = new ComboSearch(true, [.. others.Select((card, index) => Combo($"near-{index}", Oracle, card))]);
        var cards = others.Append(Oracle).ToDictionary(card => card.OracleId);

        var analysis = DeckAnalyzer.Analyze("commander", [AnalysisCards.Line(Oracle)], cards, search);

        Assert.Equal(DeckCombos.MaxNearCombos, analysis.Combos.Near.Count);
        Assert.Equal(DeckCombos.MaxNearCombos + 5, analysis.Combos.NearCount);
    }

    [Fact]
    public void EarlyTwoCardComboGivesBracket4()
    {
        var search = new ComboSearch(true, [Combo("742-1295", Consultation, Oracle)]);

        var analysis = Analyze(search, AnalysisCards.Entry(Oracle), AnalysisCards.Entry(Consultation));

        Assert.Equal(4, analysis.Bracket!.Bracket);
        var reason = Assert.Single(analysis.Bracket.Reasons);
        Assert.Equal(CommanderBracket.TwoCardCombos, reason.Code);
        Assert.Equal(["742-1295"], reason.ComboIds);
        Assert.Equal([Consultation.OracleId, Oracle.OracleId], reason.OracleIds);
        Assert.Empty(analysis.Bracket.Unchecked);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(7, 7)]
    public void ManaValueOfAComboIsTheLargerValue(int manaValueNeeded, int expected)
    {
        var search = new ComboSearch(true, [Combo("742-1295", true, manaValueNeeded, null, (Consultation, 1), (Oracle, 1))]);

        var analysis = Analyze(search, AnalysisCards.Entry(Oracle), AnalysisCards.Entry(Consultation));

        Assert.Equal(expected, Assert.Single(analysis.Combos.Complete).ManaValue);
    }

    [Fact]
    public void LateTwoCardComboGivesBracket3()
    {
        // 7 + 6 mana: more than the early-game limit of 6.
        var search = new ComboSearch(true, [Combo("late", Titan, Engine)]);

        var analysis = Analyze(search, AnalysisCards.Entry(Titan), AnalysisCards.Entry(Engine));

        Assert.Equal(3, analysis.Bracket!.Bracket);
        Assert.Equal(CommanderBracket.LateTwoCardCombos, Assert.Single(analysis.Bracket.Reasons).Code);
    }

    [Fact]
    public void ThreeCardComboAndComboThatDoesNotEndTheGameDoNotChangeTheBracket()
    {
        var search = new ComboSearch(true,
        [
            Combo("three", Consultation, Oracle, Pact),
            Combo("lock", false, 0, null, (AnalysisCards.SolRing, 1), (AnalysisCards.LlanowarElves, 1)),
        ]);

        var analysis = Analyze(
            search,
            AnalysisCards.Entry(Oracle),
            AnalysisCards.Entry(Consultation),
            AnalysisCards.Entry(Pact),
            AnalysisCards.Entry(AnalysisCards.SolRing),
            AnalysisCards.Entry(AnalysisCards.LlanowarElves));

        Assert.Equal(2, analysis.Combos.Complete.Count);
        Assert.Equal(1, analysis.Bracket!.Bracket);
        Assert.Empty(analysis.Bracket.Unchecked);
    }

    [Fact]
    public void WithoutComboDataTheBracketDoesNotCheckTheCombos()
    {
        var analysis = Analyze(null, AnalysisCards.Entry(Oracle), AnalysisCards.Entry(Consultation));

        Assert.False(analysis.Combos.Available);
        Assert.Empty(analysis.Combos.Complete);
        Assert.Equal(1, analysis.Bracket!.Bracket);
        Assert.Equal([CommanderBracket.TwoCardCombos], analysis.Bracket.Unchecked);
    }

    [Fact]
    public void OtherFormatsGetTheCombosWithoutABracket()
    {
        var search = new ComboSearch(true, [Combo("742-1295", Consultation, Oracle)]);
        var cards = new[] { Oracle, Consultation }.ToDictionary(card => card.OracleId);

        var analysis = DeckAnalyzer.Analyze("legacy", [AnalysisCards.Line(Oracle, 4), AnalysisCards.Line(Consultation, 4)], cards, search);

        Assert.Single(analysis.Combos.Complete);
        Assert.Null(analysis.Bracket);
    }
}
