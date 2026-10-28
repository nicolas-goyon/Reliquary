using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Reliquary.Collection.Exchange;

/// <summary>
/// A card of a file, as the file gives it. The import checks the values later.
/// </summary>
/// <param name="LineNumber">The number of the line in the file. The first line has the number 1.</param>
/// <param name="Quantity">The number of copies.</param>
/// <param name="Name">The card name.</param>
/// <param name="SetCode">The set code in lowercase, or <see langword="null"/>.</param>
/// <param name="CollectorNumber">The collector number, or <see langword="null"/>.</param>
internal sealed record FileCard(int LineNumber, int Quantity, string Name, string? SetCode, string? CollectorNumber)
{
    /// <summary>Gets the language as the file gives it, or <see langword="null"/>.</summary>
    public string? Language { get; init; }

    /// <summary>Gets the finish as the file gives it, or <see langword="null"/>.</summary>
    public string? Finish { get; init; }

    /// <summary>Gets the condition as the file gives it, or <see langword="null"/>.</summary>
    public string? Condition { get; init; }

    /// <summary>Gets the purchase price as the file gives it, or <see langword="null"/>.</summary>
    public string? PurchasePrice { get; init; }

    /// <summary>Gets the currency of the purchase price as the file gives it, or <see langword="null"/>.</summary>
    public string? PurchaseCurrency { get; init; }

    /// <summary>Gets the entry date as the file gives it, or <see langword="null"/>.</summary>
    public string? AcquiredOn { get; init; }

    /// <summary>Gets the note, or <see langword="null"/>.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// A line of a file that the import cannot use, or a line that the import changed.
/// </summary>
/// <param name="Line">The number of the line. The first line has the number 1.</param>
/// <param name="Code">The stable reason code, for example <c>card_not_found</c>.</param>
/// <param name="Text">The text of the line.</param>
internal sealed record FileLineError(int Line, string Code, string Text);

/// <summary>
/// The result of the parser of a file: the cards that it read, and the lines that it cannot read.
/// </summary>
internal sealed record ParsedFile(IReadOnlyList<FileCard> Cards, IReadOnlyList<FileLineError> Errors);

/// <summary>
/// The copies of one entry for an export. A field that the owner did not enable is <see langword="null"/>.
/// </summary>
/// <param name="Name">The Oracle name of the card.</param>
/// <param name="SetCode">The set code of the printing, or <see langword="null"/>.</param>
/// <param name="CollectorNumber">The collector number of the printing, or <see langword="null"/>.</param>
/// <param name="Language">The language code.</param>
/// <param name="Quantity">The number of copies.</param>
/// <param name="Finish">The finish, or <see langword="null"/>.</param>
/// <param name="Condition">The condition, or <see langword="null"/>.</param>
/// <param name="PurchasePrice">The purchase price of one copy, or <see langword="null"/>.</param>
/// <param name="PurchaseCurrency">The currency of the purchase price, or <see langword="null"/>.</param>
/// <param name="AcquiredOn">The entry date, or <see langword="null"/>.</param>
/// <param name="Note">The note, or <see langword="null"/>.</param>
/// <param name="Location">The name of the location, for example <c>Rack 1 / Box A</c> for a section.</param>
internal sealed record ExportCard(
    string Name,
    string? SetCode,
    string? CollectorNumber,
    string Language,
    int Quantity,
    string? Finish,
    string? Condition,
    decimal? PurchasePrice,
    string? PurchaseCurrency,
    DateOnly? AcquiredOn,
    string? Note,
    string Location);

/// <summary>
/// A file format of the collection import and export. See docs/architecture/collection.md.
/// </summary>
internal interface ICollectionFormat
{
    /// <summary>Gets the name of the format in the API, for example <c>manabox</c>.</summary>
    string Name { get; }

    /// <summary>Gets the extension of the file, for example <c>.csv</c>.</summary>
    string Extension { get; }

