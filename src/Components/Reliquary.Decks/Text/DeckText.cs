using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Reliquary.Decks.Text;

/// <summary>
/// One line of a deck in the text format.
/// </summary>
/// <param name="LineNumber">The number of the line in the text. The first line has the number 1.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="Name">The card name, as the text gives it.</param>
/// <param name="SetCode">The set code in lowercase, or <see langword="null"/>.</param>
/// <param name="CollectorNumber">The collector number, or <see langword="null"/>.</param>
/// <param name="Finish">The finish: <c>foil</c> or <c>etched</c>, or <see langword="null"/>.</param>
/// <param name="Tags">The local tags of the deck.</param>
/// <param name="GlobalTags">The global tags of the user.</param>
/// <param name="Note">The note, or an empty text.</param>
/// <param name="Zone">The zone of the section of the line.</param>
public sealed record DeckTextLine(
    int LineNumber,
    int Quantity,
    string Name,
    string? SetCode,
    string? CollectorNumber,
    string? Finish,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> GlobalTags,
    string Note,
    string Zone);

/// <summary>
/// A line of the text that the parser cannot read.
/// </summary>
/// <param name="LineNumber">The number of the line. The first line has the number 1.</param>
/// <param name="Code">The stable reason code, for example <c>missing_name</c>.</param>
/// <param name="Text">The text of the line.</param>
public sealed record DeckTextError(int LineNumber, string Code, string Text);

/// <summary>
/// The result of the parser: the lines that it read, and the lines that it cannot read.
/// </summary>
/// <param name="Lines">The deck lines.</param>
/// <param name="Errors">The errors.</param>
public sealed record DeckTextResult(IReadOnlyList<DeckTextLine> Lines, IReadOnlyList<DeckTextError> Errors);

/// <summary>
/// Reads and writes the deck text format. See docs/development/deck-text-format.md.
/// </summary>
/// <remarks>
/// The format accepts the bulk edit syntax of Moxfield:
/// <code>
/// Commander
/// 1 Atraxa, Praetors' Voice (2X2) 190 *F*
///
/// Main
/// 1 Sol Ring (C21) 263 #Ramp #!Mana Rock -- Turn one
/// 10 Forest
/// </code>
/// </remarks>
public static partial class DeckText
{
    /// <summary>The reason code of a line with no card name.</summary>
    public const string MissingName = "missing_name";

    /// <summary>The reason code of a quantity that is not from 1 to 999.</summary>
    public const string QuantityOutOfRange = "quantity_out_of_range";

    /// <summary>The reason code of a tag that is too long.</summary>
    public const string TagTooLong = "tag_too_long";

    /// <summary>The maximum length of a tag.</summary>
    public const int MaxTagLength = 50;

    private const string NoteSeparator = " -- ";

