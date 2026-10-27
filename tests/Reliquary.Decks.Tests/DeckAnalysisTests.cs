using System.Diagnostics;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.Cards.Catalog;
using Reliquary.Decks.Analysis;

namespace Reliquary.Decks.Tests;

/// <summary>
/// Fake cards for the analysis tests. The analysis facts come from the rules of the Cards component (ADR 0025).
/// </summary>
internal static class AnalysisCards
{
    public static CardFaceFacts Face(string name, string? cost, decimal manaValue, string typeLine, string text = "", params string[] colors) =>
        new(0, name, cost, manaValue, typeLine, text, colors);

    public static CardFacts Card(string layout, CardFaceFacts[] faces, string[]? produced = null, bool gameChanger = false, bool isExtra = false)
    {
        var indexed = faces.Select((face, index) => face with { Index = index }).ToList();
        return new CardFacts(
            Guid.NewGuid(),
            string.Join(" // ", indexed.Select(face => face.Name)),
            layout,
            indexed.Sum(face => face.ManaValue),
            string.Join(" // ", indexed.Select(face => face.TypeLine)),
            [.. indexed.SelectMany(face => face.Colors).Distinct()],
            new Dictionary<string, string> { ["commander"] = "legal" },
            [],
            produced ?? [],
            isExtra,
            gameChanger,
            null,
            indexed,
            CardAnalysisRules.Calculate(layout, indexed));
    }

    public static CardFacts Normal(string name, string? cost, decimal manaValue, string typeLine, string text = "", string[]? produced = null, bool gameChanger = false, params string[] colors) =>
        Card("normal", [Face(name, cost, manaValue, typeLine, text, colors)], produced, gameChanger);

    public static readonly CardFacts Forest = Normal("Forest", null, 0, "Basic Land — Forest", "({T}: Add {G}.)", ["G"]);
    public static readonly CardFacts Plains = Normal("Plains", null, 0, "Basic Land — Plains", "({T}: Add {W}.)", ["W"]);
    public static readonly CardFacts Island = Normal("Island", null, 0, "Basic Land — Island", "({T}: Add {U}.)", ["U"]);
    public static readonly CardFacts SolRing = Normal("Sol Ring", "{1}", 1, "Artifact", "{T}: Add {C}{C}.", ["C"]);
    public static readonly CardFacts LlanowarElves = Normal("Llanowar Elves", "{G}", 1, "Creature — Elf Druid", "{T}: Add {G}.", ["G"], false, "G");
    public static readonly CardFacts Cultivate = Normal("Cultivate", "{2}{G}", 3, "Sorcery", "Search your library for up to two basic land cards.", null, false, "G");
    public static readonly CardFacts Wrath = Normal("Wrath of God", "{2}{W}{W}", 4, "Sorcery", "Destroy all creatures. They can't be regenerated.", null, false, "W");

    public static readonly CardFacts FireIce = Card("split", [Face("Fire", "{1}{R}", 2, "Instant", "", "R"), Face("Ice", "{1}{U}", 2, "Instant", "", "U")]);
    public static readonly CardFacts TicketBooth = Card("split", [Face("Ticket Booth", "{2}{R}", 3, "Enchantment — Room", "", "R"), Face("Tunnel of Hate", "{4}{R}{R}", 6, "Enchantment — Room", "", "R")]);
    public static readonly CardFacts CutRibbons = Card("split", [Face("Cut", "{1}{R}", 2, "Sorcery", "", "R"), Face("Ribbons", "{X}{B}{B}", 2, "Sorcery", "Aftermath", "B")]);
    public static readonly CardFacts Bonecrusher = Card("adventure", [Face("Bonecrusher Giant", "{2}{R}", 3, "Creature — Giant", "", "R"), Face("Stomp", "{1}{R}", 2, "Instant — Adventure", "", "R")]);
    public static readonly CardFacts Shadelock = Card("prepare", [Face("Scathing Shadelock", "{4}{B}", 5, "Creature — Snake Warlock", "", "B"), Face("Venomous Words", "{B}", 1, "Sorcery", "", "B")]);
    public static readonly CardFacts Delver = Card("transform", [Face("Delver of Secrets", "{U}", 1, "Creature — Human Wizard", "", "U"), Face("Insectile Aberration", null, 0, "Creature — Human Insect", "Flying", "U")]);
    public static readonly CardFacts Azcanta = Card("transform", [Face("Search for Azcanta", "{1}{U}", 2, "Legendary Enchantment", "", "U"), Face("Azcanta, the Sunken Ruin", null, 0, "Legendary Land", "{T}: Add {U}.")]);
    public static readonly CardFacts Erayo = Card("flip", [Face("Erayo, Soratami Ascendant", "{1}{U}", 2, "Legendary Creature — Moonfolk Monk", "", "U"), Face("Erayo's Essence", null, 0, "Legendary Enchantment", "", "U")]);
    public static readonly CardFacts Bruna = Card("meld", [Face("Bruna, the Fading Light", "{5}{W}{W}", 7, "Legendary Creature — Angel Horror", "", "W")]);
    public static readonly CardFacts Shatterskull = Card("modal_dfc", [Face("Shatterskull Smashing", "{X}{R}{R}", 2, "Sorcery", "", "R"), Face("Shatterskull, the Hammer Pass", null, 0, "Land", "{T}: Add {R}.")], ["R"]);
    public static readonly CardFacts Shaile = Card("modal_dfc", [Face("Shaile, Dean of Radiance", "{1}{W}", 2, "Legendary Creature — Bird Cleric", "", "W"), Face("Embrose, Dean of Shadow", "{2}{B}{B}", 4, "Legendary Creature — Human Warlock", "", "B")]);
    public static readonly CardFacts Pathway = Card("modal_dfc", [Face("Brightclimb Pathway", null, 0, "Land", "{T}: Add {W}."), Face("Grimclimb Pathway", null, 0, "Land", "{T}: Add {B}.")], ["B", "W"]);
    public static readonly CardFacts DryadArbor = Normal("Dryad Arbor", null, 0, "Land Creature — Forest Dryad", "", ["G"], false, "G");