    /// <summary>Gets the media type of the file.</summary>
    string ContentType { get; }

    /// <summary>Reads a file.</summary>
    ParsedFile Parse(string content);

    /// <summary>Writes the cards of some locations, in the order of the list.</summary>
    string Write(IReadOnlyList<ExportCard> cards);
}

/// <summary>
/// The file formats of the collection import and export.
/// </summary>
internal static class CollectionFormats
{
    /// <summary>Gets all the formats, by name.</summary>
    public static IReadOnlyDictionary<string, ICollectionFormat> All { get; } = ((ICollectionFormat[])
    [
        new TextFormat(),
        CollectionCsvFormats.Generic,
        new MtgoDekFormat(),
        new ArenaFormat(),
        new MtgoTextFormat(),
        new MoxfieldFormat(),
        new ArchidektFormat(),
        .. CollectionCsvFormats.Tools,
    ]).ToDictionary(format => format.Name, StringComparer.Ordinal);

    /// <summary>
    /// Adds the quantities of the cards that a format writes in the same way, for a format that does not write all the fields.
    /// The result keeps the order of the first card of each group.
    /// </summary>
    public static IReadOnlyList<ExportCard> Merge(IEnumerable<ExportCard> cards, Func<ExportCard, string> key) =>
        [.. cards.GroupBy(key, StringComparer.Ordinal).Select(group => group.First() with { Quantity = group.Sum(card => card.Quantity) })];
}

/// <summary>
/// The reason codes of the lines of an import.
/// </summary>
internal static class FileLineCodes
{
    /// <summary>The line has no card name.</summary>
    public const string MissingName = "missing_name";

    /// <summary>The quantity is not from 1 to 100,000.</summary>
    public const string QuantityOutOfRange = "quantity_out_of_range";

    /// <summary>The import cannot read the file, for example an XML file with an error.</summary>
    public const string InvalidFile = "invalid_file";

    /// <summary>The CSV file has no column for the card names. The line is the header.</summary>
    public const string MissingColumn = "missing_column";

    /// <summary>No card has the name of the line.</summary>
    public const string CardNotFound = "card_not_found";

    /// <summary>The language is not a known language.</summary>
    public const string UnknownLanguage = "unknown_language";

    /// <summary>The condition is not a known condition.</summary>
    public const string UnknownCondition = "unknown_condition";

    /// <summary>The purchase price or its currency is not valid.</summary>
    public const string InvalidPrice = "invalid_price";

    /// <summary>The entry date is not a date.</summary>
    public const string InvalidDate = "invalid_date";

    /// <summary>The note is longer than 500 characters.</summary>
    public const string NoteTooLong = "note_too_long";

    /// <summary>A warning: the import finds no printing of the card for the set code, the collector number and the language. The card has no printing.</summary>
    public const string PrintingNotFound = "printing_not_found";

    /// <summary>A warning: the printing does not have the finish. The card has no finish.</summary>
    public const string FinishNotAvailable = "finish_not_available";
}

/// <summary>
/// Reads and writes the card lines of the text formats: <c>4 Sol Ring (C21) 263 *F* -- note</c>.
/// </summary>
/// <remarks>
/// The reader ignores the empty lines, the comments that start with <c>//</c>, the section headers such as <c>Sideboard</c>, and the tags that start with <c>#</c>.
/// </remarks>
internal static partial class CollectionText
{
    private const string NoteSeparator = " -- ";

    private static readonly HashSet<string> Headers = new(StringComparer.OrdinalIgnoreCase)
    {
        "commander", "commanders", "companion", "companions", "deck", "main", "main deck", "mainboard", "sideboard", "side",
        "considering", "maybeboard", "maybe", "tokens", "token", "collection",
    };

