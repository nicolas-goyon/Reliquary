using System.Text;
using Reliquary.BuildingBlocks.Text;

namespace Reliquary.BuildingBlocks.Tests;

public sealed class CsvTests
{
    [Fact]
    public void ReaderKeepsLineBreaksInQuotesAndGivesTheLineOfEachRow()
    {
        var rows = Csv.Read("﻿Name,Note\r\nSol Ring,\"One\nTwo\"\r\nForest,\"Say \"\"hi\"\"\"\r\n");

        Assert.Equal([1, 2, 4], rows.Select(row => row.LineNumber));
        Assert.Equal(["Sol Ring", "One\nTwo"], rows[1].Cells);
        Assert.Equal("Say \"hi\"", rows[2].Cells[1]);
    }

    [Theory]
    [InlineData("Name,Set\r\nSol Ring,c21\r\n")]
    [InlineData("Name,Set\nSol Ring,c21\n")]
    [InlineData("Name,Set\nSol Ring,c21")]
    [InlineData("﻿Name,Set\r\nSol Ring,c21")]
    public void ReaderAcceptsCrlfLfAndAByteOrderMark(string content)
    {
        var rows = Csv.Read(content);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["Name", "Set"], rows[0].Cells);
        Assert.Equal(["Sol Ring", "c21"], rows[1].Cells);
    }

    [Fact]
    public void ReaderKeepsEmptyCellsAndIgnoresEmptyLines()
    {
        var rows = Csv.Read("Name,Set,Number\n\nSol Ring,,\n,,\n\"\",c21,1\n");

        Assert.Equal([1, 3, 4, 5], rows.Select(row => row.LineNumber));
        Assert.Equal(["Sol Ring", "", ""], rows[1].Cells);
        Assert.Equal(["", "", ""], rows[2].Cells);
        Assert.Equal(["", "c21", "1"], rows[3].Cells);
    }

    [Fact]
    public void ReaderKeepsSeparatorsAndCarriageReturnsInQuotes()
    {
        var rows = Csv.Read("Name,Note\n\"Fire, Ice\",\"a\r\nb\"\n");

        Assert.Equal(["Fire, Ice", "a\r\nb"], rows[1].Cells);
    }

    [Theory]
    [InlineData("Name;Set\nSol Ring;c21", ';')]
    [InlineData("Name\tSet\nSol Ring\tc21", '\t')]
    [InlineData("Name,Set\nSol Ring,c21", ',')]
    public void ReaderGetsTheSeparatorFromTheHeader(string content, char separator)
    {
        var rows = Csv.Read(content);

        Assert.Equal(["Sol Ring", "c21"], rows[1].Cells);
        Assert.Contains(separator, content);
    }

    [Fact]
    public void ReaderUsesTheSeparatorOfASepLineAndIgnoresTheLine()
    {
        var rows = Csv.Read("sep=;\nName;Note\nSol Ring;a,b\n");

        Assert.Equal([2, 3], rows.Select(row => row.LineNumber));
        Assert.Equal(["Sol Ring", "a,b"], rows[1].Cells);
    }

    [Fact]
    public void EmptyContentGivesNoRow()
    {
        Assert.Empty(Csv.Read(string.Empty));
        Assert.Empty(Csv.Read("﻿\r\n\n"));
    }

    [Theory]
    [InlineData("Collector number", "collector_number")]
    [InlineData(" Set-Code ", "SetCode")]
    [InlineData("CARD NAME", "Card name")]
    public void SameHeaderIgnoresCaseSpacesHyphensAndUnderscores(string header, string candidate) =>
        Assert.True(Csv.SameHeader(header, candidate));

    [Fact]
    public void SameHeaderRefusesADifferentName() =>
        Assert.False(Csv.SameHeader("Set name", "Set code"));

    [Fact]
    public void WriterPutsInQuotesOnlyTheCellsThatNeedQuotes()
    {
        var builder = new StringBuilder();

        Csv.WriteRow(builder, ["Sol Ring", "Fire, Ice", "Say \"hi\"", "One\nTwo", ""]);

        Assert.Equal("Sol Ring,\"Fire, Ice\",\"Say \"\"hi\"\"\",\"One\nTwo\",\n", builder.ToString());
    }

    [Fact]
    public void WriterOutputGivesTheSameCellsWhenTheReaderReadsIt()
    {
        string[] cells = ["Sol Ring", "Fire, Ice", "Say \"hi\"", "One\r\nTwo", ""];
        var builder = new StringBuilder();
        Csv.WriteRow(builder, ["A", "B", "C", "D", "E"]);
        Csv.WriteRow(builder, cells);

        var rows = Csv.Read(builder.ToString());

        Assert.Equal(cells, rows[1].Cells);
    }
}
