using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.Cards.Catalog;

namespace Reliquary.Cards.Tests;

/// <summary>
/// Checks the analysis rules of ADR 0025 for each card layout.
/// </summary>
public sealed class CardAnalysisRulesTests
{
    private static CardFaceFacts Face(int index, string name, string? cost, decimal manaValue, string typeLine, params string[] colors) =>
        new(index, name, cost, manaValue, typeLine, null, colors);

    [Fact]
    public void CardWithOneFaceUsesItsOwnValues()
    {
        var facts = CardAnalysisRules.Calculate("normal", [Face(0, "Sol Ring", "{1}", 1, "Artifact")]);

        Assert.Equal([0], facts.CastableFaces);
        Assert.Equal(1, facts.CurveManaValue);
        Assert.False(facts.IsLand);
        Assert.Equal(["Artifact"], facts.CardTypes);
        Assert.Equal("{1}", facts.PrimaryManaCost);
    }

    [Fact]
    public void SplitCardUsesItsCheapestHalf()
    {
        var facts = CardAnalysisRules.Calculate("split",
            [Face(0, "Ticket Booth", "{2}{R}", 3, "Enchantment — Room", "R"), Face(1, "Tunnel of Hate", "{4}{R}{R}", 6, "Enchantment — Room", "R")]);
        var fireIce = CardAnalysisRules.Calculate("split",
            [Face(0, "Fire", "{1}{R}", 2, "Instant", "R"), Face(1, "Ice", "{1}{U}", 2, "Instant", "U")]);

        Assert.Equal([0, 1], facts.CastableFaces);
        Assert.Equal(3, facts.CurveManaValue);
        Assert.Equal(2, fireIce.CurveManaValue);
        Assert.Equal(0, fireIce.PrimaryFace);
        Assert.Equal(["U", "R"], fireIce.Colors);
    }

    [Theory]
    [InlineData("adventure")]
    [InlineData("prepare")]
    public void AdventureAndPreparedCreatureUseTheCreatureInTheCurveAndBothTypes(string layout)
    {
        var facts = CardAnalysisRules.Calculate(layout,
            [Face(0, "Bonecrusher Giant", "{2}{R}", 3, "Creature — Giant", "R"), Face(1, "Stomp", "{1}{R}", 2, "Instant — Adventure", "R")]);

        Assert.Equal([0, 1], facts.CastableFaces);
        Assert.Equal(3, facts.CurveManaValue);
        Assert.Equal(["Creature", "Instant"], facts.CardTypes);
        Assert.Equal("{2}{R}", facts.PrimaryManaCost);
    }

    [Theory]
    [InlineData("transform")]
    [InlineData("flip")]
    [InlineData("meld")]
    public void TransformingCardUsesItsFrontFaceOnly(string layout)
    {
        var facts = CardAnalysisRules.Calculate(layout,
            [Face(0, "Search for Azcanta", "{1}{U}", 2, "Legendary Enchantment", "U"), Face(1, "Azcanta, the Sunken Ruin", null, 0, "Legendary Land")]);

        Assert.Equal([0], facts.CastableFaces);
        Assert.Equal(2, facts.CurveManaValue);
        Assert.False(facts.IsLand);
        Assert.False(facts.IsModalLand);
        Assert.Equal(["Enchantment"], facts.CardTypes);
    }

    [Fact]
    public void ModalCardWithASpellFaceAndALandFaceIsAModalLand()
    {
        var facts = CardAnalysisRules.Calculate("modal_dfc",
            [Face(0, "Shatterskull Smashing", "{X}{R}{R}", 2, "Sorcery", "R"), Face(1, "Shatterskull, the Hammer Pass", null, 0, "Land")]);

        Assert.False(facts.IsLand);
        Assert.True(facts.IsModalLand);
        Assert.Equal(2, facts.CurveManaValue);
        Assert.Equal(["Land", "Sorcery"], facts.CardTypes);
    }

    [Fact]
    public void ModalCardWithTwoLandFacesIsALand()
    {
        var facts = CardAnalysisRules.Calculate("modal_dfc",
            [Face(0, "Clearwater Pathway", null, 0, "Land"), Face(1, "Murkwater Pathway", null, 0, "Land")]);

        Assert.True(facts.IsLand);
        Assert.False(facts.IsModalLand);
    }

