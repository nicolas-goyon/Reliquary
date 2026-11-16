using System.Text;

namespace Reliquary.BuildingBlocks.Text;

/// <summary>
/// One row of a CSV file.
/// </summary>
/// <param name="LineNumber">The number of the line where the row starts. The first line has the number 1.</param>
/// <param name="Cells">The cells.</param>
public sealed record CsvRow(int LineNumber, List<string> Cells);

/// <summary>
/// Reads and writes CSV files (RFC 4180). The reader accepts a comma, a semicolon or a tab as separator.
/// </summary>
/// <remarks>
/// The core components use this class for their CSV import and export formats.
/// The reader accepts a byte order mark, CRLF and LF line ends, quoted cells, escaped quotes and empty cells.
/// It ignores empty lines.
/// </remarks>
public static class Csv
{
    private static readonly char[] Separators = [',', ';', '\t'];

    /// <summary>
    /// Reads the rows of a file. It ignores a first line <c>sep=,</c> and gets the separator from the header.
    /// </summary>
    /// <param name="content">The text of the file.</param>
    /// <returns>The rows that are not empty, in the order of the file.</returns>
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
    /// <param name="header">The header name in the file.</param>
    /// <param name="candidate">A header name that the format knows.</param>
    /// <returns><see langword="true"/> when the two names are the same.</returns>
    public static bool SameHeader(string header, string candidate) =>
        string.Equals(Normalize(header), Normalize(candidate), StringComparison.Ordinal);

    /// <summary>
    /// Writes one row with a comma as separator. It puts a cell in quotes when the cell contains a comma, a quote or a line break.
    /// </summary>
    /// <param name="builder">The text of the file.</param>
    /// <param name="cells">The cells of the row.</param>
    public static void WriteRow(StringBuilder builder, IEnumerable<string> cells)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cells);
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
