using System.Net.Http.Json;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.Decks.Api;
using Reliquary.Decks.Legality;

namespace Reliquary.Decks.Tests;

public sealed class DeckLegalityTests
{
    private static readonly CardAnalysisFacts NoAnalysis = new([0], 0, 0, false, false, [], [], null);

    private static CardFacts Card(string name, string typeLine, string[] identity, string text = "", string[]? keywords = null, string commander = "legal", string modern = "legal") =>
        new(Guid.NewGuid(), name, "normal", 0, typeLine, identity,
            new Dictionary<string, string> { ["commander"] = commander, ["modern"] = modern, ["vintage"] = "restricted" },
            keywords ?? [], [], false, false, null, [new CardFaceFacts(0, name, null, 0, typeLine, text, [])], NoAnalysis);

    private static IReadOnlyList<LegalityProblem> Check(string format, params (string Zone, CardFacts Card, int Quantity)[] lines) =>
        DeckLegality.Check(format, lines.Select(line => (line.Zone, line.Card.OracleId, line.Quantity)), lines.Select(line => line.Card).DistinctBy(card => card.OracleId).ToDictionary(card => card.OracleId));

    private static readonly CardFacts Atraxa = Card("Atraxa, Praetors' Voice", "Legendary Creature — Phyrexian Angel Horror", ["W", "U", "B", "G"]);
    private static readonly CardFacts Forest = Card("Forest", "Basic Land — Forest", ["G"]);
    private static readonly CardFacts SolRing = Card("Sol Ring", "Artifact", []);

    [Fact]
    public void LegalCommanderDeckHasNoProblem() =>
        Assert.Empty(Check("commander", ("commander", Atraxa, 1), ("main", SolRing, 1), ("main", Forest, 98)));

    [Fact]
    public void CommanderDeckFindsEachProblem()
    {
        var bolt = Card("Lightning Bolt", "Instant", ["R"]);
        var crypt = Card("Mana Crypt", "Artifact", [], commander: "banned");

        var problems = Check("commander", ("commander", Atraxa, 1), ("main", SolRing, 2), ("main", bolt, 1), ("main", crypt, 1), ("main", Forest, 90));

        Assert.Equal(
            [
                (DeckLegality.CardBanned, crypt.OracleId, null, null),
                (DeckLegality.DeckSize, (Guid?)null, (int?)95, (int?)100),
                (DeckLegality.OutsideColorIdentity, bolt.OracleId, null, null),
                (DeckLegality.TooManyCopies, SolRing.OracleId, 2, 1),
            ],
            problems.Select(problem => (problem.Code, problem.OracleId, problem.Count, problem.Limit)).OrderBy(problem => problem.Code, StringComparer.Ordinal));
    }

    [Fact]
    public void CardsWithTheirOwnCopyRuleKeepTheirLimit()
    {
        var rats = Card("Relentless Rats", "Creature — Rat", ["B"], "A deck can have any number of cards named Relentless Rats.");
        var dwarves = Card("Seven Dwarves", "Creature — Dwarf", ["R"], "A deck can have up to seven cards named Seven Dwarves.");

        Assert.Equal(int.MaxValue, DeckLegality.CopyLimit(rats, 1));
        Assert.Equal(7, DeckLegality.CopyLimit(dwarves, 1));
        Assert.Equal(int.MaxValue, DeckLegality.CopyLimit(Forest, 1));
        Assert.Equal(1, DeckLegality.CopyLimit(SolRing, 1));
    }

    [Theory]
    [InlineData("Partner", "Partner", true)]
    [InlineData("Partner", "Friends forever", false)]
    [InlineData("Friends forever", "Friends forever", true)]
    public void PartnerKeywordsPair(string first, string second, bool expected) =>
        Assert.Equal(expected, DeckLegality.CanPair(
            Card("A", "Legendary Creature — Human", ["W"], keywords: [first]),
            Card("B", "Legendary Creature — Elf", ["G"], keywords: [second])));

