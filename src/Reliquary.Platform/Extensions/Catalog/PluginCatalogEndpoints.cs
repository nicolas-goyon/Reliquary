using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Security;
using Reliquary.BuildingBlocks.Versioning;

namespace Reliquary.Platform.Extensions.Catalog;

/// <summary>The plugin catalog of the instance, for the administrators.</summary>
/// <param name="Configured">Whether the instance has the address of a catalog index (<c>Plugins:Catalog:Url</c>).</param>
/// <param name="Serial">The serial of the last index that the instance accepted, or null before the first sync.</param>
/// <param name="ExpiresAt">The time when this index expires, or null.</param>
/// <param name="CheckedAt">The time of the last sync that got this index, or null.</param>
/// <param name="Plugins">The plugins whose files the instance has, by ID.</param>
public sealed record PluginCatalogResponse(bool Configured, long? Serial, DateTimeOffset? ExpiresAt, DateTimeOffset? CheckedAt, IReadOnlyList<CatalogPluginResponse> Plugins);

/// <summary>A plugin of the catalog.</summary>
/// <param name="Id">The ID of the plugin.</param>
/// <param name="Name">The name in English, from the manifest of the last version.</param>
/// <param name="Description">The description in English, from the manifest of the last version, or null.</param>
/// <param name="LatestVersion">The last version that the catalog did not revoke, or null when the catalog revoked all the versions.</param>
/// <param name="Versions">The versions whose files the instance has, with the last version first.</param>
public sealed record CatalogPluginResponse(string Id, string Name, string? Description, string? LatestVersion, IReadOnlyList<CatalogVersionResponse> Versions);

/// <summary>A version of a plugin of the catalog.</summary>
/// <param name="Version">The version.</param>
/// <param name="RevocationReason">The reason why the catalog revoked the version, or null.</param>
public sealed record CatalogVersionResponse(string Version, string? RevocationReason);

/// <summary>The result of a sync that an administrator started.</summary>
/// <param name="Serial">The serial of the index.</param>
/// <param name="AddedVersions">The plugin versions whose files the sync added.</param>
/// <param name="RevokedVersions">The plugin versions that the index revoked for the first time.</param>
public sealed record CatalogSyncResponse(long Serial, IReadOnlyList<string> AddedVersions, IReadOnlyList<string> RevokedVersions);

/// <summary>
/// The endpoints of the plugin catalog for the administrators: the list, a sync, and the installation of a version. See issue #300.
/// </summary>
public static class PluginCatalogEndpoints
{
    /// <summary>The instance has no address of a catalog index.</summary>
    public const string CatalogNotConfigured = "catalog_not_configured";

    /// <summary>The sync of the catalog failed.</summary>
    public const string CatalogSyncFailed = "catalog_sync_failed";

    /// <summary>Maps the endpoints of the plugin catalog.</summary>
    /// <param name="api">The route group of the public API.</param>
    public static void MapPluginCatalogEndpoints(this IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin/plugin-catalog").WithTags("Administration").RequireAuthorization(AuthPolicies.Administrator);

        admin.MapGet(string.Empty, ListAsync)
            .WithName("ListPluginCatalog")
            .WithSummary("Lists the plugins of the catalog whose files the instance has, with the state of the last catalog index.");

        admin.MapPost("/sync", SyncAsync)
            .WithName("SyncPluginCatalog")
            .WithSummary("Gets the catalog index now, checks its signature, and adds the files of the new plugin versions.");

        admin.MapPost("/{id}/{version}/install", InstallAsync)
            .WithName("InstallCatalogPlugin")
            .WithSummary("Installs a plugin version from the files of the catalog, or updates the installed plugin to this version. A new plugin is disabled.");
    }