    [Fact]
    public void LandCreatureIsALand()
    {
        var facts = CardAnalysisRules.Calculate("normal", [Face(0, "Dryad Arbor", null, 0, "Land Creature — Forest Dryad", "G")]);

        Assert.True(facts.IsLand);
        Assert.Equal(["Creature", "Land"], facts.CardTypes);
    }

    [Fact]
    public void CardWithNoFaceHasEmptyFacts() =>
        Assert.Empty(CardAnalysisRules.Calculate("normal", []).CastableFaces);
}

[Collection(ImportedCardsGroup.Name)]
public sealed class CardCatalogTests(ImportedCardsFixture fixture)
{
    private async Task<ICardCatalog> CatalogAsync() =>
        (await fixture.GetAsync()).Services.GetRequiredService<ICardCatalog>();

    [Fact]
    public async Task CatalogGivesTheFactsOfEachLayout()
    {
        var catalog = await CatalogAsync();
        var names = await catalog.ResolveNamesAsync(
            ["Fire // Ice", "Ticket Booth // Tunnel of Hate", "Bonecrusher Giant // Stomp", "Delver of Secrets // Insectile Aberration", "Sol Ring", "Forest"],
            TestContext.Current.CancellationToken);
        var cards = await catalog.GetCardsAsync([.. names.Values.Select(match => match.OracleId!.Value)], TestContext.Current.CancellationToken);
        CardFacts Card(string name) => cards[names[name].OracleId!.Value];

        Assert.Equal(4, Card("Fire // Ice").ManaValue);
        Assert.Equal(2, Card("Fire // Ice").Analysis.CurveManaValue);
        Assert.Equal(3, Card("Ticket Booth // Tunnel of Hate").Analysis.CurveManaValue);
        Assert.Equal(["Creature", "Instant"], Card("Bonecrusher Giant // Stomp").Analysis.CardTypes);
        Assert.Equal([0], Card("Delver of Secrets // Insectile Aberration").Analysis.CastableFaces);
        Assert.Equal(2, Card("Delver of Secrets // Insectile Aberration").Faces.Count);
        Assert.Equal("legal", Card("Sol Ring").LegalityIn("commander"));
        Assert.True(Card("Forest").Analysis.IsLand);
    }

    [Fact]
    public async Task NamesResolveFromAllTheKindsOfNames()
    {
        var catalog = await CatalogAsync();

        var names = await catalog.ResolveNamesAsync(
            ["Foudre", "fire /// ice", "Ice", "Aether Vial", "Jotun Grunt", "No such card"], TestContext.Current.CancellationToken);

        Assert.Equal("Lightning Bolt", names["Foudre"].Name);
        Assert.Equal("Fire // Ice", names["fire /// ice"].Name);
        Assert.Equal("Fire // Ice", names["Ice"].Name);
        Assert.Equal("Jötun Grunt", names["Jotun Grunt"].Name);
        Assert.Equal(CardNameStatus.Found, names["Aether Vial"].Status);
        Assert.Equal(CardNameStatus.NotFound, names["No such card"].Status);
    }

    [Fact]
    public async Task PrintingsResolveBySetAndCollectorNumber()
    {
        var catalog = await CatalogAsync();
        var bolt = (await catalog.ResolveNamesAsync(["Lightning Bolt"], TestContext.Current.CancellationToken))["Lightning Bolt"].OracleId!.Value;
        var card = (await catalog.GetCardsAsync([bolt], TestContext.Current.CancellationToken))[bolt];
        var printing = (await catalog.GetPrintingsAsync([card.DefaultPrintingId!.Value], TestContext.Current.CancellationToken))[card.DefaultPrintingId.Value];

        var found = await catalog.FindPrintingsAsync(
            [new PrintingReference(printing.SetCode.ToUpperInvariant(), printing.CollectorNumber), new PrintingReference("zzz", "1")],
            TestContext.Current.CancellationToken);

        var match = Assert.Single(found);
        Assert.Equal(printing.PrintingId, match.Value.PrintingId);
        Assert.Equal(bolt, match.Value.OracleId);
        Assert.StartsWith("https://", printing.ImageUri, StringComparison.Ordinal);
    }
}