    [Fact]
    public void BackgroundAndDoctorAndPartnerWithPairOnlyWithTheirCards()
    {
        var wilson = Card("Wilson, Refined Grizzly", "Legendary Creature — Bear Warrior", ["G"], keywords: ["Choose a background"]);
        var background = Card("Raised by Giants", "Legendary Enchantment — Background", ["G"]);
        var pir = Card("Pir, Imaginative Rascal", "Legendary Creature — Human", ["G"], "Partner with Toothy, Imaginary Friend", ["Partner with", "Partner"]);
        var toothy = Card("Toothy, Imaginary Friend", "Legendary Creature — Illusion", ["U"], "Partner with Pir, Imaginative Rascal", ["Partner with", "Partner"]);
        var thrasios = Card("Thrasios, Triton Hero", "Legendary Creature — Merfolk Wizard", ["G", "U"], keywords: ["Partner"]);
        var doctor = Card("The Tenth Doctor", "Legendary Creature — Time Lord Doctor", ["U", "R", "G"]);
        var rose = Card("Rose Tyler", "Legendary Creature — Human", ["W"], keywords: ["Doctor's companion"]);

        Assert.True(DeckLegality.CanPair(wilson, background));
        Assert.True(DeckLegality.CanBeCommander(background));
        Assert.True(DeckLegality.CanPair(pir, toothy));
        Assert.False(DeckLegality.CanPair(pir, thrasios));
        Assert.True(DeckLegality.CanPair(rose, doctor));
        Assert.False(DeckLegality.CanPair(wilson, thrasios));
    }

    [Fact]
    public void CommanderMustBeALegendaryCreatureOrSayItCanBe()
    {
        var grist = Card("Grist, the Hunger Tide", "Legendary Planeswalker — Grist", ["B", "G"], "Grist, the Hunger Tide can be your commander.");

        Assert.True(DeckLegality.CanBeCommander(grist));
        Assert.False(DeckLegality.CanBeCommander(SolRing));
        Assert.Contains(Check("commander", ("main", Forest, 100)), problem => problem.Code == DeckLegality.CommanderMissing);
    }

    [Fact]
    public void ConstructedFormatChecksFourCopiesAndTheSideboard()
    {
        var bolt = Card("Lightning Bolt", "Instant", ["R"]);
        var problems = Check("modern", ("main", bolt, 5), ("main", Forest, 55), ("sideboard", SolRing, 16));

        Assert.Contains(problems, problem => problem.Code == DeckLegality.TooManyCopies && problem.OracleId == bolt.OracleId && problem.Limit == 4);
        Assert.Contains(problems, problem => problem.Code == DeckLegality.SideboardSize && problem.Count == 16);
        Assert.DoesNotContain(problems, problem => problem.Code == DeckLegality.DeckSize);
    }

    [Fact]
    public void RestrictedCardPermitsOneCopy()
    {
        var problems = Check("vintage", ("main", SolRing, 2), ("main", Forest, 58));

        Assert.Contains(problems, problem => problem.Code == DeckLegality.CardRestricted);
    }
}

[Collection(DecksGroup.Name)]
public sealed class DeckLegalityEndpointTests(DecksInstance instance)
{
    [Fact]
    public async Task EndpointChecksTheDeckWithTheCardData()
    {
        using var client = await instance.NewUserAsync();
        var created = await client.PostAsJsonAsync("/api/v1/decks", new
        {
            name = "Atraxa",
            lines = new object[]
            {
                new { name = "Atraxa, Praetors' Voice", zone = "commander" },
                new { name = "Mana Crypt" },
                new { name = "Lightning Bolt" },
                new { name = "Forest", quantity = 97 },
            },
        }, TestContext.Current.CancellationToken);
        var deck = await created.Content.ReadFromJsonAsync<DeckResponse>(TestContext.Current.CancellationToken);

        var legality = await client.GetFromJsonAsync<DeckLegalityResponse>(new Uri($"/api/v1/decks/{deck!.Id}/legality", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(legality!.Legal);
        Assert.Equal(
            [DeckLegality.CardBanned, DeckLegality.OutsideColorIdentity],
            legality.Problems.Select(problem => problem.Code).Order(StringComparer.Ordinal));
    }
}