    private static async Task<Ok<PluginCatalogResponse>> ListAsync(IDatabase database, IOptions<PluginCatalogOptions> options, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        var state = await connection.QuerySingleOrDefaultAsync<(long Serial, DateTime ExpiresAt, DateTime CheckedAt)?>(new CommandDefinition(
            "select serial, expires_at, checked_at from users.plugin_catalog", cancellationToken: cancellationToken));
        var rows = await connection.QueryAsync<(string Id, string Version, byte[] Manifest, string? Reason)>(new CommandDefinition(
            """
            select f.extension_id, f.version, f.content, r.reason
            from users.extension_files f
            left join users.extension_revocations r on r.extension_id = f.extension_id and r.version = f.version
            where f.path = @path
            """,
            new { path = CatalogIndex.ManifestPath },
            cancellationToken: cancellationToken));

        var plugins = new List<CatalogPluginResponse>();
        foreach (var group in rows.GroupBy(row => row.Id, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var versions = group
                .OrderByDescending(row => SemanticVersion.TryParse(row.Version, out var version) ? version : new SemanticVersion(0, 0, 0))
                .ToList();
            var latest = versions.FirstOrDefault(row => row.Reason is null);
            var (name, description) = ReadTexts((latest.Id is null ? versions[0] : latest).Manifest, group.Key);
            plugins.Add(new CatalogPluginResponse(
                group.Key, name, description, latest.Id is null ? null : latest.Version, versions.Select(row => new CatalogVersionResponse(row.Version, row.Reason)).ToList()));
        }

        return TypedResults.Ok(new PluginCatalogResponse(
            !string.IsNullOrWhiteSpace(options.Value.Url),
            state?.Serial,
            state is { } value ? Utc(value.ExpiresAt) : null,
            state is { } last ? Utc(last.CheckedAt) : null,
            plugins));
    }

    private static async Task<Results<Ok<CatalogSyncResponse>, ProblemHttpResult>> SyncAsync(
        PluginCatalogSync sync, IOptions<PluginCatalogOptions> options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Url))
        {
            return ApiProblems.Conflict(CatalogNotConfigured, "The instance has no plugin catalog. Set Plugins:Catalog:Url to the address of a catalog index.");
        }

        try
        {
            var result = await sync.SyncAsync(cancellationToken);
            return TypedResults.Ok(new CatalogSyncResponse(
                result.Serial, result.AddedVersions, result.NewRevocations.Select(revocation => $"{revocation.Id} {revocation.Version}").ToList()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The sync wrote the reason to the log. The message of the sync has no secret: it names the index and the plugin versions.
            return ApiProblems.Problem(StatusCodes.Status502BadGateway, CatalogSyncFailed, $"The sync of the plugin catalog failed. {exception.Message}");
        }
    }

    private static async Task<Results<Created<AdminExtensionResponse>, Ok<AdminExtensionResponse>, ProblemHttpResult>> InstallAsync(
        string id, string version, PluginFileStore files, ExtensionRegistry registry, CancellationToken cancellationToken)
    {
        if (await files.FindAsync(id, version, CatalogIndex.ManifestPath, cancellationToken) is not { } manifest)
        {
            return ApiProblems.NotFound("The instance does not have the files of this plugin version. Sync the plugin catalog, then try again.");
        }

        JsonElement body;
        try
        {
            body = JsonElement.Parse(manifest.Content);
        }
        catch (JsonException)
        {
            return ApiProblems.Problem(StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "The manifest of this plugin version is not a JSON document.");
        }

        return await ExtensionEndpoints.InstallAsync(id, body, registry, cancellationToken);
    }

    /// <summary>Reads the name and the description of a plugin from its manifest. A manifest that is not correct gives the ID as the name.</summary>
    private static (string Name, string? Description) ReadTexts(byte[] manifest, string id)
    {
        try
        {
            var root = JsonElement.Parse(manifest);
            if (root.ValueKind == JsonValueKind.Object)
            {
                var name = root.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                var description = root.TryGetProperty("description", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
                return (string.IsNullOrWhiteSpace(name) ? id : name, description);
            }
        }
        catch (JsonException)
        {
            // The list still shows the plugin, with its ID.
        }

        return (id, null);
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