    public static AnalysisLine Line(CardFacts card, int quantity = 1, string zone = "main", params string[] categories) => new(zone, card.OracleId, quantity, categories);

    public static DeckAnalysis Analyze(string format, params (CardFacts Card, AnalysisLine Line)[] lines) =>
        DeckAnalyzer.Analyze(format, lines.Select(line => line.Line), lines.Select(line => line.Card).DistinctBy(card => card.OracleId).ToDictionary(card => card.OracleId));

    public static (CardFacts, AnalysisLine) Entry(CardFacts card, int quantity = 1, string zone = "main", params string[] categories) => (card, Line(card, quantity, zone, categories));
}

public sealed class DeckAnalysisTests
{
    private static int CurveAt(DeckAnalysis analysis, int manaValue) => analysis.ManaCurve.Single(point => point.ManaValue == manaValue).Count;

    private static int CountOf(IReadOnlyList<CountEntry> entries, string key) => entries.SingleOrDefault(entry => entry.Key == key)?.Count ?? 0;

    [Fact]
    public void EachLayoutFollowsTheRulesOfAdr0025()
    {
        var analysis = AnalysisCards.Analyze(
            "modern",
            AnalysisCards.Entry(AnalysisCards.SolRing),
            AnalysisCards.Entry(AnalysisCards.FireIce),
            AnalysisCards.Entry(AnalysisCards.TicketBooth),
            AnalysisCards.Entry(AnalysisCards.CutRibbons),
            AnalysisCards.Entry(AnalysisCards.Bonecrusher),
            AnalysisCards.Entry(AnalysisCards.Shadelock),
            AnalysisCards.Entry(AnalysisCards.Delver),
            AnalysisCards.Entry(AnalysisCards.Azcanta),
            AnalysisCards.Entry(AnalysisCards.Erayo),
            AnalysisCards.Entry(AnalysisCards.Bruna),
            AnalysisCards.Entry(AnalysisCards.Shatterskull),
            AnalysisCards.Entry(AnalysisCards.Shaile),
            AnalysisCards.Entry(AnalysisCards.Pathway),
            AnalysisCards.Entry(AnalysisCards.DryadArbor),
            AnalysisCards.Entry(AnalysisCards.Forest, 10));

        // Lands: the Forests, the Pathway (land front face), Dryad Arbor, and the modal land Shatterskull Smashing.
        // Search for Azcanta becomes a land only when it transforms, so it is not a land.
        Assert.Equal(13, analysis.LandCount);
        Assert.Equal(1, analysis.ModalLandCount);
        Assert.Equal(12, analysis.SpellCount);
        Assert.Equal(24, analysis.CardCount);

        // Curve: Sol Ring and Delver at 1; Fire // Ice, Cut // Ribbons, Azcanta, Erayo, Shatterskull and Shaile at 2;
        // Ticket Booth and Bonecrusher at 3; Shadelock at 5 (the first face of a prepared creature); Bruna at 7.
        Assert.Equal([0, 2, 6, 2, 0, 1, 0, 1], analysis.ManaCurve.Select(point => point.Count));
        Assert.Equal(2.67m, analysis.AverageManaValue);

        // Types: each card counts one time for each type of its castable faces.
        Assert.Equal(13, CountOf(analysis.Types, "land")); // The Forests, the Pathway, Dryad Arbor, and the land face of Shatterskull.
        Assert.Equal(7, CountOf(analysis.Types, "creature")); // Bonecrusher, Shadelock, Delver, Erayo, Bruna, Shaile, Dryad Arbor.
        Assert.Equal(2, CountOf(analysis.Types, "instant")); // Fire // Ice, Stomp.
        Assert.Equal(3, CountOf(analysis.Types, "sorcery")); // Cut // Ribbons, Venomous Words, Shatterskull Smashing.
        Assert.Equal(2, CountOf(analysis.Types, "enchantment")); // Ticket Booth, Search for Azcanta. Erayo's Essence is a back face.
        Assert.Equal(1, CountOf(analysis.Types, "artifact"));

        // Colors of the cards that are not lands, from the castable faces.
        Assert.Equal(2, CountOf(analysis.Colors, "W")); // Bruna, Shaile.
        Assert.Equal(4, CountOf(analysis.Colors, "U")); // Fire // Ice, Delver, Azcanta, Erayo.
        Assert.Equal(3, CountOf(analysis.Colors, "B")); // Cut // Ribbons, Shadelock, Embrose.
        Assert.Equal(5, CountOf(analysis.Colors, "R")); // Fire // Ice, Ticket Booth, Cut // Ribbons, Bonecrusher, Shatterskull.
        Assert.Equal(1, CountOf(analysis.Colors, "C")); // Sol Ring.
    }

