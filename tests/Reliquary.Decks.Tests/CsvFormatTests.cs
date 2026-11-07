using Reliquary.Decks.Exchange;
using Reliquary.Decks.Text;

namespace Reliquary.Decks.Tests;

public sealed class CsvFormatTests
{
    private const string Deckbox = """
        Count,Tradelist Count,Name,Edition,Edition Code,Card Number,Condition,Language,Foil,Signed,Artist Proof,Altered Art,Misprint,Promo,Textless,Printing Id,Printing Note,Tags,My Price
        1,0,Sol Ring,Commander 2021,c21,263,Near Mint,English,foil,,,,,,,,,"Ramp,Fast mana",$1.00
        4,0,Forest,Unlimited Edition,2ed,,Near Mint,English,,,,,,,,,,,$0.10
        """;

    private const string ManaBox = """
        Name,Set code,Set name,Collector number,Foil,Rarity,Quantity,ManaBox ID,Scryfall ID,Purchase price,Misprint,Altered,Condition,Language,Purchase price currency
        Sol Ring,C21,Commander 2021,263,etched,uncommon,1,1234,00000000-0000-0000-0000-000000000000,1.00,false,false,near_mint,en,USD
        Forest,2ED,Unlimited Edition,,normal,common,4,1235,00000000-0000-0000-0000-000000000000,0.10,false,false,near_mint,en,USD
        """;

    private const string DragonShield = """
        "sep=,"
        Folder Name,Quantity,Trade Quantity,Card Name,Set Code,Set Name,Card Number,Condition,Printing,Language,Price Bought,Date Bought,LOW,MID,MARKET
        Atraxa,1,0,Sol Ring,C21,Commander 2021,263,NearMint,Foil,English,1.00,2024-01-01,0.50,1.00,1.20
        Atraxa,4,0,Forest,2ED,Unlimited Edition,,NearMint,Normal,English,0.10,2024-01-01,0.05,0.10,0.10
        """;

    [Theory]
    [InlineData("deckbox", Deckbox, 2, "foil")]
    [InlineData("manabox", ManaBox, 2, "etched")]
    [InlineData("dragonshield", DragonShield, 3, "foil")]
    public void EachToolFileGivesTheCards(string format, string content, int firstLine, string finish)
    {
        var result = DeckFormats.All[format].Parse(content);

        Assert.Empty(result.Errors);
        Assert.Equal([(1, "Sol Ring", "c21", "263"), (4, "Forest", "2ed", null)], result.Lines.Select(line => (line.Quantity, line.Name, line.SetCode, line.CollectorNumber)));
        Assert.Equal((firstLine, finish, "main"), (result.Lines[0].LineNumber, result.Lines[0].Finish, result.Lines[0].Zone));
        Assert.Null(result.Lines[1].Finish);
        Assert.Equal(["Sol Ring", "Forest"], DeckFormats.All[format].Parse(DeckFormats.All[format].Write("commander", result.Lines)).Lines.Select(line => line.Name));
    }

    [Fact]
    public void DeckboxTagsBecomeLocalTags() =>
        Assert.Equal(["Ramp", "Fast mana"], DeckFormats.All["deckbox"].Parse(Deckbox).Lines[0].Tags);

    [Fact]
    public void GenericCsvKeepsAllTheDataOfALine()
    {
        DeckTextLine[] lines =
        [
            new(0, 1, "Sol Ring", "c21", "263", "foil", ["Fast, mana"], ["Mana rock"], "Turn \"one\"", "main", ["ramp"]),
            new(0, 2, "Cultivate", null, null, null, [], [], string.Empty, "considering", []),
        ];

        var text = CsvFormats.Generic.Write("commander", lines);
        var result = CsvFormats.Generic.Parse(text);

        Assert.StartsWith("Quantity,Name,Set code,Collector number,Finish,Zone,Tags,Global tags,Categories,Note\n", text, StringComparison.Ordinal);
        Assert.Empty(result.Errors);
        Assert.Equal(("Sol Ring", "c21", "263", "foil", "main"), (result.Lines[0].Name, result.Lines[0].SetCode, result.Lines[0].CollectorNumber, result.Lines[0].Finish, result.Lines[0].Zone));
        Assert.Equal(["Fast, mana"], result.Lines[0].Tags);
        Assert.Equal(["Mana rock"], result.Lines[0].GlobalTags);
        Assert.Equal(["ramp"], result.Lines[0].Categories);
        Assert.Equal("Turn \"one\"", result.Lines[0].Note);
        Assert.Equal((2, "considering"), (result.Lines[1].Quantity, result.Lines[1].Zone));
    }

    [Fact]
    public void ColumnMapReadsAnUnknownFile()
    {
        const string content = "Karte;Anzahl;Bereich\nSol Ring;1;Commander\nForest;x;Main\n;;\nIsland;2;Graveyard\n";

        var unmapped = CsvFormats.Generic.Parse(content);
        var mapped = CsvFormats.Generic
            .WithColumns(new Dictionary<string, string> { [CsvFields.Name] = "Karte", [CsvFields.Quantity] = "Anzahl", [CsvFields.Zone] = "Bereich" })
            .Parse(content);

        Assert.Equal((1, DeckFormatCodes.MissingColumn), (unmapped.Errors.Single().LineNumber, unmapped.Errors.Single().Code));
        Assert.Equal(("Sol Ring", "commander"), (mapped.Lines.Single().Name, mapped.Lines.Single().Zone));
        Assert.Equal([(3, DeckText.QuantityOutOfRange), (5, DeckFormatCodes.UnknownZone)], mapped.Errors.Select(error => (error.LineNumber, error.Code)));
    }

    [Fact]
    public void ReaderKeepsLineBreaksInQuotesAndGivesTheLineOfEachRow()
    {
        var rows = Csv.Read("﻿Name,Note\r\nSol Ring,\"One\nTwo\"\r\nForest,\"Say \"\"hi\"\"\"\r\n");

        Assert.Equal([1, 2, 4], rows.Select(row => row.LineNumber));
        Assert.Equal(["Sol Ring", "One\nTwo"], rows[1].Cells);
        Assert.Equal("Say \"hi\"", rows[2].Cells[1]);
    }
}
