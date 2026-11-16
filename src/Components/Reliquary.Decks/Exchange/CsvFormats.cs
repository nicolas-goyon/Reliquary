using System.Globalization;
using System.Text;
using Reliquary.BuildingBlocks.Text;
using Reliquary.Decks.Text;

namespace Reliquary.Decks.Exchange;

/// <summary>
/// The fields of a deck line that a CSV column can give.
/// </summary>
internal static class CsvFields
{
    public const string Quantity = "quantity";
    public const string Name = "name";
    public const string SetCode = "setCode";
    public const string CollectorNumber = "collectorNumber";
    public const string Finish = "finish";
    public const string Zone = "zone";
    public const string Tags = "tags";
    public const string GlobalTags = "globalTags";
    public const string Categories = "categories";
    public const string Note = "note";

    /// <summary>Gets all the fields.</summary>
    public static IReadOnlyList<string> All { get; } = [Quantity, Name, SetCode, CollectorNumber, Finish, Zone, Tags, GlobalTags, Categories, Note];
}

/// <summary>
/// A CSV format of a collection tool or the generic CSV format.
/// </summary>
/// <remarks>
/// The import finds each field from the header of the file. A field has a list of header names, in order of priority.
/// A column map from the user replaces the header names of the generic format.
/// A cell of tags or categories separates its items with the list separator of the format.
/// </remarks>
internal sealed class CsvDeckFormat(
    string name,
    IReadOnlyDictionary<string, string[]> headers,
    IReadOnlyList<(string Header, Func<DeckTextLine, string> Value)> columns,
    Func<DeckTextLine, bool> exported,
    char listSeparator) : IDeckFormat
{
    public string Name => name;

    public string Extension => ".csv";

    public string ContentType => "text/csv";

    /// <summary>Gets the header names of each field.</summary>
    public IReadOnlyDictionary<string, string[]> Headers => headers;

    /// <summary>
    /// Makes a format with the header names of a user for some fields. The other fields keep their header names.
    /// </summary>
    /// <param name="map">The header name of each field, by field.</param>
    public CsvDeckFormat WithColumns(IReadOnlyDictionary<string, string> map)
    {
        var changed = headers.ToDictionary(pair => pair.Key, pair => map.TryGetValue(pair.Key, out var header) ? [header] : pair.Value, StringComparer.Ordinal);
        return new CsvDeckFormat(name, changed, columns, exported, listSeparator);
    }

    public DeckTextResult Parse(string content)
    {
        var rows = Csv.Read(content);
        if (rows.Count == 0)
        {
            return new DeckTextResult([], []);
        }

        var header = rows[0];
        var index = headers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(candidate => header.Cells.FindIndex(cell => Csv.SameHeader(cell, candidate))).FirstOrDefault(found => found >= 0, -1),
            StringComparer.Ordinal);
        if (index[CsvFields.Name] < 0)
        {
            return new DeckTextResult([], [new DeckTextError(header.LineNumber, DeckFormatCodes.MissingColumn, string.Join(',', header.Cells))]);
        }

        var lines = new List<DeckTextLine>();
        var errors = new List<DeckTextError>();
        foreach (var row in rows.Skip(1))
        {
            string Cell(string field) => index[field] is var column and >= 0 && column < row.Cells.Count ? row.Cells[column].Trim() : string.Empty;
            var text = string.Join(',', row.Cells);
            var cardName = Cell(CsvFields.Name);
            if (cardName.Length == 0)
            {
                if (row.Cells.Any(cell => cell.Trim().Length > 0))
                {
                    errors.Add(new DeckTextError(row.LineNumber, DeckText.MissingName, text));
                }

                continue;
            }

            var quantityText = Cell(CsvFields.Quantity);
            var quantity = 1;
            if (quantityText.Length > 0
                && (!int.TryParse(quantityText, NumberStyles.None, CultureInfo.InvariantCulture, out quantity) || quantity is < 1 or > DeckRules.MaxQuantity))
            {
                errors.Add(new DeckTextError(row.LineNumber, DeckText.QuantityOutOfRange, text));
                continue;
            }

            var zone = "main";
            if (Cell(CsvFields.Zone) is { Length: > 0 } zoneText && !DeckText.TryReadZone(zoneText, out zone))
            {
                errors.Add(new DeckTextError(row.LineNumber, DeckFormatCodes.UnknownZone, text));
                continue;
            }

            var tags = SplitList(Cell(CsvFields.Tags));
            var globalTags = SplitList(Cell(CsvFields.GlobalTags));
            if (tags.Concat(globalTags).Any(tag => tag.Length > DeckText.MaxTagLength))
            {
                errors.Add(new DeckTextError(row.LineNumber, DeckText.TagTooLong, text));
                continue;
            }

            var categories = SplitList(Cell(CsvFields.Categories)).Select(category => category.ToLowerInvariant()).ToList();
            if (categories.Any(category => !DeckRules.Categories.Contains(category)))
            {
                errors.Add(new DeckTextError(row.LineNumber, DeckText.UnknownCategory, text));
                continue;
            }

            var setCode = Cell(CsvFields.SetCode);
            var collectorNumber = Cell(CsvFields.CollectorNumber);
            lines.Add(new DeckTextLine(
                row.LineNumber,
                quantity,
                cardName,
                setCode.Length > 0 ? setCode.ToLowerInvariant() : null,
                setCode.Length > 0 && collectorNumber.Length > 0 ? collectorNumber : null,
                ReadFinish(Cell(CsvFields.Finish)),
                tags,
                globalTags,
                Cell(CsvFields.Note),
                zone,
                categories));
        }

        return new DeckTextResult(lines, errors);
    }

    public string Write(string deckFormat, IReadOnlyList<DeckTextLine> lines)
    {
        var builder = new StringBuilder();
        Csv.WriteRow(builder, columns.Select(column => column.Header));
        foreach (var line in lines.Where(exported))
        {
            Csv.WriteRow(builder, columns.Select(column => column.Value(line)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a finish. The tools write <c>foil</c>, <c>etched</c>, <c>true</c>, <c>yes</c> or <c>1</c> for a foil card.
    /// </summary>
    internal static string? ReadFinish(string value) => value.ToLowerInvariant() switch
    {
        "foil" or "true" or "yes" or "1" => "foil",
        "etched" or "etched foil" => "etched",
        _ => null,
    };

    private List<string> SplitList(string value) =>
        [.. value.Split(listSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

/// <summary>
/// The CSV formats of the collection tools, and the generic CSV format.
/// </summary>
internal static class CsvFormats
{
    /// <summary>The name of the generic CSV format. Only this format accepts a column map.</summary>
    public const string GenericName = "csv";

    private static readonly Func<DeckTextLine, bool> PlayedZones = line => line.Zone is "commander" or "companion" or "main";

    /// <summary>
    /// Gets the generic CSV format. Its export keeps all the data of the lines.
    /// </summary>
    public static CsvDeckFormat Generic { get; } = new(
        GenericName,
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [CsvFields.Quantity] = ["Quantity", "Count", "Qty", "Amount"],
            [CsvFields.Name] = ["Name", "Card name", "Card"],
            [CsvFields.SetCode] = ["Set code", "Set", "Edition code"],
            [CsvFields.CollectorNumber] = ["Collector number", "Card number", "Number"],
            [CsvFields.Finish] = ["Finish", "Foil", "Printing"],
            [CsvFields.Zone] = ["Zone", "Board", "Section"],
            [CsvFields.Tags] = ["Tags", "Tag"],
            [CsvFields.GlobalTags] = ["Global tags"],
            [CsvFields.Categories] = ["Categories"],
            [CsvFields.Note] = ["Note", "Notes"],
        },
        [
            ("Quantity", line => line.Quantity.ToString(CultureInfo.InvariantCulture)),
            ("Name", line => line.Name),
            ("Set code", line => line.SetCode ?? string.Empty),
            ("Collector number", line => line.CollectorNumber ?? string.Empty),
            ("Finish", line => line.Finish ?? "nonfoil"),
            ("Zone", line => line.Zone),
            ("Tags", line => string.Join(';', line.Tags)),
            ("Global tags", line => string.Join(';', line.GlobalTags)),
            ("Categories", line => string.Join(';', line.Categories)),
            ("Note", line => line.Note),
        ],
        _ => true,
        ';');

    /// <summary>
    /// Gets the CSV formats of the collection tools. A collection has no zones, so their export keeps only the commanders, the companions and the main deck.
    /// </summary>
    public static IReadOnlyList<CsvDeckFormat> Tools { get; } =
    [
        new(
            "deckbox",
            Fields(quantity: "Count", name: "Name", setCode: "Edition Code", collectorNumber: "Card Number", finish: "Foil", tags: "Tags"),
            [
                ("Count", line => line.Quantity.ToString(CultureInfo.InvariantCulture)),
                ("Name", line => line.Name),
                ("Edition Code", line => line.SetCode ?? string.Empty),
                ("Card Number", line => line.CollectorNumber ?? string.Empty),
                ("Foil", line => line.Finish ?? string.Empty),
                ("Tags", line => string.Join(',', line.Tags.Concat(line.GlobalTags))),
            ],
            PlayedZones,
            ','),
        new(
            "manabox",
            Fields(quantity: "Quantity", name: "Name", setCode: "Set code", collectorNumber: "Collector number", finish: "Foil"),
            [
                ("Name", line => line.Name),
                ("Set code", line => line.SetCode ?? string.Empty),
                ("Collector number", line => line.CollectorNumber ?? string.Empty),
                ("Foil", line => line.Finish ?? "normal"),
                ("Quantity", line => line.Quantity.ToString(CultureInfo.InvariantCulture)),
            ],
            PlayedZones,
            ','),
        new(
            "dragonshield",
            Fields(quantity: "Quantity", name: "Card Name", setCode: "Set Code", collectorNumber: "Card Number", finish: "Printing"),
            [
                ("Quantity", line => line.Quantity.ToString(CultureInfo.InvariantCulture)),
                ("Card Name", line => line.Name),
                ("Set Code", line => line.SetCode?.ToUpperInvariant() ?? string.Empty),
                ("Card Number", line => line.CollectorNumber ?? string.Empty),
                ("Printing", line => line.Finish switch { "foil" => "Foil", "etched" => "Etched", _ => "Normal" }),
            ],
            PlayedZones,
            ','),
    ];

    private static Dictionary<string, string[]> Fields(
        string quantity, string name, string setCode, string collectorNumber, string finish, string? tags = null)
    {
        var fields = CsvFields.All.ToDictionary(field => field, _ => Array.Empty<string>(), StringComparer.Ordinal);
        fields[CsvFields.Quantity] = [quantity];
        fields[CsvFields.Name] = [name];
        fields[CsvFields.SetCode] = [setCode];
        fields[CsvFields.CollectorNumber] = [collectorNumber];
        fields[CsvFields.Finish] = [finish];
        if (tags is not null)
        {
            fields[CsvFields.Tags] = [tags];
        }

        return fields;
    }
}
