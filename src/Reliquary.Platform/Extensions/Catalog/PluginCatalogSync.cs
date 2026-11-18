using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Jobs;

namespace Reliquary.Platform.Extensions.Catalog;

/// <summary>The result of one catalog sync.</summary>
/// <param name="Status">The status of the index: <see cref="CatalogIndexStatus.Valid"/> or <see cref="CatalogIndexStatus.Unchanged"/>.</param>
/// <param name="Serial">The serial of the index.</param>
/// <param name="AddedVersions">The plugin versions whose files the sync added to the store.</param>
/// <param name="FailedVersions">The plugin versions that the sync did not add. The next sync tries them again.</param>
/// <param name="NewRevocations">The revocations that the instance did not have before this sync.</param>
public sealed record CatalogSyncResult(
    CatalogIndexStatus Status,
    long Serial,
    IReadOnlyList<string> AddedVersions,
    IReadOnlyList<string> FailedVersions,
    IReadOnlyList<CatalogRevocation> NewRevocations);

/// <summary>
/// Gets the catalog index and the files of the plugin versions. See docs/decisions/0052-plugin-catalog-signature-and-revocation.md.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The sync gets <c>index.json</c> and <c>index.json.sig</c>, and checks them with <see cref="CatalogIndexCheck"/>.
/// It keeps the last index that it accepted in <c>users.plugin_catalog</c>. A refused index does not change this row.</item>
/// <item>In the same transaction, it adds the revocations of the index to <c>users.extension_revocations</c>, and disables each installed plugin whose version is revoked.
/// The registry, the plugin token and the frame endpoint then refuse the revoked version.</item>
/// <item>For each version that is not revoked and not in the store, it gets the files from <c>{folder of the index}/{plugin ID}/{version}/{path}</c>.
/// It checks the size and the SHA-384 hash of each file, and the plugin manifest of the version. Then it keeps the files with <see cref="PluginFileStore"/>.</item>
/// </list>
/// </remarks>
/// <param name="httpClients">The factory of the HTTP clients.</param>
/// <param name="options">The settings of the catalog.</param>
/// <param name="database">The databases of the instance.</param>
/// <param name="files">The store of the plugin files.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class PluginCatalogSync(
    IHttpClientFactory httpClients,
    IOptions<PluginCatalogOptions> options,
    IDatabase database,
    PluginFileStore files,
    TimeProvider time,
    ILogger<PluginCatalogSync> logger)
{
    /// <summary>The name of the HTTP client of the sync.</summary>
    public const string HttpClientName = "Reliquary.Plugins.Catalog";

    /// <summary>
    /// Syncs the catalog one time.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    /// <exception cref="InvalidOperationException">The settings are not complete, the instance refused the index, or a plugin version failed.</exception>
    public async Task<CatalogSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        var indexUrl = IndexUrl(options.Value.Url);
        using var keys = CatalogKeyRing.Create(options.Value.TrustedKeys);
        if (keys.KeyIds.Count == 0)
        {
            throw new InvalidOperationException("The instance trusts no catalog key. Set Plugins:Catalog:Keys.");
        }

        using var client = httpClients.CreateClient(HttpClientName);
        var index = await DownloadAsync(client, indexUrl, CatalogIndexCheck.MaxIndexBytes, cancellationToken);
        var signature = await DownloadAsync(client, new Uri(indexUrl + ".sig"), CatalogIndexCheck.MaxSignatureBytes, cancellationToken);

        var now = time.GetUtcNow();
        var last = await LastStateAsync(cancellationToken);
        var result = CatalogIndexCheck.Check(index, signature, keys, last, now);
        if (!result.Accepted)
        {
            logger.CatalogIndexRefused(result.Status.ToString(), result.Message);
            throw new InvalidOperationException($"The instance refused the catalog index ({result.Status}). {result.Message}");
        }

        var accepted = result.Index!;
        var revoked = await SaveStateAsync(accepted, index, signature, now, cancellationToken);
        if (result.Status == CatalogIndexStatus.Valid)
        {
            logger.CatalogIndexAccepted(accepted.Serial, accepted.KeyId);
        }

        foreach (var revocation in revoked)
        {
            logger.CatalogVersionRevoked(revocation.Id, revocation.Version, revocation.Reason);
        }

        var revokedVersions = await RevokedVersionsAsync(cancellationToken);
        var added = new List<string>();
        var failed = new List<string>();
        foreach (var plugin in accepted.Plugins)
        {
            foreach (var version in plugin.Versions)
            {
                if (revokedVersions.Contains((plugin.Id, version.Version)) || await files.HasVersionAsync(plugin.Id, version.Version, cancellationToken))
                {
                    continue;
                }

                var name = $"{plugin.Id} {version.Version}";
                try
                {
                    var versionFiles = await DownloadVersionAsync(client, indexUrl, plugin.Id, version, cancellationToken);
                    await files.SaveVersionAsync(plugin.Id, version.Version, versionFiles, cancellationToken);
                    logger.CatalogVersionAdded(plugin.Id, version.Version);
                    added.Add(name);
                }
                catch (Exception exception) when (exception is HttpRequestException or CatalogFileException or ArgumentException
                    || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    logger.CatalogVersionFailed(plugin.Id, version.Version, exception.Message);
                    failed.Add(name);
                }
            }
        }

        if (failed.Count > 0)
        {
            throw new InvalidOperationException($"The catalog sync accepted the index {accepted.Serial}, but did not add these plugin versions: {string.Join(", ", failed)}.");
        }

        return new CatalogSyncResult(result.Status, accepted.Serial, added, failed, revoked);
    }

    /// <summary>
    /// Reads the address of the catalog index from the settings.
    /// </summary>
    /// <exception cref="InvalidOperationException">The address is missing or is not HTTPS.</exception>
    private static Uri IndexUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The instance has no valid catalog address. Set Plugins:Catalog:Url.");
        }

        // The signature protects the content. HTTPS also protects the files of a version that the index does not list yet.
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
        {
            throw new InvalidOperationException("The catalog address must use HTTPS. Only a loopback address can use HTTP.");
        }

        return uri;
    }

    private static async Task<List<PluginFile>> DownloadVersionAsync(HttpClient client, Uri indexUrl, string pluginId, CatalogVersion version, CancellationToken cancellationToken)
    {
        var result = new List<PluginFile>();
        foreach (var file in version.Files)
        {
            // The check of the index accepts only plugin IDs, versions and paths without special characters, so the address stays in the folder of the index.
            var url = new Uri(indexUrl, $"{pluginId}/{version.Version}/{file.Path}");
            var content = await DownloadAsync(client, url, (int)file.Size, cancellationToken);
            if (content.Length != file.Size || PluginFileStore.Integrity(content) != file.Integrity)
            {
                throw new CatalogFileException($"The file {file.Path} does not have the size or the SHA-384 hash of the catalog index.");
            }

            result.Add(new PluginFile(file.Path, content));
        }

        CheckManifest(pluginId, version.Version, result);
        return result;
    }

    /// <summary>Checks the plugin manifest of a version: the rules of the API, the ID, the version and the entry file.</summary>
    private static void CheckManifest(string pluginId, string version, List<PluginFile> versionFiles)
    {
        var manifestFile = versionFiles.Single(file => file.Path == CatalogIndex.ManifestPath);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifestFile.Content);
        }
        catch (JsonException exception)
        {
            throw new CatalogFileException($"The plugin manifest is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (PluginManifestCheck.Check(root) is { Count: > 0 } errors)
            {
                throw new CatalogFileException($"The plugin manifest is not valid: {string.Join(" ", errors.Select(error => $"{error.Field}: {error.Message}"))}");
            }

            if (root.GetProperty("id").GetString() != pluginId || root.GetProperty("version").GetString() != version)
            {
                throw new CatalogFileException("The ID or the version of the plugin manifest is different from the catalog index.");
            }

            var entry = root.GetProperty("entry").GetString();
            if (!versionFiles.Any(file => file.Path == entry))
            {
                throw new CatalogFileException($"The version has no entry file {entry}.");
            }
        }
    }

    /// <summary>Gets the content of an address, with a size limit.</summary>
    private static async Task<byte[]> DownloadAsync(HttpClient client, Uri url, int maxBytes, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The catalog sent the status {(int)response.StatusCode} for {url}.", null, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new CatalogFileException($"The content of {url} has more than {maxBytes} bytes.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > maxBytes)
            {
                throw new CatalogFileException($"The content of {url} has more than {maxBytes} bytes.");
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private async Task<CatalogIndexState?> LastStateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<CatalogIndexState>(new CommandDefinition(
            "select serial, index_sha256 as sha256 from users.plugin_catalog",
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Keeps the accepted index and applies its revocations in one transaction.
    /// </summary>
    /// <returns>The revocations that the instance did not have before.</returns>
    private async Task<IReadOnlyList<CatalogRevocation>> SaveStateAsync(CatalogIndex index, byte[] content, byte[] signature, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The condition on the serial keeps the larger serial if two syncs run at the same time.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.plugin_catalog (serial, expires_at, key_id, index_sha256, index_content, signature, accepted_at, checked_at)
            values (@Serial, @Expires, @KeyId, @sha256, @content, @signature, @now, @now)
            on conflict (singleton) do update
            set serial = excluded.serial, expires_at = excluded.expires_at, key_id = excluded.key_id, index_sha256 = excluded.index_sha256,
                index_content = excluded.index_content, signature = excluded.signature, checked_at = excluded.checked_at,
                accepted_at = case when users.plugin_catalog.serial = excluded.serial then users.plugin_catalog.accepted_at else excluded.accepted_at end
            where users.plugin_catalog.serial <= excluded.serial
            """,
            new { index.Serial, Expires = index.Expires.ToUniversalTime(), index.KeyId, sha256 = SHA256.HashData(content), content, signature, now },
            transaction,
            cancellationToken: cancellationToken));

        // A revocation is permanent. A later index without the revocation does not enable the version again.
        var added = new List<CatalogRevocation>();
        foreach (var revocation in index.Revoked)
        {
            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into users.extension_revocations (extension_id, version, reason, revoked_at)
                values (@Id, @Version, @Reason, @now)
                on conflict (extension_id, version) do nothing
                """,
                new { revocation.Id, revocation.Version, revocation.Reason, now },
                transaction,
                cancellationToken: cancellationToken));
            if (inserted == 1)
            {
                added.Add(revocation);
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            update users.extensions e
            set enabled = false, updated_at = @now
            from users.extension_revocations r
            where r.extension_id = e.id and r.version = e.version and e.enabled
            """,
            new { now },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return added;
    }

    /// <summary>Gets all the revoked versions of the instance. The list contains the revocations of the earlier indexes too.</summary>
    private async Task<HashSet<(string Id, string Version)>> RevokedVersionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        var rows = await connection.QueryAsync<(string Id, string Version)>(new CommandDefinition(
            "select extension_id, version from users.extension_revocations",
            cancellationToken: cancellationToken));
        return rows.ToHashSet();
    }

    /// <summary>A file of the catalog is not correct.</summary>
    private sealed class CatalogFileException(string message) : Exception(message);
}

/// <summary>
/// The scheduled job of the catalog sync: <c>plugins.catalog</c>. The worker has it only when the setting <c>Plugins:Catalog:Url</c> has a value.
/// </summary>
/// <param name="sync">The catalog sync.</param>
/// <param name="options">The settings of the catalog.</param>
internal sealed class PluginCatalogJob(PluginCatalogSync sync, IOptions<PluginCatalogOptions> options) : IScheduledJob
{
    public string Name => "plugins.catalog";

    public TimeSpan Interval => options.Value.Interval;

    public Task RunAsync(JobRun run, CancellationToken cancellationToken) => sync.SyncAsync(cancellationToken);
}
