using System.Text.Json;

namespace Reliquary.Cards.Tests.Search;

/// <summary>
/// One query of the parity tests, with the Oracle cards that the Scryfall search finds in the demo dataset.
/// </summary>
/// <param name="Query">The query.</param>
/// <param name="Section">The section of the Scryfall syntax guide.</param>
/// <param name="Origin">The origin of the query: <c>syntax-guide</c>, <c>syntax-guide-text</c> or <c>reliquary</c>.</param>
/// <param name="Expected">The expected Oracle identifiers.</param>
/// <param name="KnownDifference">The reason why Reliquary cannot find the same cards, or <see langword="null"/>.</param>
public sealed record SearchParityEntry(string Query, string Section, string Origin, IReadOnlyList<Guid> Expected, string? KnownDifference)
{
    /// <inheritdoc />
    public override string ToString() => Query;
}

/// <summary>
/// Reads the file <c>SearchParity/expectations.json</c>. The script <c>tools/search-parity/record_expectations.py</c> writes it.
/// </summary>
public static class SearchParityData
{
    private static readonly Lazy<IReadOnlyList<SearchParityEntry>> Entries = new(Read);

    /// <summary>Gets the folder of the demo dataset in the repository.</summary>
    public static string DemoDirectory { get; } = Path.Combine(RepositoryRoot.Path, "data", "demo");

    /// <summary>
    /// Gets all the queries.
    /// </summary>
    /// <returns>The queries.</returns>
    public static IReadOnlyList<SearchParityEntry> Load() => Entries.Value;

    /// <summary>
    /// Gets the queries as test data.
    /// </summary>
    /// <returns>The queries.</returns>
    public static TheoryData<string> Queries() => [.. Load().Select(entry => entry.Query)];

    private static List<SearchParityEntry> Read()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SearchParity", "expectations.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return
        [
            .. document.RootElement.GetProperty("queries").EnumerateArray().Select(entry => new SearchParityEntry(
                entry.GetProperty("query").GetString()!,
                entry.GetProperty("section").GetString()!,
                entry.GetProperty("origin").GetString()!,
                [.. entry.GetProperty("expected").EnumerateArray().Select(id => id.GetGuid())],
                entry.TryGetProperty("knownDifference", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null)),
        ];
    }
}

/// <summary>
/// Finds the root folder of the repository from the folder of the test assembly.
/// </summary>
internal static class RepositoryRoot
{
    /// <summary>Gets the full path of the folder that contains <c>Reliquary.slnx</c>.</summary>
    public static string Path { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Reliquary.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"The test cannot find Reliquary.slnx in a parent folder of {AppContext.BaseDirectory}.");
    }
}