    /// <summary>
    /// Reads the card lines of a text.
    /// </summary>
    /// <param name="content">The text.</param>
    /// <param name="prepare">Changes the text of a line before the parser reads it, or <see langword="null"/>.</param>
    public static ParsedFile Parse(string content, Func<string, string>? prepare = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var cards = new List<FileCard>();
        var errors = new List<FileLineError>();
        var number = 0;
        foreach (var raw in content.TrimStart('﻿').Split('\n'))
        {
            number++;
            var source = raw.TrimEnd('\r');
            var line = (prepare is null ? source : prepare(source)).Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || IsHeader(line))
            {
                continue;
            }

            var (card, error) = ParseLine(line, number);
            if (card is not null)
            {
                cards.Add(card);
            }
            else
            {
                errors.Add(new FileLineError(number, error!, source));
            }
        }

        return new ParsedFile(cards, errors);
    }

    /// <summary>
    /// Writes one card line.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <param name="quantitySuffix">The text after the quantity, for example <c>x</c> for Archidekt.</param>
    /// <param name="upperSetCode">Whether the set code is in uppercase.</param>
    /// <param name="finish">Whether the line contains the finish.</param>
    public static string WriteLine(ExportCard card, string quantitySuffix = "", bool upperSetCode = true, bool finish = true)
    {
        ArgumentNullException.ThrowIfNull(card);
        var builder = new StringBuilder();
        builder.Append(FileValues.Quantity(card.Quantity)).Append(quantitySuffix).Append(' ').Append(card.Name);
        if (card.SetCode is { Length: > 0 } set)
        {
            builder.Append(" (").Append(upperSetCode ? set.ToUpperInvariant() : set).Append(')');
            if (card.CollectorNumber is { Length: > 0 } collectorNumber)
            {
                builder.Append(' ').Append(collectorNumber);
            }
        }

        if (finish)
        {
            builder.Append(card.Finish switch { "foil" => " *F*", "etched" => " *E*", _ => string.Empty });
        }

        return builder.ToString();
    }

    /// <summary>Gets the key of a card for a text line: the name, the printing and the finish.</summary>
    public static string LineKey(ExportCard card) => $"{card.Name}|{card.SetCode}|{card.CollectorNumber}|{card.Finish}";

    private static bool IsHeader(string line) =>
        !char.IsAsciiDigit(line[0]) && Headers.Contains(HeaderPattern().Replace(line, string.Empty).Trim());

    private static (FileCard? Card, string? Error) ParseLine(string line, int number)
    {
        var quantity = 1;
        var rest = line;
        var quantityMatch = QuantityPattern().Match(line);
        if (quantityMatch.Success)
        {
            if (!int.TryParse(quantityMatch.Groups["quantity"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out quantity)
                || quantity is < 1 or > CollectionRules.MaxQuantity)
            {
                return (null, FileLineCodes.QuantityOutOfRange);
            }

            rest = quantityMatch.Groups["rest"].Value;
        }

        string? note = null;
        var noteIndex = rest.IndexOf(NoteSeparator, StringComparison.Ordinal);
        if (noteIndex >= 0)
        {
            note = rest[(noteIndex + NoteSeparator.Length)..].Trim();
            rest = rest[..noteIndex];
        }

        // A space before the rest finds a tag at the start, for example "3 #ramp", which has no card name.
        rest = " " + rest;
        var tagIndex = rest.IndexOf(" #", StringComparison.Ordinal);
        if (tagIndex >= 0)
        {
            rest = rest[..tagIndex];
        }

        string? finish = null;
        var finishMatch = FinishPattern().Match(rest);
        if (finishMatch.Success)
        {
            finish = string.Equals(finishMatch.Groups["finish"].Value, "E", StringComparison.OrdinalIgnoreCase) ? "etched" : "foil";
            rest = rest[..finishMatch.Index];
        }

        string? setCode = null;
        string? collectorNumber = null;
        var printingMatch = PrintingPattern().Match(rest);
        if (printingMatch.Success)
        {
            setCode = printingMatch.Groups["set"].Value.ToLowerInvariant();
            collectorNumber = printingMatch.Groups["number"].Success ? printingMatch.Groups["number"].Value : null;
            rest = rest[..printingMatch.Index];
        }

        var name = WhiteSpace().Replace(rest.Trim(), " ");
        if (name.Length == 0)
        {
            return (null, FileLineCodes.MissingName);
        }

        return (new FileCard(number, quantity, name, setCode, collectorNumber) { Finish = finish, Note = string.IsNullOrEmpty(note) ? null : note }, null);
    }

    [GeneratedRegex(@"\s*:?\s*(?:\(\d+\))?$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^(?<quantity>\d+)\s*[xX]?\s+(?<rest>.+)$")]
    private static partial Regex QuantityPattern();

    [GeneratedRegex(@"\s+\*(?<finish>[FfEe])\*\s*$")]
    private static partial Regex FinishPattern();

    [GeneratedRegex(@"\s+\((?<set>[A-Za-z0-9]{2,6})\)(?:\s+(?<number>[^\s()]+))?\s*$")]
    private static partial Regex PrintingPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}

/// <summary>
/// The text format of Reliquary: the syntax of the deck text format. A comment line with the name of the location starts the cards of each location.
/// </summary>
internal sealed class TextFormat : ICollectionFormat
{
    public string Name => "text";

