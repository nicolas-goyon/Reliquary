using System.IO.Compression;
using Reliquary.Cards.Import;
using Reliquary.Cards.Import.Scryfall;

namespace Reliquary.Cards.Tests;

public sealed class CardMapperTests
{
    private static readonly Lazy<List<ScryfallCard>> Cards = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(CardsDatabase.FixturesDirectory, "cards.jsonl.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var cards = new List<ScryfallCard>();
        while (reader.ReadLine() is { } line)
        {
            cards.Add(ScryfallJson.Read(line, ScryfallJson.Default.ScryfallCard));
        }

        return cards;
    });

    [Fact]
    public void TransformCardHasTwoFacesAndTheCostOfItsFrontFace()
    {
        var card = English("Delver of Secrets // Insectile Aberration");

        var oracle = CardMapper.ToOracle(card, CardMapper.OracleIdOf(card)!.Value);

        Assert.Equal(2, oracle.Faces.Count);
        Assert.Equal("{U}", oracle.ManaCost);
        Assert.Equal(1, oracle.ManaValue);
        Assert.Equal(["U"], oracle.Colors);
        Assert.Equal("Insectile Aberration", oracle.Faces[1].Name);
    }

    [Fact]
    public void HalvesOfASplitCardGetTheColorsOfTheirCosts()
    {
        var card = English("Fire // Ice");

        var oracle = CardMapper.ToOracle(card, card.OracleId!.Value);

        Assert.Equal(["R"], oracle.Faces[0].Colors);
        Assert.Equal(["U"], oracle.Faces[1].Colors);
        Assert.Equal(2, oracle.Faces[0].ManaValue);
        Assert.Equal(4, oracle.ManaValue);
    }

    [Fact]
    public void NamesContainTheFaceNamesAndTheLocalizedNames()
    {
        var english = English("Delver of Secrets // Insectile Aberration");
        var oracle = CardMapper.ToOracle(english, english.OracleId!.Value);
        var localized = Cards.Value.First(card => card.Lang != "en" && card.PrintedName is not null && card.PrintedName != card.Name);
        var localizedOracle = CardMapper.ToOracle(localized, CardMapper.OracleIdOf(localized)!.Value);

        var names = CardMapper.Names(english, oracle).ToList();
        var localizedNames = CardMapper.Names(localized, localizedOracle).ToList();

        Assert.Contains(names, name => name is { Kind: "oracle", Lang: "en" } && name.Name == english.Name);
        Assert.Contains(names, name => name is { Kind: "face", Name: "Insectile Aberration" });
        Assert.Contains(localizedNames, name => name.Kind == "printed" && name.Lang == localized.Lang && name.Name == localized.PrintedName);
    }

    [Fact]
    public void AlternateNameOfAPrintingIsAFlavorName()
    {
        var card = Cards.Value.First(card => card.Name == "Zilortha, Strength Incarnate" && card.FlavorName is not null);
        var oracle = CardMapper.ToOracle(card, card.OracleId!.Value);

        Assert.Contains(CardMapper.Names(card, oracle), name => name.Kind == "flavor" && name.Name == card.FlavorName);
    }

    [Fact]
    public void RelationsDoNotContainTheCardItself()
    {
        var card = English("Brisela, Voice of Nightmares");

        var relations = CardMapper.Relations(card, card.OracleId!.Value).ToList();

        Assert.Contains(relations, relation => relation.Component == "meld_part");
        Assert.DoesNotContain(relations, relation => relation.RelatedPrintingId == card.Id);
    }

    [Fact]
    public void SameRowGivesTheSameHash()
    {
        var card = English("Lightning Bolt");

        var first = CardMapper.Hash(CardMapper.ToPrinting(card, card.OracleId!.Value));
        var second = CardMapper.Hash(CardMapper.ToPrinting(card, card.OracleId!.Value));

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("123", 123)]
    [InlineData("67★", 67)]
    [InlineData("14b", 14)]
    [InlineData("CLB-187", null)]
    public void CollectorNumberValueIsTheNumberAtTheStart(string collectorNumber, int? expected) =>
        Assert.Equal(expected, CardMapper.CollectorNumberValue(collectorNumber));

    private static ScryfallCard English(string name) => Cards.Value.First(card => card.Name == name && card.Lang == "en");
}
