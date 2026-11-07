using Reliquary.Decks.Exchange;
using Reliquary.Decks.Text;

namespace Reliquary.Decks.Tests;

public sealed class DeckFormatTests
{
    private static IDeckFormat Format(string name) => DeckFormats.All[name];

    [Fact]
    public void ArenaFileSkipsTheAboutSectionAndReadsTheSections()
    {
        var result = Format("mtga").Parse("About\nName Atraxa\n\nCommander\n1 Atraxa, Praetors' Voice (2X2) 190\n\nDeck\n1 Sol Ring (C21) 263\n\nSideboard\n1 Counterspell\n");

        Assert.Empty(result.Errors);
        Assert.Equal(["commander", "main", "sideboard"], result.Lines.Select(line => line.Zone));
        Assert.Equal(("2x2", "190"), (result.Lines[0].SetCode, result.Lines[0].CollectorNumber));
    }

    [Fact]
    public void MtgoTextUsesTheEmptyLineForTheSideboard()
    {
        var result = Format("mtgo").Parse("4 Lightning Bolt\n20 Mountain\n\n2 Pyroblast\n");

        Assert.Equal([("Lightning Bolt", "main"), ("Mountain", "main"), ("Pyroblast", "sideboard")], result.Lines.Select(line => (line.Name, line.Zone)));
    }

    [Fact]
    public void MtgoWritesTheCommanderInTheSideboard()
    {
        DeckTextLine[] lines = [Line("Atraxa, Praetors' Voice", "commander"), Line("Sol Ring", "main"), Line("Cultivate", "considering")];

        Assert.Equal("1 Sol Ring\n\n1 Atraxa, Praetors' Voice\n", Format("mtgo").Write("commander", lines));
        Assert.Equal("1 Atraxa, Praetors' Voice\n1 Sol Ring\n\n", Format("mtgo").Write("legacy", lines));
    }

    [Fact]
    public void DekFileReadsTheCardsAndWritesThemAgain()
    {
        const string dek = """
            <?xml version="1.0" encoding="utf-8"?>
            <Deck xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <NetDeckID>0</NetDeckID>
              <Cards CatID="1" Quantity="4" Sideboard="false" Name="Lightning Bolt" Annotation="0" />
              <Cards CatID="2" Quantity="2" Sideboard="true" Name="Pyroblast" Annotation="0" />
              <Cards CatID="3" Quantity="0" Sideboard="false" Name="Mountain" Annotation="0" />
            </Deck>
            """;

        var result = Format("dek").Parse(dek);
        var again = Format("dek").Parse(Format("dek").Write("legacy", result.Lines));

        Assert.Equal([(4, "Lightning Bolt", "main"), (2, "Pyroblast", "sideboard")], result.Lines.Select(line => (line.Quantity, line.Name, line.Zone)));
        Assert.Equal((6, DeckText.QuantityOutOfRange), (result.Errors.Single().LineNumber, result.Errors.Single().Code));
        Assert.Equal(result.Lines.Select(line => (line.Quantity, line.Name, line.Zone)), again.Lines.Select(line => (line.Quantity, line.Name, line.Zone)));
        Assert.Equal(DeckFormatCodes.InvalidFile, Format("dek").Parse("<Deck>").Errors.Single().Code);
    }

    [Fact]
    public void ArchidektCategoriesGiveTheZoneAndTheTags()
    {
        var result = Format("archidekt").Parse("1x Atraxa, Praetors' Voice (2x2) 190 *F* [Commander{top}]\n1x Sol Ring (c21) 263 [Ramp,Fast mana] ^Have,#37d67a^\n1x Cultivate [Maybeboard{noDeck}{noPrice},Ramp]\n");
        var written = Format("archidekt").Write("commander", result.Lines);

        Assert.Empty(result.Errors);
        Assert.Equal([("commander", "foil"), ("main", null), ("considering", null)], result.Lines.Select(line => (line.Zone, line.Finish)));
        Assert.Equal(["Ramp", "Fast mana"], result.Lines[1].Tags);
        Assert.Equal("1x Atraxa, Praetors' Voice (2x2) 190 *F* [Commander{top}]\n1x Sol Ring (c21) 263 [Ramp,Fast mana]\n1x Cultivate [Ramp,Maybeboard]\n", written);
    }

    [Fact]
    public void MoxfieldWritesItsSectionNamesAndLeavesTheTokensOut()
    {
        DeckTextLine[] lines = [Line("Atraxa, Praetors' Voice", "commander"), Line("Sol Ring", "main") with { Tags = ["Ramp"] }, Line("Cultivate", "considering"), Line("Treasure", "tokens")];

        var text = Format("moxfield").Write("commander", lines);

        Assert.Equal("Commander\n1 Atraxa, Praetors' Voice\n\nMainboard\n1 Sol Ring #Ramp\n\nMaybeboard\n1 Cultivate\n", text);
        Assert.Equal(["commander", "main", "considering"], Format("moxfield").Parse(text).Lines.Select(line => line.Zone));
    }

    [Theory]
    [InlineData("Atraxa / 4c", "Atraxa _ 4c")]
    [InlineData("../..", "deck")]
    [InlineData("Élan vital", "Élan vital")]
    public void FileNameKeepsOnlySafeCharacters(string name, string expected) =>
        Assert.Equal(expected, DeckExchangeService.FileName(name));

    private static DeckTextLine Line(string name, string zone) => new(0, 1, name, null, null, null, [], [], string.Empty, zone, []);
}
