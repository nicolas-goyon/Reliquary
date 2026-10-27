using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Reliquary.Decks.Text;

namespace Reliquary.Decks.Exchange;

/// <summary>
/// A file format of decks: it reads a file into text lines and writes text lines into a file.
/// </summary>
internal interface IDeckFormat
{
    /// <summary>Gets the name of the format in the API, for example <c>moxfield</c>.</summary>
    string Name { get; }

    /// <summary>Gets the extension of the file, for example <c>.txt</c>.</summary>
    string Extension { get; }

    /// <summary>Gets the media type of the file.</summary>
    string ContentType { get; }

    /// <summary>Reads a file.</summary>
    DeckTextResult Parse(string content);

    /// <summary>Writes a deck. The format of the deck changes the zone of the commanders in some formats.</summary>
    string Write(string deckFormat, IReadOnlyList<DeckTextLine> lines);
}

/// <summary>
/// The file formats of the import and the export. See docs/development/deck-import-export.md.
/// </summary>
internal static class DeckFormats
{
    /// <summary>Gets all the formats, by name.</summary>
    public static IReadOnlyDictionary<string, IDeckFormat> All { get; } = new IDeckFormat[]
    {
        new ReliquaryTextFormat(),
        new MoxfieldFormat(),
        new ArenaFormat(),
        new MtgoTextFormat(),
        new MtgoDekFormat(),
        new ArchidektFormat(),
    }.ToDictionary(format => format.Name, StringComparer.Ordinal);
}

/// <summary>The deck text format of Reliquary.</summary>
internal sealed class ReliquaryTextFormat : IDeckFormat
{
    public string Name => "text";

    public string Extension => ".txt";

    public string ContentType => "text/plain";

    public DeckTextResult Parse(string content) => DeckText.Parse(content);

    public string Write(string deckFormat, IReadOnlyList<DeckTextLine> lines) => DeckText.Write(lines);
}

/// <summary>
/// The text of Moxfield: the bulk edit syntax, with the section names of Moxfield.
/// Moxfield has no tokens zone, so the export leaves the tokens out.
/// </summary>
internal sealed class MoxfieldFormat : IDeckFormat
{
    private static readonly (string Zone, string Header)[] Sections =
        [("commander", "Commander"), ("companion", "Companion"), ("main", "Mainboard"), ("sideboard", "Sideboard"), ("considering", "Maybeboard")];

    public string Name => "moxfield";

    public string Extension => ".txt";

    public string ContentType => "text/plain";

    public DeckTextResult Parse(string content) => DeckText.Parse(content);

    public string Write(string deckFormat, IReadOnlyList<DeckTextLine> lines) => Sectioned.Write(lines, Sections, DeckText.WriteLine);
}

/// <summary>
/// The text of MTG Arena: the sections Commander, Companion, Deck and Sideboard, and lines with the set code and the collector number.
/// Arena has no considering zone and no tokens zone, so the export leaves them out.
/// </summary>
internal sealed class ArenaFormat : IDeckFormat
{
    private static readonly (string Zone, string Header)[] Sections =
        [("commander", "Commander"), ("companion", "Companion"), ("main", "Deck"), ("sideboard", "Sideboard")];

    public string Name => "mtga";

    public string Extension => ".txt";

    public string ContentType => "text/plain";

    public DeckTextResult Parse(string content)
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

        return DeckText.Parse(string.Join('\n', lines));
    }

    public string Write(string deckFormat, IReadOnlyList<DeckTextLine> lines) =>
        Sectioned.Write(lines, Sections, line => DeckText.WriteLine(line with { Tags = [], GlobalTags = [], Categories = [], Note = string.Empty, Finish = null }));
}

/// <summary>
/// The text of Magic Online: the main deck, an empty line, then the sideboard. In Commander, the commanders are in the sideboard.
/// </summary>
internal sealed class MtgoTextFormat : IDeckFormat
{
    public string Name => "mtgo";

    public string Extension => ".txt";

    public string ContentType => "text/plain";