    private static readonly Dictionary<string, string> SectionZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["commander"] = "commander",
        ["commanders"] = "commander",
        ["companion"] = "companion",
        ["companions"] = "companion",
        ["deck"] = "main",
        ["main"] = "main",
        ["main deck"] = "main",
        ["mainboard"] = "main",
        ["sideboard"] = "sideboard",
        ["side"] = "sideboard",
        ["considering"] = "considering",
        ["maybeboard"] = "considering",
        ["maybe"] = "considering",
        ["tokens"] = "tokens",
        ["token"] = "tokens",
    };

    private static readonly Dictionary<string, string> ZoneHeaders = new(StringComparer.Ordinal)
    {
        ["commander"] = "Commander",
        ["companion"] = "Companion",
        ["main"] = "Main",
        ["sideboard"] = "Sideboard",
        ["considering"] = "Considering",
        ["tokens"] = "Tokens",
    };

    /// <summary>
    /// Reads a deck text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="defaultZone">The zone of the lines before the first section header.</param>
    /// <returns>The lines and the errors.</returns>
    public static DeckTextResult Parse(string text, string defaultZone = "main")
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<DeckTextLine>();
        var errors = new List<DeckTextError>();
        var zone = defaultZone;
        var number = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            number++;
            var line = rawLine.Trim().TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryReadHeader(line, out var headerZone))
            {
                zone = headerZone;
                continue;
            }

            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var result = ParseLine(line, number, zone);
            if (result.Line is not null)
            {
                lines.Add(result.Line);
            }
            else
            {
                errors.Add(new DeckTextError(number, result.Error!, rawLine.TrimEnd('\r')));
            }
        }

        return new DeckTextResult(lines, errors);
    }

    /// <summary>
    /// Writes deck lines as text, with one section for each zone that has lines.
    /// </summary>
    /// <param name="lines">The lines. The text keeps their order in each zone.</param>
    /// <returns>The text.</returns>
    public static string Write(IEnumerable<DeckTextLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var byZone = lines.GroupBy(line => line.Zone).ToDictionary(group => group.Key, group => group.ToList());
        var builder = new StringBuilder();
        foreach (var zone in DeckRules.Zones.Where(byZone.ContainsKey))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(ZoneHeaders[zone]).Append('\n');
            foreach (var line in byZone[zone])
            {
                builder.Append(WriteLine(line)).Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes one deck line without its zone.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The text of the line.</returns>
    public static string WriteLine(DeckTextLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var builder = new StringBuilder();
        builder.Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(line.Name);
        if (line.SetCode is { Length: > 0 } set)
        {
            builder.Append(" (").Append(set.ToUpperInvariant()).Append(')');
            if (line.CollectorNumber is { Length: > 0 } collectorNumber)
            {
                builder.Append(' ').Append(collectorNumber);
            }
        }

        builder.Append(line.Finish switch
        {
            "foil" => " *F*",
            "etched" => " *E*",
            _ => string.Empty,
        });
        foreach (var tag in line.Tags)
        {
            builder.Append(" #").Append(tag);
        }

        foreach (var tag in line.GlobalTags)
        {
            builder.Append(" #!").Append(tag);
        }

        if (line.Note.Length > 0)
        {
            builder.Append(NoteSeparator).Append(line.Note);
        }

        return builder.ToString();
    }

    private static bool TryReadHeader(string line, out string zone)
    {
        var header = HeaderPattern().Match(line);
        if (header.Success && SectionZones.TryGetValue(WhiteSpace().Replace(header.Groups["name"].Value.Trim(), " "), out var found))
        {
            zone = found;
            return true;
        }

        zone = string.Empty;
        return false;
    }

    private static (DeckTextLine? Line, string? Error) ParseLine(string line, int number, string zone)
    {
        var quantity = 1;
        var rest = line;
        var quantityMatch = QuantityPattern().Match(line);
        if (quantityMatch.Success)
        {
            if (!int.TryParse(quantityMatch.Groups["quantity"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out quantity)
                || quantity is < 1 or > DeckRules.MaxQuantity)
            {
                return (null, QuantityOutOfRange);
            }

            rest = quantityMatch.Groups["rest"].Value;
        }

        var note = string.Empty;
        var noteIndex = rest.IndexOf(NoteSeparator, StringComparison.Ordinal);
        if (noteIndex >= 0)
        {
            note = rest[(noteIndex + NoteSeparator.Length)..].Trim();
            rest = rest[..noteIndex];
        }

        var tags = new List<string>();
        var globalTags = new List<string>();
        // A space before the rest finds a tag at the start, for example "3 #ramp", which has no card name.
        rest = " " + rest;
        var tagIndex = rest.IndexOf(" #", StringComparison.Ordinal);
        if (tagIndex >= 0)
        {
            foreach (var part in rest[(tagIndex + 2)..].Split(" #", StringSplitOptions.RemoveEmptyEntries))
            {
                var global = part.StartsWith('!');
                var tag = (global ? part[1..] : part).Trim();
                if (tag.Length == 0)
                {
                    continue;
                }

                if (tag.Length > MaxTagLength)
                {
                    return (null, TagTooLong);
                }

                (global ? globalTags : tags).Add(tag);
            }

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
            return (null, MissingName);
        }

        return (new DeckTextLine(number, quantity, name, setCode, collectorNumber, finish, tags, globalTags, note, zone), null);
    }

    [GeneratedRegex(@"^(?://\s*)?(?<name>[A-Za-z][A-Za-z ]*?)\s*:?\s*(?:\(\d+\))?$")]
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