    public string Extension => ".txt";

    public string ContentType => "text/plain";

    public ParsedFile Parse(string content) => CollectionText.Parse(content);

    public string Write(IReadOnlyList<ExportCard> cards)
    {
        var builder = new StringBuilder();
        var locations = cards.GroupBy(card => card.Location, StringComparer.Ordinal).ToList();
        foreach (var location in locations)
        {
            if (locations.Count > 1)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append("// ").Append(location.Key).Append('\n');
            }

            foreach (var card in CollectionFormats.Merge(location, CollectionText.LineKey))
            {
                builder.Append(CollectionText.WriteLine(card)).Append('\n');
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// The text of MTG Arena: the header <c>Deck</c>, then lines with the set code and the collector number.
/// </summary>
internal sealed class ArenaFormat : ICollectionFormat
{
    public string Name => "mtga";

    public string Extension => ".txt";

    public string ContentType => "text/plain";

    public ParsedFile Parse(string content)
    {
        // The "About" section of Arena gives the name of the deck. It contains no card.
        var lines = content.Split('\n');
        var inAbout = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Equals("About", StringComparison.OrdinalIgnoreCase))
            {
                inAbout = true;
                lines[index] = string.Empty;
            }
            else if (inAbout && line.Length == 0)
            {
                inAbout = false;
            }
            else if (inAbout)
            {
                lines[index] = string.Empty;
            }
        }

        return CollectionText.Parse(string.Join('\n', lines));
    }

    public string Write(IReadOnlyList<ExportCard> cards)
    {
        var builder = new StringBuilder("Deck\n");
        foreach (var card in CollectionFormats.Merge(cards, card => $"{card.Name}|{card.SetCode}|{card.CollectorNumber}"))
        {
            builder.Append(CollectionText.WriteLine(card, finish: false)).Append('\n');
        }

        return builder.ToString();
    }
}

/// <summary>
/// The text of Magic Online: one line with the quantity and the name for each card.
/// </summary>
internal sealed class MtgoTextFormat : ICollectionFormat
{
    public string Name => "mtgo";

    public string Extension => ".txt";

    public string ContentType => "text/plain";

    public ParsedFile Parse(string content) => CollectionText.Parse(content);

    public string Write(IReadOnlyList<ExportCard> cards)
    {
        var builder = new StringBuilder();
        foreach (var card in CollectionFormats.Merge(cards, card => card.Name))
        {
            builder.Append(FileValues.Quantity(card.Quantity)).Append(' ').Append(card.Name).Append('\n');
        }

        return builder.ToString();
    }
}

/// <summary>
/// The XML file of Magic Online (<c>.dek</c>). Each card has a quantity and a name.
/// </summary>
internal sealed class MtgoDekFormat : ICollectionFormat
{
    public string Name => "dek";