    [Fact]
    public void ModalLandCountsAsALandAndAsASpell()
    {
        var analysis = AnalysisCards.Analyze("modern", AnalysisCards.Entry(AnalysisCards.Shatterskull, 4), AnalysisCards.Entry(AnalysisCards.Forest, 20));

        Assert.Equal(24, analysis.LandCount);
        Assert.Equal(4, analysis.ModalLandCount);
        Assert.Equal(4, CurveAt(analysis, 2));
        Assert.Equal(24, CountOf(analysis.Categories, "land"));
        Assert.Equal(24, analysis.Draws.Lands);
        var red = analysis.Mana.Colors.Single(color => color.Color == "R");
        Assert.Equal(4, red.LandSources);
        Assert.Equal(8, red.Symbols);
    }

    [Fact]
    public void OtherZonesAndExtraCardsAreOutOfTheAnalysis()
    {
        var token = AnalysisCards.Card("token", [AnalysisCards.Face("Soldier", null, 0, "Token Creature — Soldier")], isExtra: true);
        var analysis = AnalysisCards.Analyze(
            "commander",
            AnalysisCards.Entry(AnalysisCards.Bruna, 1, "commander"),
            AnalysisCards.Entry(AnalysisCards.Wrath, 1, "sideboard"),
            AnalysisCards.Entry(AnalysisCards.SolRing, 1, "considering"),
            AnalysisCards.Entry(token, 3, "tokens"),
            AnalysisCards.Entry(token, 1),
            AnalysisCards.Entry(AnalysisCards.Plains, 10));

        Assert.Equal(11, analysis.CardCount);
        Assert.Equal(1, analysis.SpellCount);
        Assert.Equal(10, analysis.Draws.LibrarySize);
    }

    [Fact]
    public void CategoriesCountEachLandAndTheCardsWithNoCategory()
    {
        var analysis = AnalysisCards.Analyze(
            "modern",
            AnalysisCards.Entry(AnalysisCards.Forest, 20),
            AnalysisCards.Entry(AnalysisCards.Pathway, 2, "main", "ramp"),
            AnalysisCards.Entry(AnalysisCards.Cultivate, 2, "main", "ramp", "land"),
            AnalysisCards.Entry(AnalysisCards.FireIce, 3, "main", "targeted_interaction", "card_draw"),
            AnalysisCards.Entry(AnalysisCards.Bruna, 4));

        Assert.Equal(24, CountOf(analysis.Categories, "land")); // 22 lands, and the 2 Cultivate that the user put in the category.
        Assert.Equal(4, CountOf(analysis.Categories, "ramp"));
        Assert.Equal(3, CountOf(analysis.Categories, "targeted_interaction"));
        Assert.Equal(3, CountOf(analysis.Categories, "card_draw"));
        Assert.Equal(4, CountOf(analysis.Categories, DeckAnalyzer.Uncategorized));
        Assert.Equal(DeckRules.Categories.Count + 1, analysis.Categories.Count);
    }

    [Fact]
    public void EmptyDeckGivesZeros()
    {
        var analysis = DeckAnalyzer.Analyze("commander", [], new Dictionary<Guid, CardFacts>());

        Assert.Equal(0, analysis.CardCount);
        Assert.Equal(0, analysis.AverageManaValue);
        Assert.Empty(analysis.Mana.Colors);
        Assert.All(analysis.Draws.Checkpoints, checkpoint => Assert.All(checkpoint.AtLeast, probability => Assert.Equal(0, probability.Probability)));
        Assert.Equal(1, analysis.Bracket!.Bracket);
    }