    public DeckTextResult Parse(string content)
    {
        // The first empty line after a card starts the sideboard, when the file has no section header.
        var lines = content.Split('\n');
        var parsed = DeckText.Parse(content);
        if (parsed.Lines.Any(line => line.Zone != "main"))
        {
            return parsed;
        }

        var seenCard = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            {
                seenCard = true;
            }
            else if (line.Length == 0 && seenCard)
            {
                lines[index] = "Sideboard";
                break;
            }
        }

        return DeckText.Parse(string.Join('\n', lines));
    }

    public string Write(string deckFormat, IReadOnlyList<DeckTextLine> lines)
    {
        var commanderInSideboard = deckFormat is "commander" or "duel" or "predh" or "paupercommander";
        var main = lines.Where(line => line.Zone == "main" || (!commanderInSideboard && line.Zone == "commander"));
        var sideboard = lines.Where(line => line.Zone is "sideboard" or "companion" || (commanderInSideboard && line.Zone == "commander"));
        static string Simple(DeckTextLine line) => string.Create(CultureInfo.InvariantCulture, $"{line.Quantity} {line.Name}");
        var builder = new StringBuilder();
        foreach (var line in main)
        {
            builder.Append(Simple(line)).Append('\n');
        }

        builder.Append('\n');
        foreach (var line in sideboard)
        {
            builder.Append(Simple(line)).Append('\n');
        }

        return builder.ToString();
    }
}

/// <summary>
/// The XML file of Magic Online (<c>.dek</c>). Each card has a quantity, a name and a sideboard flag.
/// </summary>
internal sealed class MtgoDekFormat : IDeckFormat
{
    public string Name => "dek";

    public string Extension => ".dek";

    public string ContentType => "application/xml";

    public DeckTextResult Parse(string content)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(content, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            return new DeckTextResult([], [new DeckTextError(exception.LineNumber, DeckFormatCodes.InvalidFile, string.Empty)]);
        }

        var lines = new List<DeckTextLine>();
        var errors = new List<DeckTextError>();
        foreach (var card in document.Descendants("Cards"))
        {
            var number = ((IXmlLineInfo)card).LineNumber;
            var name = card.Attribute("Name")?.Value?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                errors.Add(new DeckTextError(number, DeckText.MissingName, card.ToString(SaveOptions.DisableFormatting)));
                continue;
            }

            if (!int.TryParse(card.Attribute("Quantity")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity is < 1 or > DeckRules.MaxQuantity)
            {
                errors.Add(new DeckTextError(number, DeckText.QuantityOutOfRange, card.ToString(SaveOptions.DisableFormatting)));
                continue;
            }

            var sideboard = string.Equals(card.Attribute("Sideboard")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            lines.Add(new DeckTextLine(number, quantity, name, null, null, null, [], [], string.Empty, sideboard ? "sideboard" : "main", []));
        }

        return new DeckTextResult(lines, errors);
    }

    public string Write(string deckFormat, IReadOnlyList<DeckTextLine> lines)
    {
        var commanderInSideboard = deckFormat is "commander" or "duel" or "predh" or "paupercommander";
        var deck = new XElement(
            "Deck",
            new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XElement("NetDeckID", 0),
            new XElement("PreconstructedDeckID", 0));
        foreach (var line in lines.Where(line => line.Zone is "main" or "commander" or "companion" or "sideboard"))
        {
            var sideboard = line.Zone is "sideboard" or "companion" || (line.Zone == "commander" && commanderInSideboard);
            deck.Add(new XElement(
                "Cards",
                new XAttribute("CatID", 0),
                new XAttribute("Quantity", line.Quantity),
                new XAttribute("Sideboard", sideboard ? "true" : "false"),
                new XAttribute("Name", line.Name),
                new XAttribute("Annotation", 0)));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), deck).Declaration + "\n" + deck.ToString() + "\n";
    }
}

