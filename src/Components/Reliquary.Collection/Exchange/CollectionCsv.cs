using System.Globalization;
using System.Text;
using Reliquary.BuildingBlocks.Collection;

namespace Reliquary.Collection.Exchange;

/// <summary>
/// The fields of a card that a CSV column can give.
/// </summary>
internal static class CsvFields
{
    public const string Quantity = "quantity";
    public const string Name = "name";
    public const string SetCode = "setCode";
    public const string CollectorNumber = "collectorNumber";
    public const string Language = "language";
    public const string Finish = "finish";
    public const string Condition = "condition";
    public const string PurchasePrice = "purchasePrice";
    public const string PurchaseCurrency = "purchaseCurrency";
    public const string AcquiredOn = "acquiredOn";
    public const string Note = "note";

    /// <summary>Gets all the fields.</summary>
    public static IReadOnlyList<string> All { get; } = [Quantity, Name, SetCode, CollectorNumber, Language, Finish, Condition, PurchasePrice, PurchaseCurrency, AcquiredOn, Note];
}

/// <summary>
/// A CSV format of a collection tool, or the generic CSV format.
/// </summary>
/// <remarks>
/// The import finds each field from the header of the file. A field has a list of header names, in order of priority.
/// </remarks>
/// <param name="name">The name of the format in the API.</param>
/// <param name="headers">The header names of each field. A field with no header name is not in the files of the format.</param>
/// <param name="columns">The columns of the export, with the value of each card.</param>
/// <param name="preamble">The text before the header of the export, for example <c>sep=,</c> for Dragon Shield.</param>
internal sealed class CsvCollectionFormat(
    string name,
    IReadOnlyDictionary<string, string[]> headers,
    IReadOnlyList<(string Header, Func<ExportCard, string> Value)> columns,
    string preamble = "") : ICollectionFormat
{
    public string Name => name;

    public string Extension => ".csv";

    public string ContentType => "text/csv";

    public ParsedFile Parse(string content)
    {
        var rows = CollectionCsv.Read(content);
        if (rows.Count == 0)
        {
            return new ParsedFile([], []);
        }

        var header = rows[0];
        var index = CsvFields.All.ToDictionary(
            field => field,
            field => headers.GetValueOrDefault(field, []).Select(candidate => header.Cells.FindIndex(cell => CollectionCsv.SameHeader(cell, candidate))).FirstOrDefault(found => found >= 0, -1),
            StringComparer.Ordinal);
        if (index[CsvFields.Name] < 0)
        {
            return new ParsedFile([], [new FileLineError(header.LineNumber, FileLineCodes.MissingColumn, string.Join(',', header.Cells))]);
        }

        var cards = new List<FileCard>();
        var errors = new List<FileLineError>();
        foreach (var row in rows.Skip(1))
        {
            string? Cell(string field) => index[field] is var column and >= 0 && column < row.Cells.Count && row.Cells[column].Trim() is { Length: > 0 } value ? value : null;
            var text = string.Join(',', row.Cells);
            if (Cell(CsvFields.Name) is not { } cardName)
            {
                if (row.Cells.Any(cell => cell.Trim().Length > 0))
                {
                    errors.Add(new FileLineError(row.LineNumber, FileLineCodes.MissingName, text));
                }

                continue;
            }

            var quantity = 1;
            if (Cell(CsvFields.Quantity) is { } quantityText
                && (!int.TryParse(quantityText, NumberStyles.None, CultureInfo.InvariantCulture, out quantity) || quantity is < 1 or > CollectionRules.MaxQuantity))
            {
                errors.Add(new FileLineError(row.LineNumber, FileLineCodes.QuantityOutOfRange, text));
                continue;
            }

            var setCode = Cell(CsvFields.SetCode)?.ToLowerInvariant();
            cards.Add(new FileCard(row.LineNumber, quantity, cardName, setCode, setCode is null ? null : Cell(CsvFields.CollectorNumber))
            {
                Language = Cell(CsvFields.Language),
                Finish = Cell(CsvFields.Finish),
                Condition = Cell(CsvFields.Condition),
                PurchasePrice = Cell(CsvFields.PurchasePrice),
                PurchaseCurrency = Cell(CsvFields.PurchaseCurrency),
                AcquiredOn = Cell(CsvFields.AcquiredOn),
                Note = Cell(CsvFields.Note),
            });
        }

        return new ParsedFile(cards, errors);
    }

    public string Write(IReadOnlyList<ExportCard> cards)
    {
        var builder = new StringBuilder(preamble);
        CollectionCsv.WriteRow(builder, columns.Select(column => column.Header));
        foreach (var card in cards)
        {
            CollectionCsv.WriteRow(builder, columns.Select(column => column.Value(card)));
        }

        return builder.ToString();
    }
}