    [Fact]
    public void DrawProbabilitiesUseTheMainZone()
    {
        var analysis = AnalysisCards.Analyze(
            "commander",
            AnalysisCards.Entry(AnalysisCards.Bruna, 1, "commander"),
            AnalysisCards.Entry(AnalysisCards.Plains, 38),
            AnalysisCards.Entry(AnalysisCards.Wrath, 61));

        Assert.Equal(99, analysis.Draws.LibrarySize);
        Assert.Equal(38, analysis.Draws.Lands);
        Assert.Equal(
            [("opening_hand", 7), ("turn_2_play", 8), ("turn_2_draw", 9), ("turn_3_play", 9), ("turn_3_draw", 10), ("turn_4_play", 10), ("turn_4_draw", 11)],
            analysis.Draws.Checkpoints.Select(checkpoint => (checkpoint.Code, checkpoint.Cards)));
        var opening = analysis.Draws.Checkpoints[0];
        Assert.Equal(0.5480, opening.AtLeast.Single(probability => probability.Lands == 3).Probability);
        var turn4 = analysis.Draws.Checkpoints.Single(checkpoint => checkpoint.Code == "turn_4_play");
        Assert.Equal(0.5828, turn4.AtLeast.Single(probability => probability.Lands == 4).Probability);
    }
}

