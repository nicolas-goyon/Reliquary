using System.Text.Json;
using Dapper;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Versioning;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// An installed extension as the table <c>users.extensions</c> stores it.
/// </summary>
internal sealed class ExtensionRow
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string CoreApi { get; set; } = string.Empty;

    public string[] Permissions { get; set; } = [];

    public string[] Slots { get; set; } = [];

    public string Manifest { get; set; } = "{}";

    public string Settings { get; set; } = "{}";

    public bool Enabled { get; set; }

    public DateTime InstalledAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Tells whether the extension supports the current core API. An update of the core can make an installed extension incompatible.
    /// </summary>
    public bool Compatible => VersionRange.TryParse(CoreApi, out var range) && range!.Contains(BuildingBlocks.Versioning.CoreApi.Version);
}

/// <summary>
/// Reads and writes the extension registry: the installed extensions of the instance and the plugin activations of each user.
/// </summary>
/// <param name="database">The databases of the instance.</param>
/// <param name="time">The clock.</param>
internal sealed class ExtensionRegistry(IDatabase database, TimeProvider time)
{
    private const string Columns = """
        id, kind, name, version, core_api as CoreApi, permissions, slots, manifest::text as Manifest, settings::text as Settings,
        enabled, installed_at as InstalledAt, updated_at as UpdatedAt
        """;

    public async Task<IReadOnlyList<ExtensionRow>> ListAsync(bool enabledOnly, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        var rows = await connection.QueryAsync<ExtensionRow>(new CommandDefinition(
            $"select {Columns} from users.extensions where (not @enabledOnly or enabled) order by kind, id",
            new { enabledOnly },
            cancellationToken: cancellationToken));
        return rows.Where(row => !enabledOnly || row.Compatible).ToList();
    }

    public async Task<ExtensionRow?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ExtensionRow>(new CommandDefinition(
            $"select {Columns} from users.extensions where id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Installs an extension, or updates the manifest of an installed extension. An update keeps the activation flag and the settings.
    /// </summary>
    /// <returns>The extension, and <see langword="true"/> when the extension is new.</returns>
    public async Task<(ExtensionRow Row, bool Created)> InstallAsync(ExtensionManifest manifest, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var manifestJson = JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web);
        var settings = manifest.Settings is { ValueKind: JsonValueKind.Object } value ? value.GetRawText() : "{}";
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        var created = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            insert into users.extensions (id, kind, name, version, core_api, permissions, slots, manifest, settings, enabled, installed_at, updated_at)
            values (@Id, @Kind, @Name, @Version, @CoreApi, @Permissions, @Slots, @manifestJson::jsonb, @settings::jsonb, false, @now, @now)
            on conflict (id) do update
            set kind = excluded.kind, name = excluded.name, version = excluded.version, core_api = excluded.core_api,
                permissions = excluded.permissions, slots = excluded.slots, manifest = excluded.manifest, updated_at = excluded.updated_at
            returning (xmax = 0)
            """,
            new
            {
                manifest.Id,
                manifest.Kind,
                Name = manifest.Name!.Trim(),
                manifest.Version,
                CoreApi = manifest.CoreApi!.Trim(),
                Permissions = (manifest.Permissions ?? []).Distinct().ToArray(),
                Slots = (manifest.Slots ?? []).Distinct().ToArray(),
                manifestJson,
                settings,
                now,
            },
            cancellationToken: cancellationToken));
        var row = await FindAsync(manifest.Id!, cancellationToken) ?? throw new InvalidOperationException($"The extension {manifest.Id} is not in the registry after the installation.");
        return (row, created);
    }

    public async Task<ExtensionRow?> UpdateAsync(string id, bool? enabled, JsonElement? settings, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update users.extensions
            set enabled = coalesce(@enabled, enabled), settings = coalesce(@settings::jsonb, settings), updated_at = @now
            where id = @id
            """,
            new { id, enabled, settings = settings?.GetRawText(), now = time.GetUtcNow() },
            cancellationToken: cancellationToken));
        return await FindAsync(id, cancellationToken);
    }

    public async Task<bool> UninstallAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "delete from users.extensions where id = @id",
            new { id },
            cancellationToken: cancellationToken)) == 1;
    }

    /// <summary>
    /// Gets the plugin activations of a user.
    /// </summary>
    /// <returns>The activation flag of each plugin that the user changed.</returns>
    public async Task<IReadOnlyDictionary<string, bool>> PluginActivationsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Read, cancellationToken);
        var rows = await connection.QueryAsync<(string ExtensionId, bool Enabled)>(new CommandDefinition(
            "select extension_id, enabled from content.plugin_activations where owner_id = @ownerId",
            new { ownerId },
            cancellationToken: cancellationToken));
        return rows.ToDictionary(row => row.ExtensionId, row => row.Enabled, StringComparer.Ordinal);
    }

    public async Task SetPluginActivationAsync(Guid ownerId, string extensionId, bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Write, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.plugin_activations (owner_id, extension_id, enabled, updated_at)
            values (@ownerId, @extensionId, @enabled, @now)
            on conflict (owner_id, extension_id) do update set enabled = excluded.enabled, updated_at = excluded.updated_at
            """,
            new { ownerId, extensionId, enabled, now = time.GetUtcNow() },
            cancellationToken: cancellationToken));
    }
}