/// <summary>
/// The text of Archidekt: <c>1x Sol Ring (c21) 263 *F* [Ramp,Commander{top}]</c>.
/// The categories Commander, Companion, Sideboard, Maybeboard and Tokens give the zone. The other categories become local tags.
/// </summary>
internal sealed partial class ArchidektFormat : IDeckFormat
{
    private static readonly Dictionary<string, string> ZoneCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Commander"] = "commander",
        ["Companion"] = "companion",
        ["Sideboard"] = "sideboard",
        ["Maybeboard"] = "considering",
        ["Tokens"] = "tokens",
        ["Token"] = "tokens",
    };

    public string Name => "archidekt";

    public string Extension => ".txt";

    public string ContentType => "text/plain";

    public DeckTextResult Parse(string content)
    {
        var lines = new List<DeckTextLine>();
        var errors = new List<DeckTextError>();
        var number = 0;
        foreach (var raw in content.Split('\n'))
        {
            number++;
            var text = LabelPattern().Replace(raw.TrimEnd('\r'), string.Empty).Trim();
            if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var categories = new List<string>();
            var match = CategoryPattern().Match(text);
            if (match.Success)
            {
                categories.AddRange(match.Groups["categories"].Value.Split(',')
                    .Select(category => BraceSuffix().Replace(category, string.Empty).Trim())
                    .Where(category => category.Length > 0));
                text = text[..match.Index].Trim();
            }

            var parsed = DeckText.Parse(text);
            if (parsed.Errors.Count > 0)
            {
                errors.Add(new DeckTextError(number, parsed.Errors[0].Code, raw.TrimEnd('\r')));
                continue;
            }

            if (parsed.Lines.Count == 0)
            {
                continue;
            }

            var zone = categories.Select(category => ZoneCategories.GetValueOrDefault(category)).FirstOrDefault(found => found is not null) ?? "main";
            var tags = categories.Where(category => !ZoneCategories.ContainsKey(category)).ToList();
            if (tags.Any(tag => tag.Length > DeckText.MaxTagLength))
            {
                errors.Add(new DeckTextError(number, DeckText.TagTooLong, raw.TrimEnd('\r')));
                continue;
            }

            lines.Add(parsed.Lines[0] with { LineNumber = number, Zone = zone, Tags = tags });
        }

        return new DeckTextResult(lines, errors);
    }

    public string Write(string deckFormat, IReadOnlyList<DeckTextLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append("x ").Append(line.Name);
            if (line.SetCode is { Length: > 0 })
            {
                builder.Append(" (").Append(line.SetCode).Append(')');
                if (line.CollectorNumber is { Length: > 0 })
                {
                    builder.Append(' ').Append(line.CollectorNumber);
                }
            }

            builder.Append(line.Finish switch { "foil" => " *F*", "etched" => " *E*", _ => string.Empty });
            var categories = line.Tags.Concat(line.GlobalTags).ToList();
            var zoneCategory = line.Zone switch
            {
                "commander" => "Commander{top}",
                "companion" => "Companion",
                "sideboard" => "Sideboard",
                "considering" => "Maybeboard",
                "tokens" => "Tokens",
                _ => null,
            };
            if (zoneCategory is not null)
            {
                categories.Add(zoneCategory);
            }

            if (categories.Count > 0)
            {
                builder.Append(" [").Append(string.Join(',', categories)).Append(']');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\s*\^[^^]*\^\s*$")]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"\s*\[(?<categories>[^\]]*)\]\s*$")]
    private static partial Regex CategoryPattern();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex BraceSuffix();
}

/// <summary>
/// Writes the lines of a text format in sections, one section for each zone of a list.
/// </summary>
internal static class Sectioned
{
    public static string Write(IReadOnlyList<DeckTextLine> lines, IReadOnlyList<(string Zone, string Header)> sections, Func<DeckTextLine, string> writeLine)
    {
        var builder = new StringBuilder();
        foreach (var (zone, header) in sections)
        {
            var zoneLines = lines.Where(line => line.Zone == zone).ToList();
            if (zoneLines.Count == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(header).Append('\n');
            foreach (var line in zoneLines)
            {
                builder.Append(writeLine(line)).Append('\n');
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// The reason codes of the import that are not codes of the deck text format.
/// </summary>
internal static class DeckFormatCodes
{
    /// <summary>The file is not a valid file of its format.</summary>
    public const string InvalidFile = "invalid_file";
}
