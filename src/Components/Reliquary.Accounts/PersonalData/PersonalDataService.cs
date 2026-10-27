using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Reliquary.Accounts.Data;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.BuildingBlocks.Jobs;

namespace Reliquary.Accounts.PersonalData;

/// <summary>
/// The export and the deletion of the personal data of an account. See docs/decisions/0032-personal-data.md.
/// </summary>
internal sealed class PersonalDataService(
    AccountStore store,
    IEnumerable<IPersonalDataProvider> providers,
    IShardDirectory shards,
    IDomainEventWriter events,
    TimeProvider time,
    ILogger<PersonalDataService> logger)
{
    /// <summary>The delay between the deletion request and the deletion.</summary>
    public static readonly TimeSpan DeletionDelay = TimeSpan.FromDays(7);

    /// <summary>The time that the archive of an export stays available.</summary>
    public static readonly TimeSpan ExportLifetime = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Asks for an export. When an export of the account is pending, the method gives it again.
    /// </summary>
    public async Task<DataExportRow> RequestExportAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        var pending = (await PersonalDataStore.ListExportsAsync(connection, accountId, cancellationToken)).FirstOrDefault(row => row.Status == "pending");
        if (pending is not null)
        {
            return pending;
        }

        var row = new DataExportRow { Id = Ids.New(), AccountId = accountId, Status = "pending", RequestedAt = time.GetUtcNow().UtcDateTime };
        await PersonalDataStore.InsertExportAsync(connection, row, cancellationToken);
        return row;
    }

    public async Task<IReadOnlyList<DataExportRow>> ListExportsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        return await PersonalDataStore.ListExportsAsync(connection, accountId, cancellationToken);
    }

    public async Task<byte[]?> GetArchiveAsync(Guid accountId, Guid exportId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        return await PersonalDataStore.GetArchiveAsync(connection, accountId, exportId, time.GetUtcNow().UtcDateTime, cancellationToken);
    }

    /// <summary>
    /// Makes the archives of the pending exports, then deletes the expired archives.
    /// </summary>
    /// <returns>The number of exports that the method processed.</returns>
    public async Task<int> ProcessExportsAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        while (true)
        {
            await using var connection = await store.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var export = await PersonalDataStore.TakePendingExportAsync(transaction, cancellationToken);
            if (export is null)
            {
                break;
            }

            byte[]? archive = null;
            try
            {
                archive = await BuildArchiveAsync(export.AccountId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.ExportFailed(exception, export.Id, export.AccountId);
            }

            var now = time.GetUtcNow().UtcDateTime;
            await PersonalDataStore.CompleteExportAsync(transaction, export.Id, archive, now, now + ExportLifetime, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            count++;
        }

        await using (var cleanup = await store.OpenAsync(cancellationToken))
        {
            await PersonalDataStore.DeleteExpiredExportsAsync(cleanup, time.GetUtcNow().UtcDateTime, cancellationToken);
        }

        return count;
    }

    /// <summary>
    /// Makes the archive of all the data of an account: one folder for each core component.
    /// </summary>
    public async Task<byte[]> BuildArchiveAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(zip, "README.txt", Readme, cancellationToken);
            foreach (var provider in providers.OrderBy(provider => provider.Name, StringComparer.Ordinal))
            {
                await provider.ExportAsync(accountId, new Archive(zip, provider.Name), cancellationToken);
            }
        }

        return stream.ToArray();
    }

    public async Task<DateTime?> GetDeletionRequestAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        return await PersonalDataStore.GetDeletionRequestAsync(connection, accountId, cancellationToken);
    }

    /// <summary>
    /// Asks for the deletion of an account, or cancels the request.
    /// </summary>
    /// <returns>The time of the request, or <see langword="null"/> after a cancellation.</returns>
    public async Task<DateTime?> SetDeletionRequestAsync(Guid accountId, bool delete, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        var current = await PersonalDataStore.GetDeletionRequestAsync(connection, accountId, cancellationToken);
        var requestedAt = delete ? current ?? time.GetUtcNow().UtcDateTime : (DateTime?)null;
        await PersonalDataStore.SetDeletionRequestAsync(connection, accountId, requestedAt, cancellationToken);
        return requestedAt;
    }

    /// <summary>
    /// Deletes the accounts with a deletion request older than the delay.
    /// </summary>
    /// <returns>The number of deleted accounts.</returns>
    public async Task<int> ProcessDeletionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> due;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            due = await PersonalDataStore.ListDueDeletionsAsync(connection, (time.GetUtcNow() - DeletionDelay).UtcDateTime, cancellationToken);
        }

        foreach (var accountId in due)
        {
            await DeleteAccountAsync(accountId, cancellationToken);
        }

        return due.Count;
    }

    /// <summary>
    /// Deletes an account now: first the data of each core component, then the account and its shard assignment.
    /// A failure stops the deletion, and the next run of the job starts it again.
    /// </summary>
    public async Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        foreach (var provider in providers.OrderBy(provider => provider.DeletionOrder).ThenBy(provider => provider.Name, StringComparer.Ordinal))
        {
            await provider.DeleteAsync(accountId, cancellationToken);
        }

        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PersonalDataStore.DeleteAccountAsync(transaction, accountId, cancellationToken);
        await shards.RemoveAsync(accountId, transaction, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Users, transaction, new AccountDeleted(accountId), null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.AccountDeleted(accountId);
    }

    private const string Readme = """
        Reliquary: export of the data of an account.

        Each folder contains the data of one part of Reliquary, in JSON and text files.
        The decks folder contains each deck in the Reliquary text format and in JSON.
        You can import a deck text file into Reliquary or into other deck tools.
        """;

    private static async Task WriteEntryAsync(ZipArchive zip, string path, string text, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var entryStream = await entry.OpenAsync(cancellationToken);
        await entryStream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    private sealed class Archive(ZipArchive zip, string folder) : IPersonalDataArchive
    {
        public Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken) =>
            WriteEntryAsync(zip, $"{folder}/{path}", JsonSerializer.Serialize(value, value.GetType(), Json), cancellationToken);

        public Task WriteTextAsync(string path, string text, CancellationToken cancellationToken) =>
            WriteEntryAsync(zip, $"{folder}/{path}", text, cancellationToken);
    }
}

