using Npgsql;
using NpgsqlTypes;

namespace Reliquary.Cards.Import;

/// <summary>
/// One column of a binary COPY into a table.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
/// <param name="Name">The name of the column.</param>
/// <param name="Type">The PostgreSQL type of the column.</param>
/// <param name="Value">Gets the value of the column from a row. A <see langword="null"/> value writes NULL.</param>
internal sealed record CopyColumn<T>(string Name, NpgsqlDbType Type, Func<T, object?> Value);

/// <summary>
/// Writes rows into a table with the binary COPY protocol, the fastest way to add many rows.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
/// <param name="table">The qualified name of the table.</param>
/// <param name="columns">The columns, in the order of the COPY command.</param>
internal sealed class CopyTable<T>(string table, IReadOnlyList<CopyColumn<T>> columns)
{
    /// <summary>Gets the qualified name of the table.</summary>
    public string Table { get; } = table;

    /// <summary>Gets the column names, separated by commas.</summary>
    public string ColumnList { get; } = string.Join(", ", columns.Select(column => column.Name));

    /// <summary>
    /// Starts a binary COPY on a connection.
    /// </summary>
    /// <param name="connection">The connection. It must stay free until the COPY completes.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The importer.</returns>
    public Task<NpgsqlBinaryImporter> BeginAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        connection.BeginBinaryImportAsync($"copy {Table} ({ColumnList}) from stdin (format binary)", cancellationToken);

    /// <summary>
    /// Writes one row.
    /// </summary>
    /// <param name="importer">The importer of this table.</param>
    /// <param name="row">The row.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the row is written.</returns>
    public async Task WriteAsync(NpgsqlBinaryImporter importer, T row, CancellationToken cancellationToken)
    {
        await importer.StartRowAsync(cancellationToken);
        foreach (var column in columns)
        {
            var value = column.Value(row);
            if (value is null)
            {
                await importer.WriteNullAsync(cancellationToken);
            }
            else
            {
                await importer.WriteAsync(value, column.Type, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Writes many rows in one COPY, then completes it.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The number of rows.</returns>
    public async Task<ulong> WriteAllAsync(NpgsqlConnection connection, IEnumerable<T> rows, CancellationToken cancellationToken)
    {
        await using var importer = await BeginAsync(connection, cancellationToken);
        foreach (var row in rows)
        {
            await WriteAsync(importer, row, cancellationToken);
        }

        return await importer.CompleteAsync(cancellationToken);
    }
}