    public string Extension => ".dek";

    public string ContentType => "application/xml";

    public ParsedFile Parse(string content)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(content.TrimStart('﻿'), LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            return new ParsedFile([], [new FileLineError(exception.LineNumber, FileLineCodes.InvalidFile, string.Empty)]);
        }

        var cards = new List<FileCard>();
        var errors = new List<FileLineError>();
        foreach (var card in document.Descendants("Cards"))
        {
            var number = ((IXmlLineInfo)card).LineNumber;
            var text = card.ToString(SaveOptions.DisableFormatting);
            var name = card.Attribute("Name")?.Value?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                errors.Add(new FileLineError(number, FileLineCodes.MissingName, text));
                continue;
            }

            if (!int.TryParse(card.Attribute("Quantity")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity is < 1 or > CollectionRules.MaxQuantity)
            {
                errors.Add(new FileLineError(number, FileLineCodes.QuantityOutOfRange, text));
                continue;
            }

            cards.Add(new FileCard(number, quantity, name, null, null));
        }

        return new ParsedFile(cards, errors);
    }

    public string Write(IReadOnlyList<ExportCard> cards)
    {
        var deck = new XElement(
            "Deck",
            new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XElement("NetDeckID", 0),
            new XElement("PreconstructedDeckID", 0));
        foreach (var card in CollectionFormats.Merge(cards, card => card.Name))
        {
            deck.Add(new XElement(
                "Cards",
                new XAttribute("CatID", 0),
                new XAttribute("Quantity", card.Quantity),
                new XAttribute("Sideboard", "false"),
                new XAttribute("Name", card.Name),
                new XAttribute("Annotation", 0)));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), deck).Declaration + "\n" + deck.ToString() + "\n";
    }
}

/// <summary>
/// The text of Archidekt: <c>1x Sol Ring (c21) 263 *F* [Ramp]</c>. The import ignores the categories and the labels.
/// </summary>
internal sealed partial class ArchidektFormat : ICollectionFormat
{
    public string Name => "archidekt";

    public string Extension => ".txt";

    public string ContentType => "text/plain";

    public ParsedFile Parse(string content) =>
        CollectionText.Parse(content, line => CategoryPattern().Replace(LabelPattern().Replace(line, string.Empty), string.Empty));

    public string Write(IReadOnlyList<ExportCard> cards)
    {
        var builder = new StringBuilder();
        foreach (var card in CollectionFormats.Merge(cards, CollectionText.LineKey))
        {
            builder.Append(CollectionText.WriteLine(card, quantitySuffix: "x", upperSetCode: false)).Append('\n');
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\s*\^[^^]*\^\s*$")]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"\s*\[[^\]]*\]\s*$")]
    private static partial Regex CategoryPattern();
}

/// <summary>
/// The formats of Moxfield. The import reads the collection CSV file of Moxfield, or the text of Moxfield.
/// The export writes the collection CSV file, which the collection import of Moxfield reads.
/// </summary>
internal sealed class MoxfieldFormat : ICollectionFormat
{
    public string Name => "moxfield";

    public string Extension => ".csv";

    public string ContentType => "text/csv";

    public ParsedFile Parse(string content)
    {
        var header = CollectionCsv.Read(content).FirstOrDefault()?.Cells ?? [];
        var csv = header.Count > 1 && header.Any(cell => CollectionCsv.SameHeader(cell, "Count")) && header.Any(cell => CollectionCsv.SameHeader(cell, "Name"));
        return csv ? CollectionCsvFormats.Moxfield.Parse(content) : CollectionText.Parse(content);
    }

    public string Write(IReadOnlyList<ExportCard> cards) => CollectionCsvFormats.Moxfield.Write(cards);
}