/// <summary>
/// Makes the pending exports and deletes the accounts after the delay.
/// </summary>
internal sealed class PersonalDataJob(PersonalDataService service) : IScheduledJob
{
    public string Name => "accounts.personal_data";

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task RunAsync(JobRun run, CancellationToken cancellationToken)
    {
        await service.ProcessExportsAsync(cancellationToken);
        await service.ProcessDeletionsAsync(cancellationToken);
    }
}

/// <summary>
/// Gives the data of the Accounts component for the export. The deletion of the account row is the last step of the deletion.
/// </summary>
internal sealed class AccountPersonalData(AccountStore store) : IPersonalDataProvider
{
    public string Name => "account";

    public int DeletionOrder => int.MaxValue;

    public async Task ExportAsync(Guid ownerId, IPersonalDataArchive archive, CancellationToken cancellationToken)
    {
        var account = await store.FindByIdAsync(ownerId, cancellationToken)
            ?? throw new InvalidOperationException($"The account {ownerId} does not exist.");
        var providers = await store.ListExternalProvidersAsync(ownerId, cancellationToken);
        var keys = await store.ListApiKeysAsync(ownerId, cancellationToken);
        await archive.WriteJsonAsync("account.json", new
        {
            account.Id,
            account.Email,
            EmailChecked = account.EmailCheckedAt is not null,
            account.DisplayName,
            account.Language,
            account.IsAdministrator,
            account.CreatedAt,
            account.LastSignInAt,
            ExternalLogins = providers,
            ApiKeys = keys.Select(key => new { key.Name, key.Prefix, key.CreatedAt, key.LastUsedAt }),
        }, cancellationToken);
    }

    // The service deletes the account row in the last step, with the shard assignment.
    public Task DeleteAsync(Guid ownerId, CancellationToken cancellationToken) => Task.CompletedTask;
}
