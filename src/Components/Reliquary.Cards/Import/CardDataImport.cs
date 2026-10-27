using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Jobs;

namespace Reliquary.Cards.Import;

/// <summary>
/// The result of the import of one source file.
/// </summary>
/// <param name="Source">The name of the source, for example <c>scryfall:all_cards</c>.</param>
/// <param name="Status">The status: <c>succeeded</c>, <c>failed</c>, <c>unchanged</c> or <c>missing</c>.</param>
/// <param name="ItemCount">The number of items that the import read or changed.</param>
public sealed record SourceImportResult(string Source, string Status, int ItemCount)
{
    /// <summary>Gets the kind of the source, for example <c>cards</c> or <c>combos</c>.</summary>
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// Imports all the card data: sets, cards, Oracle tags, illustration tags, rulings, MTGJSON identifiers and the combos of Commander Spellbook.
/// </summary>
/// <remarks>
/// The import skips a source file when its date did not change since the last successful import, except for a forced run.
/// The import of the combos also runs when the import of the cards changed the cards in the same run, because the combos keep only the known cards.
/// The table <c>reference.import_runs</c> records each import.
/// </remarks>
internal sealed class CardDataImport(
    IHttpClientFactory httpClients,
    IOptions<CardImportOptions> options,
    CardFileImporter cards,
    ReferenceImporters references,
    ComboImporter combos,
    IDatabase database,
    TimeProvider time,
    ILogger<CardDataImport> logger)
{
    /// <summary>The name of the HTTP client of the import.</summary>
    public const string HttpClientName = "Reliquary.Cards.Import";

    /// <summary>
    /// Imports each source file that changed.
    /// </summary>
    /// <param name="force">When <see langword="true"/>, the import reads all the files, also the files that did not change.</param>
    /// <param name="cancellationToken">A token that stops the import.</param>
    /// <returns>The result of each source.</returns>
    /// <exception cref="InvalidOperationException">The import of one or more sources failed.</exception>
    public async Task<IReadOnlyList<SourceImportResult>> RunAsync(bool force, CancellationToken cancellationToken)
    {
        using var sources = CreateSources();
        var results = new List<SourceImportResult>();

        foreach (var kind in SourceKinds.InOrder)
        {
            var file = await sources.Value.FindAsync(kind, cancellationToken);
            if (file is null)
            {
                results.Add(new SourceImportResult(kind, "missing", 0) { Kind = kind });
                continue;
            }

            var cardsChanged = kind == SourceKinds.Combos && results.Any(result => result.Kind == SourceKinds.Cards && result.Status == "succeeded");
            if (!force && !cardsChanged && file.UpdatedAt is { } updatedAt && await LastSuccessAsync(file.Name, cancellationToken) == updatedAt)
            {
                logger.SourceUnchanged(file.Name);
                results.Add(new SourceImportResult(file.Name, "unchanged", 0) { Kind = kind });
                continue;
            }

            results.Add(await ImportFileAsync(file, cancellationToken) with { Kind = kind });
        }

        var failed = results.Where(result => result.Status == "failed").Select(result => result.Source).ToList();
        if (failed.Count > 0)
        {
            throw new InvalidOperationException($"The import of these sources failed: {string.Join(", ", failed)}. The table reference.import_runs gives the errors.");
        }

        return results;
    }

    private async Task<SourceImportResult> ImportFileAsync(SourceFile file, CancellationToken cancellationToken)
    {
        var runId = Ids.New();
        await using (var connection = await database.OpenReferenceAsync(cancellationToken))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "insert into reference.import_runs (id, source, source_updated_at, started_at, status) values (@runId, @source, @updatedAt, @now, 'running')",
                new { runId, source = file.Name, updatedAt = file.UpdatedAt, now = time.GetUtcNow() },
                cancellationToken: cancellationToken));
        }

        logger.SourceImportStarted(file.Name);
        try
        {
            await using var content = await file.Open(cancellationToken);
            var count = file.Kind switch
            {
                SourceKinds.Sets => await references.ImportSetsAsync(content, cancellationToken),
                SourceKinds.Cards => (await cards.ImportAsync(file.Name, content, cancellationToken)).PrintingCount,
                SourceKinds.OracleTags => await references.ImportTagsAsync("oracle", content, cancellationToken),
                SourceKinds.IllustrationTags => await references.ImportTagsAsync("illustration", content, cancellationToken),
                SourceKinds.Rulings => await references.ImportRulingsAsync(content, cancellationToken),
                SourceKinds.Identifiers => await references.ImportIdentifiersAsync(content, cancellationToken),
                SourceKinds.Combos => (await combos.ImportAsync(content, cancellationToken)).Kept,
                _ => throw new InvalidOperationException($"The source kind \"{file.Kind}\" is not known."),
            };

            await RecordEndAsync(runId, "succeeded", count, null);
            logger.SourceImportCompleted(file.Name, count);
            return new SourceImportResult(file.Name, "succeeded", count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.SourceImportFailed(exception, file.Name);
            await RecordEndAsync(runId, "failed", null, exception.Message);
            return new SourceImportResult(file.Name, "failed", 0);
        }
    }

    private Disposable<ICardDataSources> CreateSources()
    {
        if (options.Value.Directory is { Length: > 0 } directory)
        {
            return new Disposable<ICardDataSources>(new LocalCardDataSources(directory), null);
        }

        var remote = new RemoteCardDataSources(httpClients.CreateClient(HttpClientName), options);
        return new Disposable<ICardDataSources>(remote, remote);
    }

    private async Task<DateTimeOffset?> LastSuccessAsync(string source, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        var value = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            """
            select source_updated_at from reference.import_runs
            where source = @source and status = 'succeeded'
            order by started_at desc limit 1
            """,
            new { source },
            cancellationToken: cancellationToken));
        return value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }

    private async Task RecordEndAsync(Guid runId, string status, int? count, string? error)
    {
        await using var connection = await database.OpenReferenceAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            "update reference.import_runs set finished_at = @now, status = @status, item_count = @count, error = @error where id = @runId",
            new { runId, status, count, error, now = time.GetUtcNow() });
    }

    private sealed class Disposable<T>(T value, IDisposable? owner) : IDisposable
    {
        public T Value { get; } = value;

        public void Dispose() => owner?.Dispose();
    }
}

/// <summary>
/// The scheduled job of the card data import: <c>cards.import</c>.
/// </summary>
/// <param name="import">The import.</param>
/// <param name="options">The settings of the import.</param>
internal sealed class CardImportJob(CardDataImport import, IOptions<CardImportOptions> options) : IScheduledJob
{
    public string Name => "cards.import";

    public TimeSpan Interval => options.Value.Interval;

    public Task RunAsync(JobRun run, CancellationToken cancellationToken) => import.RunAsync(run.Force, cancellationToken);
}