/// <summary>
/// The generic CSV format and the CSV formats of the collection tools.
/// </summary>
internal static class CollectionCsvFormats
{
    /// <summary>
    /// Gets the generic CSV format. Its export keeps all the fields that the owner enabled, and the location of each entry.
    /// </summary>
    public static CsvCollectionFormat Generic { get; } = new(
        "csv",
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [CsvFields.Quantity] = ["Quantity", "Count", "Qty", "Amount"],
            [CsvFields.Name] = ["Name", "Card name", "Card"],
            [CsvFields.SetCode] = ["Set code", "Set", "Edition code"],
            [CsvFields.CollectorNumber] = ["Collector number", "Card number", "Number"],
            [CsvFields.Language] = ["Language", "Lang"],
            [CsvFields.Finish] = ["Finish", "Foil", "Printing"],
            [CsvFields.Condition] = ["Condition"],
            [CsvFields.PurchasePrice] = ["Purchase price", "Price bought", "Price"],
            [CsvFields.PurchaseCurrency] = ["Purchase currency", "Purchase price currency", "Currency"],
            [CsvFields.AcquiredOn] = ["Entry date", "Acquired on", "Date bought", "Date added"],
            [CsvFields.Note] = ["Note", "Notes"],
        },
        [
            ("Quantity", card => FileValues.Quantity(card.Quantity)),
            ("Name", card => card.Name),
            ("Set code", card => card.SetCode ?? string.Empty),
            ("Collector number", card => card.CollectorNumber ?? string.Empty),
            ("Language", card => card.Language),
            ("Finish", card => card.Finish ?? string.Empty),
            ("Condition", card => card.Condition ?? string.Empty),
            ("Purchase price", card => FileValues.Price(card.PurchasePrice)),
            ("Purchase currency", card => card.PurchasePrice is null ? string.Empty : card.PurchaseCurrency ?? string.Empty),
            ("Entry date", card => FileValues.Date(card.AcquiredOn)),
            ("Note", card => card.Note ?? string.Empty),
            ("Location", card => card.Location),
        ]);

    /// <summary>Gets the collection CSV format of Moxfield.</summary>
    public static CsvCollectionFormat Moxfield { get; } = new(
        "moxfield",
        Fields(
            (CsvFields.Quantity, "Count"),
            (CsvFields.Name, "Name"),
            (CsvFields.SetCode, "Edition"),
            (CsvFields.CollectorNumber, "Collector Number"),
            (CsvFields.Language, "Language"),
            (CsvFields.Finish, "Foil"),
            (CsvFields.Condition, "Condition"),
            (CsvFields.PurchasePrice, "Purchase Price")),
        [
            ("Count", card => FileValues.Quantity(card.Quantity)),
            ("Tradelist Count", _ => "0"),
            ("Name", card => card.Name),
            ("Edition", card => card.SetCode ?? string.Empty),
            ("Condition", card => UsCondition(card.Condition)),
            ("Language", card => FileValues.LanguageName(card.Language)),
            ("Foil", card => card.Finish is "foil" or "etched" ? card.Finish : string.Empty),
            ("Tags", _ => string.Empty),
            ("Last Modified", _ => string.Empty),
            ("Collector Number", card => card.CollectorNumber ?? string.Empty),
            ("Alter", _ => "False"),
            ("Proxy", _ => "False"),
            ("Purchase Price", card => FileValues.Price(card.PurchasePrice)),
        ]);

    /// <summary>Gets the CSV formats of Deckbox, ManaBox and Dragon Shield.</summary>
    public static IReadOnlyList<CsvCollectionFormat> Tools { get; } =
    [
        new(
            "deckbox",
            Fields(
                (CsvFields.Quantity, "Count"),
                (CsvFields.Name, "Name"),
                (CsvFields.SetCode, "Edition Code"),
                (CsvFields.CollectorNumber, "Card Number"),
                (CsvFields.Language, "Language"),
                (CsvFields.Finish, "Foil"),
                (CsvFields.Condition, "Condition")),
            [
                ("Count", card => FileValues.Quantity(card.Quantity)),
                ("Tradelist Count", _ => "0"),
                ("Name", card => card.Name),
                ("Edition Code", card => card.SetCode ?? string.Empty),
                ("Card Number", card => card.CollectorNumber ?? string.Empty),
                ("Condition", card => DeckboxCondition(card.Condition)),
                ("Language", card => FileValues.LanguageName(card.Language)),
                ("Foil", card => card.Finish is "foil" or "etched" ? card.Finish : string.Empty),
            ]),
        new(
            "manabox",
            Fields(
                (CsvFields.Quantity, "Quantity"),
                (CsvFields.Name, "Name"),
                (CsvFields.SetCode, "Set code"),
                (CsvFields.CollectorNumber, "Collector number"),
                (CsvFields.Language, "Language"),
                (CsvFields.Finish, "Foil"),
                (CsvFields.Condition, "Condition"),
                (CsvFields.PurchasePrice, "Purchase price"),
                (CsvFields.PurchaseCurrency, "Purchase price currency")),
            [
                ("Name", card => card.Name),
                ("Set code", card => card.SetCode?.ToUpperInvariant() ?? string.Empty),
                ("Collector number", card => card.CollectorNumber ?? string.Empty),
                ("Foil", card => card.Finish is "foil" or "etched" ? card.Finish : "normal"),
                ("Quantity", card => FileValues.Quantity(card.Quantity)),
                ("Purchase price", card => FileValues.Price(card.PurchasePrice)),
                ("Condition", card => card.Condition ?? string.Empty),
                ("Language", card => card.Language),
                ("Purchase price currency", card => card.PurchasePrice is null ? string.Empty : card.PurchaseCurrency ?? string.Empty),
            ]),
        new(
            "dragonshield",
            Fields(
                (CsvFields.Quantity, "Quantity"),
                (CsvFields.Name, "Card Name"),
                (CsvFields.SetCode, "Set Code"),
                (CsvFields.CollectorNumber, "Card Number"),
                (CsvFields.Language, "Language"),
                (CsvFields.Finish, "Printing"),
                (CsvFields.Condition, "Condition"),
                (CsvFields.PurchasePrice, "Price Bought"),
                (CsvFields.AcquiredOn, "Date Bought")),
            [
                ("Folder Name", card => card.Location),
                ("Quantity", card => FileValues.Quantity(card.Quantity)),
                ("Trade Quantity", _ => "0"),
                ("Card Name", card => card.Name),
                ("Set Code", card => card.SetCode?.ToUpperInvariant() ?? string.Empty),
                ("Set Name", _ => string.Empty),
                ("Card Number", card => card.CollectorNumber ?? string.Empty),
                ("Condition", card => DragonShieldCondition(card.Condition)),
                ("Printing", card => card.Finish switch { "foil" => "Foil", "etched" => "Etched", _ => "Normal" }),
                ("Language", card => FileValues.LanguageName(card.Language)),
                ("Price Bought", card => FileValues.Price(card.PurchasePrice)),
                ("Date Bought", card => FileValues.Date(card.AcquiredOn)),
            ],
            "sep=,\n"),
    ];

    /// <summary>Writes a condition in the scale of TCGplayer, which Moxfield uses.</summary>
    private static string UsCondition(string? condition) => condition switch
    {
        CardConditions.Mint => "Mint",
        CardConditions.NearMint => "Near Mint",
        CardConditions.Excellent or CardConditions.Good or CardConditions.LightPlayed => "Lightly Played",
        CardConditions.Played => "Moderately Played",
        CardConditions.Poor => "Damaged",
        _ => string.Empty,
    };

    /// <summary>Writes a condition in the scale of Deckbox.</summary>
    private static string DeckboxCondition(string? condition) => condition switch
    {
        CardConditions.Mint => "Mint",
        CardConditions.NearMint => "Near Mint",
        CardConditions.Excellent or CardConditions.Good or CardConditions.LightPlayed => "Good (Lightly Played)",
        CardConditions.Played => "Played",
        CardConditions.Poor => "Poor",
        _ => string.Empty,
    };

    /// <summary>Writes a condition in the scale of Dragon Shield.</summary>
    private static string DragonShieldCondition(string? condition) => condition switch
    {
        CardConditions.Mint => "Mint",
        CardConditions.NearMint => "NearMint",
        CardConditions.Excellent => "Excellent",
        CardConditions.Good => "Good",
        CardConditions.LightPlayed => "LightPlayed",
        CardConditions.Played => "Played",
        CardConditions.Poor => "Poor",
        _ => string.Empty,
    };

    private static Dictionary<string, string[]> Fields(params (string Field, string Header)[] headers) =>
        headers.ToDictionary(pair => pair.Field, pair => new[] { pair.Header }, StringComparer.Ordinal);
}

