using Reliquary.Decks.Text;

namespace Reliquary.Decks.Tests;

public sealed class DeckTextTests
{
    [Fact]
    public void ParserReadsEachElementOfTheSyntax()
    {
        var result = DeckText.Parse("""
            Commander
            1 Atraxa, Praetors' Voice (2x2) 190 *F*

            Main
            1x Sol Ring (C21) 263 #Ramp #!Mana Rock #@ramp -- Turn one
            10 Forest
            Lightning Bolt
            1 Fire // Ice *E*

            // A comment
            SIDEBOARD:
            2 Counterspell (CMR)
            Maybeboard (2)
            1 B.F.M. (Big Furry Monster)
            """);

        Assert.Empty(result.Errors);
        Assert.Collection(
            result.Lines,
            line => Assert.Equal(("Atraxa, Praetors' Voice", "2x2", "190", "foil", "commander"), (line.Name, line.SetCode, line.CollectorNumber, line.Finish, line.Zone)),
            line =>
            {
                Assert.Equal(("Sol Ring", "c21", "263", "main"), (line.Name, line.SetCode, line.CollectorNumber, line.Zone));
                Assert.Equal(["Ramp"], line.Tags);
                Assert.Equal(["Mana Rock"], line.GlobalTags);
                Assert.Equal(["ramp"], line.Categories);
                Assert.Equal("Turn one", line.Note);
            },
            line => Assert.Equal((10, "Forest", (string?)null), (line.Quantity, line.Name, line.SetCode)),
            line => Assert.Equal((1, "Lightning Bolt"), (line.Quantity, line.Name)),
            line => Assert.Equal(("Fire // Ice", "etched"), (line.Name, line.Finish)),
            line => Assert.Equal((2, "Counterspell", "cmr", (string?)null, "sideboard"), (line.Quantity, line.Name, line.SetCode, line.CollectorNumber, line.Zone)),
            line => Assert.Equal(("B.F.M. (Big Furry Monster)", "considering"), (line.Name, line.Zone)));
    }

    [Fact]
    public void ParserReportsTheLinesThatItCannotRead()
    {
        var result = DeckText.Parse("1 Sol Ring\n0 Forest\n1000 Island\n3 #ramp\n1 Sol Ring #@mana\n");

        Assert.Single(result.Lines);
        Assert.Equal(
            [(2, DeckText.QuantityOutOfRange), (3, DeckText.QuantityOutOfRange), (4, DeckText.MissingName), (5, DeckText.UnknownCategory)],
            result.Errors.Select(error => (error.LineNumber, error.Code)));
    }

    [Fact]
    public void WriteThenParseGivesTheSameLines()
    {
        DeckTextLine[] lines =
        [
            new(0, 1, "Atraxa, Praetors' Voice", "2x2", "190", "foil", [], [], string.Empty, "commander", []),
            new(0, 1, "Sol Ring", "c21", "263", null, ["Ramp", "Fast mana"], ["Mana Rock"], "Turn one", "main", ["ramp", "card_draw"]),
            new(0, 12, "Forest", null, null, null, [], [], string.Empty, "main", []),
            new(0, 1, "Fire // Ice", "mh2", "290", "etched", [], [], string.Empty, "sideboard", []),
            new(0, 1, "Cultivate", null, null, null, ["Ramp"], [], string.Empty, "considering", []),
            new(0, 1, "Treasure", "tc21", "1", null, [], [], string.Empty, "tokens", []),
        ];

        var text = DeckText.Write(lines);
        var parsed = DeckText.Parse(text);

        Assert.Empty(parsed.Errors);
        Assert.Equal(
            lines.Select(Comparable),
            parsed.Lines.Select(Comparable));
        Assert.StartsWith("Commander\n1 Atraxa, Praetors' Voice (2X2) 190 *F*\n\nMain\n1 Sol Ring (C21) 263 #Ramp #Fast mana #!Mana Rock #@ramp #@card_draw -- Turn one\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LinesBeforeTheFirstHeaderGoToTheDefaultZone()
    {
        var result = DeckText.Parse("4 Lightning Bolt\nSideboard\n1 Pyroblast", defaultZone: "main");

        Assert.Equal(["main", "sideboard"], result.Lines.Select(line => line.Zone));
    }

    private static string Comparable(DeckTextLine line) =>
        $"{line.Quantity}|{line.Name}|{line.SetCode}|{line.CollectorNumber}|{line.Finish}|{string.Join(',', line.Tags)}|{string.Join(',', line.GlobalTags)}|{string.Join(',', line.Categories)}|{line.Note}|{line.Zone}";
}
