using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reliquary.Cards.Import.Scryfall;

namespace Reliquary.Cards.Import;

/// <summary>
/// The kinds of source files of the card data.
/// </summary>
internal static class SourceKinds
{
    public const string Sets = "sets";
    public const string Cards = "cards";
    public const string OracleTags = "oracle_tags";
    public const string IllustrationTags = "art_tags";
    public const string Rulings = "rulings";
    public const string Identifiers = "card_identifiers";

    /// <summary>Gets the kinds in the order of the import.</summary>
    public static IReadOnlyList<string> InOrder { get; } = [Sets, Cards, OracleTags, IllustrationTags, Rulings, Identifiers];
}

/// <summary>
/// A source file of the card data.
/// </summary>
/// <param name="Kind">The kind of the file. See <see cref="SourceKinds"/>.</param>
/// <param name="Name">The name of the source, for the import runs, for example <c>scryfall:all_cards</c>.</param>
/// <param name="UpdatedAt">The time of the last change of the file at its source, when the source gives it.</param>
/// <param name="Open">Opens the content of the file, without compression.</param>
internal sealed record SourceFile(string Kind, string Name, DateTimeOffset? UpdatedAt, Func<CancellationToken, Task<Stream>> Open);

/// <summary>
/// Gives the source files of the card data.
/// </summary>
internal interface ICardDataSources
{
    /// <summary>
    /// Gets a source file.
    /// </summary>
    /// <param name="kind">The kind of the file.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The file, or <see langword="null"/> when the source has no file of this kind.</returns>
    Task<SourceFile?> FindAsync(string kind, CancellationToken cancellationToken);
}

/// <summary>
/// Gives the source files from the folder <see cref="CardImportOptions.Directory"/>.
/// </summary>
/// <param name="directory">The folder.</param>
internal sealed class LocalCardDataSources(string directory) : ICardDataSources
{
    private static readonly Dictionary<string, string> FileNames = new(StringComparer.Ordinal)
    {
        [SourceKinds.Sets] = "sets.json",
        [SourceKinds.Cards] = "cards.jsonl",
        [SourceKinds.OracleTags] = "oracle_tags.jsonl",
        [SourceKinds.IllustrationTags] = "art_tags.jsonl",
        [SourceKinds.Rulings] = "rulings.jsonl",
        [SourceKinds.Identifiers] = "card_identifiers.csv",
    };

    public Task<SourceFile?> FindAsync(string kind, CancellationToken cancellationToken)
    {
        var name = FileNames[kind];
        foreach (var candidate in new[] { name, name + ".gz" })
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path))
            {
                // The database keeps microseconds, so the time of the file loses its smaller part.
                var modified = File.GetLastWriteTimeUtc(path);
                var updatedAt = new DateTimeOffset(modified.AddTicks(-(modified.Ticks % TimeSpan.TicksPerSecond)), TimeSpan.Zero);
                return Task.FromResult<SourceFile?>(new SourceFile(kind, $"local:{kind}", updatedAt, _ => Task.FromResult(Files.OpenDecompressed(path))));
            }
        }

        return Task.FromResult<SourceFile?>(null);
    }
}

/// <summary>
/// Gives the source files from Scryfall and MTGJSON. Each file goes to a temporary file before the import reads it.
/// </summary>
/// <param name="http">The HTTP client. Its base address is not used.</param>
/// <param name="options">The settings of the import.</param>
internal sealed class RemoteCardDataSources(HttpClient http, IOptions<CardImportOptions> options) : ICardDataSources, IDisposable
{
    private readonly List<string> downloads = [];
    private List<ScryfallBulkFile>? bulkFiles;

    public async Task<SourceFile?> FindAsync(string kind, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        switch (kind)
        {
            case SourceKinds.Sets:
                return new SourceFile(kind, "scryfall:sets", null, token => DownloadAsync(new Uri(settings.ScryfallApi, "sets"), token));

            case SourceKinds.Identifiers:
                if (!settings.IncludeMtgjsonIdentifiers)
                {
                    return null;
                }

                var meta = await http.GetFromJsonAsync<JsonElement>(new Uri(settings.MtgjsonApi, "Meta.json"), cancellationToken);
                var date = meta.GetProperty("data").GetProperty("date").GetString();
                return new SourceFile(
                    kind,
                    "mtgjson:card_identifiers",
                    date is null ? null : DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
                    token => DownloadAsync(new Uri(settings.MtgjsonApi, "csv/cardIdentifiers.csv.gz"), token));

            case SourceKinds.IllustrationTags when !settings.IncludeIllustrationTags:
                return null;

            default:
                var type = kind == SourceKinds.Cards ? settings.BulkType : kind;
                var file = (await BulkFilesAsync(cancellationToken)).FirstOrDefault(bulk => bulk.Type == type);
                var uri = file?.JsonlDownloadUri ?? file?.DownloadUri;
                return file is null || uri is null
                    ? null
                    : new SourceFile(kind, $"scryfall:{type}", file.UpdatedAt, token => DownloadAsync(new Uri(uri), token));
        }
    }

    public void Dispose()
    {
        foreach (var path in downloads)
        {
            File.Delete(path);
        }
    }

    private async Task<List<ScryfallBulkFile>> BulkFilesAsync(CancellationToken cancellationToken)
    {
        if (bulkFiles is null)
        {
            var list = await http.GetFromJsonAsync(new Uri(options.Value.ScryfallApi, "bulk-data"), ScryfallJson.Default.ScryfallListScryfallBulkFile, cancellationToken);
            bulkFiles = list?.Data ?? [];
        }

        return bulkFiles;
    }

    private async Task<Stream> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        var folder = options.Value.DownloadDirectory ?? Path.GetTempPath();
        var path = Path.Combine(folder, $"reliquary-{Guid.NewGuid():N}.download");
        downloads.Add(path);

        using (var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var target = File.Create(path);
            await response.Content.CopyToAsync(target, cancellationToken);
        }

        return Files.OpenDecompressed(path);
    }
}

/// <summary>
/// Opens files that can be compressed with gzip.
/// </summary>
internal static class Files
{
    /// <summary>
    /// Opens a file. When the file starts with the gzip signature, the stream decompresses it.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The content of the file, without compression.</returns>
    public static Stream OpenDecompressed(string path)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        Span<byte> signature = stackalloc byte[2];
        var read = file.Read(signature);
        file.Position = 0;
        return read == 2 && signature[0] == 0x1f && signature[1] == 0x8b ? new GZipStream(file, CompressionMode.Decompress) : file;
    }
}