/// <summary>
/// One row of a CSV file.
/// </summary>
/// <param name="LineNumber">The number of the line where the row starts. The first line has the number 1.</param>
/// <param name="Cells">The cells.</param>
internal sealed record CsvRow(int LineNumber, List<string> Cells);

/// <summary>
/// Reads and writes CSV files (RFC 4180). The reader accepts a comma, a semicolon or a tab as separator.
/// </summary>
internal static class CollectionCsv
{
    private static readonly char[] Separators = [',', ';', '\t'];

    /// <summary>
    /// Reads the rows of a file. It ignores a first line <c>sep=,</c> and gets the separator from the header.
    /// </summary>
    public static List<CsvRow> Read(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var text = content.TrimStart('﻿');
        var line = 1;
        var firstLineEnd = text.IndexOf('\n', StringComparison.Ordinal);
        var firstLine = (firstLineEnd < 0 ? text : text[..firstLineEnd]).Trim().Trim('"');
        char separator;
        if (firstLine.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && firstLine.Length == 5)
        {
            separator = firstLine[4];
            text = firstLineEnd < 0 ? string.Empty : text[(firstLineEnd + 1)..];
            line = 2;
        }
        else
        {
            separator = Separators.MaxBy(candidate => firstLine.Count(character => character == candidate));
        }

        var rows = new List<CsvRow>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var rowStart = line;
        for (var position = 0; position < text.Length; position++)
        {
            var character = text[position];
            if (quoted)
            {
                if (character == '"' && position + 1 < text.Length && text[position + 1] == '"')
                {
                    cell.Append('"');
                    position++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    line += character == '\n' ? 1 : 0;
                    cell.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == separator)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (character == '\n')
            {
                cells.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                AddRow(rows, rowStart, cells);
                cells = [];
                line++;
                rowStart = line;
            }
            else
            {
                cell.Append(character);
            }
        }

        cells.Add(cell.ToString().TrimEnd('\r'));
        AddRow(rows, rowStart, cells);
        return rows;
    }

    /// <summary>
    /// Compares two header names. The comparison ignores the case, the spaces, the hyphens and the underscores.
    /// </summary>
    public static bool SameHeader(string header, string candidate) =>
        string.Equals(Normalize(header), Normalize(candidate), StringComparison.Ordinal);

    /// <summary>
    /// Writes one row with a comma as separator. It puts a cell in quotes when the cell contains a comma, a quote or a line break.
    /// </summary>
    public static void WriteRow(StringBuilder builder, IEnumerable<string> cells)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AppendJoin(',', cells.Select(cell => cell.AsSpan().IndexOfAny(",\"\n\r") >= 0 ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : cell));
        builder.Append('\n');
    }

    private static void AddRow(List<CsvRow> rows, int lineNumber, List<string> cells)
    {
        if (cells.Count > 1 || cells[0].Trim().Length > 0)
        {
            rows.Add(new CsvRow(lineNumber, cells));
        }
    }

    private static string Normalize(string header) =>
        new([.. header.Trim().Where(character => character is not (' ' or '-' or '_')).Select(char.ToLowerInvariant)]);
}