/// <summary>
/// The time of the analysis engine. The test runs alone, after the tests that run in parallel, so that the other tests do not change the measure.
/// </summary>
[Collection(AnalysisPerformanceGroup.Name)]
public sealed class DeckAnalysisPerformanceTests
{
    [Fact]
    public void AnalysisOfOneHundredCardsIsFast()
    {
        var cards = new List<(CardFacts Card, AnalysisLine Line)>
        {
            AnalysisCards.Entry(AnalysisCards.Bruna, 1, "commander"),
            AnalysisCards.Entry(AnalysisCards.Plains, 18),
            AnalysisCards.Entry(AnalysisCards.Island, 18),
            AnalysisCards.Entry(AnalysisCards.Shatterskull, 1, "main", "land"),
        };
        for (var index = 0; index < 62; index++)
        {
            var colors = index % 2 == 0 ? new[] { "W" } : ["U", "W"];
            var cost = index % 2 == 0 ? "{2}{W}{W}" : "{1}{W}{U}";
            var text = index % 7 == 0 ? "Take an extra turn after this one." : "Draw a card.";
            var produced = index % 5 == 0 ? new[] { "W" } : null;
            var card = AnalysisCards.Normal($"Spell {index}", cost, 3 + (index % 4), "Creature — Test", text, produced, index % 11 == 0, colors);
            cards.Add(AnalysisCards.Entry(card, 1, "main", index % 3 == 0 ? "ramp" : "card_draw"));
        }

        var lines = cards.Select(entry => entry.Line).ToList();
        var facts = cards.Select(entry => entry.Card).DistinctBy(card => card.OracleId).ToDictionary(card => card.OracleId);
        Assert.Equal(100, DeckAnalyzer.Analyze("commander", lines, facts).CardCount);

        // The first runs compile the code. The measure starts after them.
        for (var run = 0; run < 500; run++)
        {
            DeckAnalyzer.Analyze("commander", lines, facts);
        }

        const int Runs = 500;
        var watch = Stopwatch.StartNew();
        for (var run = 0; run < Runs; run++)
        {
            DeckAnalyzer.Analyze("commander", lines, facts);
        }

        watch.Stop();
        var average = watch.Elapsed.TotalMilliseconds / Runs;
        TestContext.Current.TestOutputHelper?.WriteLine($"Average time of the analysis of 100 cards: {average:F3} ms.");

        // The goal is less than 1 ms with a Release build. The limit of the test is higher for the Debug builds and the slow CI runners.
        Assert.True(average < 5, $"The analysis of 100 cards took {average:F3} ms on average.");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnalysisPerformanceGroup
{
    public const string Name = "Analysis performance";
}

public sealed class HypergeometricTests
{
    [Theory]
    [InlineData(60, 24, 7, 2, 0.8573441200898213)]
    [InlineData(60, 24, 7, 3, 0.5879294964471378)]
    [InlineData(99, 38, 7, 3, 0.5480304292287325)]
    [InlineData(99, 38, 10, 4, 0.5827946462484221)]
    [InlineData(99, 37, 7, 2, 0.8141769669242799)]
    [InlineData(40, 17, 7, 1, 0.9868503118503118)]
    [InlineData(4, 2, 2, 1, 5.0 / 6.0)]
    public void AtLeastAgreesWithExactFractions(int population, int successes, int draws, int minimum, double expected)
    {
        Assert.Equal(expected, Hypergeometric.AtLeast(population, successes, draws, minimum), 12);
        Assert.Equal(expected, Hypergeometric.AtLeastEach(population, successes, draws, 7)[minimum], 12);
    }

    [Fact]
    public void ExactlyAgreesWithAnExactFraction() =>
        Assert.Equal(0.29571837521727495, Hypergeometric.Exactly(99, 38, 7, 3), 12);

    [Fact]
    public void ProbabilitiesOfAllCountsAddToOne()
    {
        var total = Enumerable.Range(0, 8).Sum(count => Hypergeometric.Exactly(99, 38, 7, count));

        Assert.Equal(1, total, 12);
    }

    [Fact]
    public void EdgeCasesHaveTheirLimits()
    {
        Assert.Equal(1, Hypergeometric.AtLeast(99, 38, 7, 0));
        Assert.Equal(0, Hypergeometric.AtLeast(99, 0, 7, 1));
        Assert.Equal(0, Hypergeometric.AtLeast(99, 38, 7, 8));
        Assert.Equal(1, Hypergeometric.AtLeast(10, 10, 7, 7));
        Assert.Equal(0, Hypergeometric.AtLeast(0, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Hypergeometric.AtLeast(10, 11, 7, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Hypergeometric.AtLeast(10, 5, 11, 1));
    }

    [Fact]
    public void BinomialGivesTheKnownValues()
    {
        Assert.Equal(1, (int)Hypergeometric.Binomial(5, 0));
        Assert.Equal(10, (int)Hypergeometric.Binomial(5, 2));
        Assert.Equal(0, (int)Hypergeometric.Binomial(5, 6));
        Assert.Equal(System.Numerics.BigInteger.Parse("14887031544", System.Globalization.CultureInfo.InvariantCulture), Hypergeometric.Binomial(99, 7));
    }

    [Fact]
    public void LargePopulationsKeepAFiniteResult()
    {
        var probability = Hypergeometric.AtLeast(1_000_000, 400_000, 300, 100);

        Assert.InRange(probability, 0.99, 1);
    }
}

public sealed class ManaRequirementTests
{
    // The summary table of Frank Karsten, "How Many Sources Do You Need to Consistently Cast Your Spells? A 2022 Update".
    [Theory]
    [InlineData("5C", 1, 6, 9, 14)]
    [InlineData("4C", 1, 5, 9, 15)]
    [InlineData("3C", 1, 4, 10, 16)]
    [InlineData("2C", 1, 3, 12, 18)]
    [InlineData("5CC", 2, 7, 12, 20)]
    [InlineData("1C", 1, 2, 13, 19)]
    [InlineData("4CC", 2, 6, 13, 22)]
    [InlineData("C", 1, 1, 14, 19)]
    [InlineData("3CC", 2, 5, 15, 23)]
    [InlineData("4CCC", 3, 7, 16, 26)]
    [InlineData("2CC", 2, 4, 16, 26)]
    [InlineData("3CCC", 3, 6, 17, 28)]
    [InlineData("1CC", 2, 3, 18, 28)]
    [InlineData("2CCC", 3, 5, 19, 30)]
    [InlineData("CC", 2, 2, 21, 30)]
    [InlineData("1CCC", 3, 4, 21, 33)]
    [InlineData("1CCCC", 4, 5, 22, 36)]
    [InlineData("CCC", 3, 3, 23, 36)]
    [InlineData("CCCC", 4, 4, 24, 39)]
    public void RequirementsAgreeWithTheTablesOfKarsten(string cost, int pips, int manaValue, int sixty, int ninetyNine)
    {
        Assert.NotEmpty(cost);
        Assert.Equal(sixty, ManaRequirements.Required(60, pips, manaValue));
        Assert.Equal(ninetyNine, ManaRequirements.Required(99, pips, manaValue));
    }

    [Fact]
    public void ValuesOutsideTheTablesUseTheNearestCell()
    {
        Assert.Equal(14, ManaRequirements.Required(99, 1, 9)); // 8C uses 5C.
        Assert.Equal(36, ManaRequirements.Required(99, 4, 8)); // 4CCCC uses 1CCCC.
        Assert.Equal(39, ManaRequirements.Required(99, 5, 5)); // CCCCC uses CCCC.
        Assert.Equal(19, ManaRequirements.Required(99, 1, 0)); // A card with mana value 0 uses C.
        Assert.Equal(0, ManaRequirements.Required(99, 0, 3));
    }

    [Fact]
    public void SymbolsCountOnlyTheSymbolsOfOneColor()
    {
        var symbols = ManaRequirements.CountSymbols("{X}{2}{W}{W}{U/B}{G/P}{2/R}{C}{S}");

        Assert.Equal(new Dictionary<string, int> { ["W"] = 2, ["C"] = 1 }, symbols);
        Assert.Empty(ManaRequirements.CountSymbols(null));
    }

    [Fact]
    public void ManaSourcesCountLandsAndPartOfTheOtherPermanents()
    {
        var signet = AnalysisCards.Normal("Azorius Signet", "{2}", 2, "Artifact", "{1}, {T}: Add {W}{U}.", ["U", "W"]);
        var birds = AnalysisCards.Normal("Birds of Paradise", "{G}", 1, "Creature — Bird", "{T}: Add one mana of any color.", ["B", "G", "R", "U", "W"], false, "G");
        var ritual = AnalysisCards.Normal("Dark Ritual", "{B}", 1, "Instant", "Add {B}{B}{B}.", ["B"], false, "B");
        var hybrid = AnalysisCards.Normal("Hybrid", "{W/U}{W/U}{W/U}", 3, "Creature — Test", colors: ["W", "U"]);
        var analysis = AnalysisCards.Analyze(
            "commander",
            AnalysisCards.Entry(AnalysisCards.Plains, 20),
            AnalysisCards.Entry(AnalysisCards.Island, 16),
            AnalysisCards.Entry(signet, 4),
            AnalysisCards.Entry(birds, 2),
            AnalysisCards.Entry(ritual, 1),
            AnalysisCards.Entry(hybrid, 1),
            AnalysisCards.Entry(AnalysisCards.Wrath, 1, "commander"));

        Assert.Equal(99, analysis.Mana.TableDeckSize);
        var white = analysis.Mana.Colors.Single(color => color.Color == "W");

        // Wrath of God (2WW) needs 26 sources. The Signets (mana value 2) and the Birds (mana value 1) help with a card at 4.
        Assert.Equal(20, white.LandSources);
        Assert.Equal(6, white.OtherSources);
        Assert.Equal(20 + (4 * 0.75m) + (2 * 0.5m), white.Sources);
        Assert.Equal(26, white.Required);
        Assert.Equal(AnalysisCards.Wrath.OracleId, white.OracleId);
        Assert.Equal(DeckAnalyzer.Low, white.Status);
        Assert.Equal(2, white.Symbols);

        // The hybrid card does not make a requirement. No card needs blue mana.
        var blue = analysis.Mana.Colors.Single(color => color.Color == "U");
        Assert.Equal(DeckAnalyzer.NoRequirement, blue.Status);
        Assert.Null(blue.Required);

        // Dark Ritual is an instant: it is not a source. The Birds produce black mana.
        var black = analysis.Mana.Colors.Single(color => color.Color == "B");
        Assert.Equal(2, black.OtherSources);
        Assert.Equal(1, black.Symbols);
        Assert.Equal(19, black.Required);
        Assert.Equal(0, black.Sources); // The Birds have mana value 1: they do not help with a card at 1.
    }

    [Fact]
    public void GoldCardsNeedOneMoreSourceOfEachColor()
    {
        var teferi = AnalysisCards.Normal("Teferi, Time Raveler", "{1}{W}{U}", 3, "Legendary Planeswalker — Teferi", colors: ["W", "U"]);
        var analysis = AnalysisCards.Analyze("modern", AnalysisCards.Entry(teferi, 4), AnalysisCards.Entry(AnalysisCards.Plains, 13), AnalysisCards.Entry(AnalysisCards.Island, 12));

        Assert.Equal(60, analysis.Mana.TableDeckSize);
        var white = analysis.Mana.Colors.Single(color => color.Color == "W");
        var blue = analysis.Mana.Colors.Single(color => color.Color == "U");

        // Karsten: a 2W card needs 12 sources, so Teferi needs 13 white sources and 13 blue sources.
        Assert.Equal((13, DeckAnalyzer.Ok), (white.Required!.Value, white.Status));
        Assert.Equal((13, DeckAnalyzer.Low), (blue.Required!.Value, blue.Status));
    }

    [Fact]
    public void TheColorUsesTheCardWithTheLargestShortage()
    {
        var bolt = AnalysisCards.Normal("Lightning Bolt", "{R}", 1, "Instant", colors: ["R"]);
        var krenko = AnalysisCards.Normal("Krenko, Mob Boss", "{2}{R}{R}", 4, "Legendary Creature — Goblin Warrior", colors: ["R"]);
        var mountain = AnalysisCards.Normal("Mountain", null, 0, "Basic Land — Mountain", produced: ["R"]);
        var analysis = AnalysisCards.Analyze("modern", AnalysisCards.Entry(bolt, 4), AnalysisCards.Entry(krenko, 4), AnalysisCards.Entry(mountain, 15));

        var red = analysis.Mana.Colors.Single(color => color.Color == "R");
        Assert.Equal((krenko.OracleId, 16, 2, 4m), (red.OracleId!.Value, red.Required!.Value, red.Pips!.Value, red.ManaValue!.Value));
        Assert.Equal(DeckAnalyzer.Low, red.Status);
        Assert.Equal(12, red.Symbols);
    }
}

public sealed class CommanderNeedsTests
{
    private static Dictionary<string, int> Counts(int land = 37, int ramp = 10, int draw = 12, int interaction = 12, int wipes = 6) => new()
    {
        ["land"] = land,
        ["ramp"] = ramp,
        ["card_draw"] = draw,
        ["targeted_interaction"] = interaction,
        ["board_wipe"] = wipes,
        ["synergy"] = 20,
    };

    [Fact]
    public void DeckWithFiveRampCardsShowsARampNeed()
    {
        var ramp = CommanderNeeds.Calculate(Counts(ramp: 5), 3m, 10).Single(need => need.Category == "ramp");

        Assert.Equal(("ramp", 5, 10, 8, 15, CommanderNeeds.Low, -5), (ramp.Category, ramp.Count, ramp.Target!.Value, ramp.Min!.Value, ramp.Max!.Value, ramp.Status, ramp.Difference!.Value));
    }

    [Theory]
    [InlineData(3.0, 10, 38)]
    [InlineData(3.0, 0, 41)]
    [InlineData(2.0, 15, 33)]
    [InlineData(4.0, 5, 43)]
    public void LandTargetFollowsTheFormulaOfKarsten(double averageManaValue, int cheap, int expected) =>
        Assert.Equal(expected, CommanderNeeds.LandTarget((decimal)averageManaValue, cheap));

    [Theory]
    [InlineData(3.0, 10)]
    [InlineData(3.5, 11)]
    [InlineData(4.0, 12)]
    [InlineData(2.4, 9)]
    [InlineData(1.0, 8)]
    [InlineData(6.0, 14)]
    public void RampTargetFollowsTheAverageManaValue(double averageManaValue, int expected) =>
        Assert.Equal(expected, CommanderNeeds.RampTarget((decimal)averageManaValue));

    [Fact]
    public void EachCategoryGetsAStatus()
    {
        var needs = CommanderNeeds.Calculate(Counts(land: 44, draw: 12, interaction: 19, wipes: 2), 3m, 10).ToDictionary(need => need.Category);

        Assert.Equal(DeckRules.Categories, needs.Keys);
        Assert.Equal((CommanderNeeds.High, 6), (needs["land"].Status, needs["land"].Difference!.Value));
        Assert.Equal(CommanderNeeds.Ok, needs["card_draw"].Status);
        Assert.Equal(CommanderNeeds.High, needs["targeted_interaction"].Status);
        Assert.Equal((CommanderNeeds.Low, -4), (needs["board_wipe"].Status, needs["board_wipe"].Difference!.Value));
        Assert.Equal((CommanderNeeds.NoTarget, 20, (int?)null), (needs["synergy"].Status, needs["synergy"].Count, needs["synergy"].Target));
        Assert.Equal(CommanderNeeds.NoTarget, needs["win_condition"].Status);
        Assert.Equal(CommanderNeeds.NoTarget, needs["protection"].Status);
    }

    [Fact]
    public void AnalyzerGivesNeedsAndBracketForCommanderOnly()
    {
        var cheapRamp = AnalysisCards.Normal("Arcane Signet", "{2}", 2, "Artifact", produced: ["C"]);
        var commander = AnalysisCards.Analyze(
            "commander",
            AnalysisCards.Entry(AnalysisCards.Bruna, 1, "commander"),
            AnalysisCards.Entry(cheapRamp, 1, "main", "ramp"),
            AnalysisCards.Entry(AnalysisCards.Plains, 40));
        var modern = AnalysisCards.Analyze("modern", AnalysisCards.Entry(AnalysisCards.Plains, 40));

        // Average mana value (7 + 2) / 2 = 4.5, and one cheap ramp card: 31.42 + 3.13 × 4.5 − 0.28 = 45.225.
        Assert.Equal(45, commander.Needs!.Single(need => need.Category == "land").Target);
        Assert.Equal(40, commander.Needs!.Single(need => need.Category == "land").Count);
        Assert.NotNull(commander.Bracket);
        Assert.Null(modern.Needs);
        Assert.Null(modern.Bracket);
    }
}

public sealed class CommanderBracketTests
{
    private static CardFacts Spell(string name, string text, bool gameChanger = false) =>
        AnalysisCards.Normal(name, "{2}{U}", 3, "Sorcery", text, null, gameChanger, "U");

    private static BracketAnalysis Evaluate(params CardFacts[] cards) => CommanderBracket.Evaluate(cards);

    [Fact]
    public void DeckWithNoRestrictedCardIsBracketOne()
    {
        var bracket = Evaluate(AnalysisCards.SolRing, AnalysisCards.Wrath, AnalysisCards.Forest);

        Assert.Equal(1, bracket.Bracket);
        Assert.Empty(bracket.Reasons);
        Assert.Equal([CommanderBracket.TwoCardCombos], bracket.Unchecked);
    }

    [Fact]
    public void OneToThreeGameChangersGiveBracketThree()
    {
        var rhystic = Spell("Rhystic Study", "Whenever an opponent casts a spell, you may draw a card unless that player pays {1}.", true);
        var bracket = Evaluate(rhystic, AnalysisCards.SolRing);

        Assert.Equal(3, bracket.Bracket);
        Assert.Equal((CommanderBracket.GameChangers, 3), (bracket.Reasons.Single().Code, bracket.Reasons.Single().Bracket));
        Assert.Equal([rhystic.OracleId], bracket.Reasons.Single().OracleIds);
    }

    [Fact]
    public void MoreThanThreeGameChangersGiveBracketFour()
    {
        var bracket = Evaluate([.. Enumerable.Range(0, 4).Select(index => Spell($"Changer {index}", string.Empty, true))]);

        Assert.Equal(4, bracket.Bracket);
        Assert.Equal(4, bracket.Reasons.Single().OracleIds.Count);
    }

    [Fact]
    public void ExtraTurnsGiveBracketTwoInLowQuantities()
    {
        var timeWarp = Spell("Time Warp", "Target player takes an extra turn after this one.");
        var bracket = Evaluate(timeWarp);

        Assert.Equal(2, bracket.Bracket);
        Assert.Equal(CommanderBracket.ExtraTurns, bracket.Reasons.Single().Code);
    }

    [Fact]
    public void MoreThanThreeExtraTurnCardsGiveBracketFour()
    {
        var bracket = Evaluate(
            Spell("Time Warp", "Target player takes an extra turn after this one."),
            Spell("Time Stretch", "Target player takes two extra turns after this one."),
            Spell("Nexus of Fate", "Take an extra turn after this one."),
            Spell("Medomai", "Whenever Medomai deals combat damage to a player, take an extra turn after this one."));

        Assert.Equal(4, bracket.Bracket);
        Assert.Equal((CommanderBracket.ExtraTurns, 4), (bracket.Reasons.Single().Code, bracket.Reasons.Single().Bracket));
    }

    [Theory]
    [InlineData("Armageddon", "Destroy all lands.")]
    [InlineData("Jokulhaups", "Destroy all artifacts, creatures, and lands. They can't be regenerated.")]
    [InlineData("Sunder", "Return all lands to their owners' hands.")]
    [InlineData("Wildfire", "Each player sacrifices four lands. Wildfire deals 4 damage to each creature.")]
    [InlineData("Winter Orb", "As long as Winter Orb is untapped, players can't untap more than one land during their untap steps.")]
    [InlineData("Static Orb", "As long as Static Orb is untapped, players can't untap more than two permanents during their untap steps.")]
    [InlineData("Back to Basics", "Nonbasic lands don't untap during their controllers' untap steps.")]
    [InlineData("Blood Moon", "Nonbasic lands are Mountains.")]
    public void MassLandDenialCardsGiveBracketFour(string name, string text)
    {
        var card = Spell(name, text);

        Assert.True(CommanderBracket.IsMassLandDenial(card));
        var bracket = Evaluate(card);
        Assert.Equal(4, bracket.Bracket);
        Assert.Equal(CommanderBracket.MassLandDenial, bracket.Reasons.Single().Code);
    }

    [Theory]
    [InlineData("Destroy all creatures. They can't be regenerated.")]
    [InlineData("Destroy target land.")]
    [InlineData("Return all land cards from your graveyard to the battlefield tapped.")]
    [InlineData("Exile all lands you control.")]
    [InlineData("Each player sacrifices a land.")]
    [InlineData("Search your library for up to two basic land cards.")]
    public void OtherLandCardsAreNotMassLandDenial(string text) =>
        Assert.False(CommanderBracket.IsMassLandDenial(Spell("Test", text)));

    [Theory]
    [InlineData("If a player would begin an extra turn, that player skips that turn instead.")]
    [InlineData("Your opponents can't take extra turns.")]
    public void TextsThatStopExtraTurnsAreNotExtraTurnCards(string text) =>
        Assert.False(CommanderBracket.IsExtraTurn(Spell("Test", text)));

    [Fact]
    public void BracketIsTheHighestBracketOfTheReasons()
    {
        var bracket = Evaluate(
            Spell("Changer", string.Empty, true),
            Spell("Time Warp", "Target player takes an extra turn after this one."),
            Spell("Armageddon", "Destroy all lands."));

        Assert.Equal(4, bracket.Bracket);
        Assert.Equal(
            [(CommanderBracket.GameChangers, 3), (CommanderBracket.MassLandDenial, 4), (CommanderBracket.ExtraTurns, 2)],
            bracket.Reasons.Select(reason => (reason.Code, reason.Bracket)));
    }
}
