using System.Text.Json;
using Dapper;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Platform.Extensions;

/// <summary>The settings of a user for a plugin. See issue #278.</summary>
/// <param name="Position">The position of the contributions of the plugin among the contributions of the other plugins. A smaller number comes first. The default is 0.</param>
/// <param name="Hidden">The contributions that the user hides, as <c>slot:id</c>, for example <c>deck.actions:sync</c>.</param>
/// <param name="DisabledByCoreAt">The time when the core deactivated the plugin because it failed, or null.</param>
public sealed record PluginSettings(int Position, IReadOnlyList<string> Hidden, DateTimeOffset? DisabledByCoreAt)
{
    /// <summary>The settings of a plugin that the user did not change.</summary>
    public static PluginSettings Default { get; } = new(0, [], null);

    /// <summary>Reads the settings from the JSON text of the column <c>settings</c>.</summary>
    /// <param name="json">The JSON object.</param>
    /// <returns>The settings. A field that is missing or not correct has its default value.</returns>
    public static PluginSettings Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Default;
        }

        var root = JsonElement.Parse(json);
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Default;
        }

        var position = root.TryGetProperty("position", out var value) && value.TryGetInt32(out var number) ? number : 0;
        var hidden = root.TryGetProperty("hidden", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : [];
        DateTimeOffset? disabledAt = root.TryGetProperty("disabledByCoreAt", out var at) && at.TryGetDateTimeOffset(out var time) ? time : null;
        return new PluginSettings(position, hidden, disabledAt);
    }
}

/// <summary>
/// The activation of a plugin for a user, and the consent of the user.
/// </summary>
/// <param name="ExtensionId">The ID of the plugin.</param>
/// <param name="Enabled">Whether the user activated the plugin.</param>
/// <param name="AcceptedVersion">The plugin version of the last consent, or null when the user did not accept access.</param>
/// <param name="Accepted">The access that the user accepted. It is empty without a consent.</param>
/// <param name="AcceptedAt">The time of the last consent, or null.</param>
/// <param name="Settings">The settings of the user for the plugin.</param>
public sealed record PluginActivation(string ExtensionId, bool Enabled, string? AcceptedVersion, PluginGrant Accepted, DateTimeOffset? AcceptedAt, PluginSettings Settings)
{
    /// <summary>
    /// Tells whether a plugin version stays paused for the user: the user activated the plugin, but the version asks for access that the user did not accept.
    /// </summary>
    /// <param name="asked">The access that the installed version asks for.</param>
    /// <returns><see langword="true"/> when the plugin must not run until the user accepts the difference.</returns>
    public bool IsPaused(PluginGrant asked) => Enabled && !Accepted.Covers(asked);

    /// <summary>Tells whether the plugin runs for the user, with the access that the installed version asks for.</summary>
    /// <param name="asked">The access that the installed version asks for.</param>
    /// <returns><see langword="true"/> when the user activated the plugin and accepted all its access.</returns>
    public bool Runs(PluginGrant asked) => Enabled && Accepted.Covers(asked);
}

/// <summary>
/// Reads and writes the table <c>content.plugin_activations</c>: the activation and the consent of each plugin for each user.
/// </summary>
/// <param name="database">The databases of the instance.</param>
/// <param name="time">The clock.</param>
internal sealed class PluginActivations(IDatabase database, TimeProvider time)
{
    /// <summary>Gets the activations of a user, by plugin ID.</summary>
    /// <param name="ownerId">The ID of the account of the user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The activation of each plugin that the user changed.</returns>
    public async Task<IReadOnlyDictionary<string, PluginActivation>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Read, cancellationToken);
        var rows = await connection.QueryAsync<ActivationRow>(new CommandDefinition(
            """
            select extension_id as ExtensionId, enabled, accepted_version as AcceptedVersion, accepted_scopes as AcceptedScopes,
                accepted_network as AcceptedNetwork, accepted_devices as AcceptedDevices, accepted_sites as AcceptedSites, accepted_at as AcceptedAt,
                settings::text as Settings
            from content.plugin_activations where owner_id = @ownerId
            """,
            new { ownerId },
            cancellationToken: cancellationToken));
        return rows.Select(row => row.ToActivation()).ToDictionary(activation => activation.ExtensionId, StringComparer.Ordinal);
    }

    /// <summary>Gets the activation of one plugin for a user.</summary>
    /// <param name="ownerId">The ID of the account of the user.</param>
    /// <param name="extensionId">The ID of the plugin.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The activation, or null when the user never changed the plugin.</returns>
    public async Task<PluginActivation?> FindAsync(Guid ownerId, string extensionId, CancellationToken cancellationToken) =>
        (await ListAsync(ownerId, cancellationToken)).GetValueOrDefault(extensionId);

    /// <summary>Activates or deactivates a plugin for a user. The consent does not change.</summary>
    /// <param name="ownerId">The ID of the account of the user.</param>
    /// <param name="extensionId">The ID of the plugin.</param>
    /// <param name="enabled">Whether the plugin runs for the user.</param>
    /// <param name="failed">True when the core deactivates the plugin because it failed. The settings then keep the time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SetEnabledAsync(Guid ownerId, string extensionId, bool enabled, bool failed, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Write, cancellationToken);
        var now = time.GetUtcNow();

        // An activation removes the mark of the core. A deactivation by the core adds it.
        var mark = !enabled && failed ? JsonSerializer.Serialize(new { disabledByCoreAt = now }) : "{}";
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.plugin_activations (owner_id, extension_id, enabled, updated_at, settings)
            values (@ownerId, @extensionId, @enabled, @now, @mark::jsonb)
            on conflict (owner_id, extension_id) do update
            set enabled = excluded.enabled, updated_at = excluded.updated_at,
                settings = case when excluded.enabled then content.plugin_activations.settings - 'disabledByCoreAt'
                    else content.plugin_activations.settings || excluded.settings end
            """,
            new { ownerId, extensionId, enabled, now, mark },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Keeps the position of a plugin and its hidden contributions for an owner.
    /// </summary>
    /// <returns>False when the owner has no row for the plugin: the owner never activated it.</returns>
    public async Task<bool> SetLayoutAsync(Guid ownerId, string extensionId, int position, IReadOnlyList<string> hidden, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Write, cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            update content.plugin_activations
            set settings = settings || @layout::jsonb, updated_at = @now
            where owner_id = @ownerId and extension_id = @extensionId
            """,
            new { ownerId, extensionId, layout = JsonSerializer.Serialize(new { position, hidden }), now = time.GetUtcNow() },
            cancellationToken: cancellationToken)) == 1;
    }

    /// <summary>Activates a plugin for a user and keeps the access that the user accepted for a plugin version.</summary>
    /// <param name="ownerId">The ID of the account of the user.</param>
    /// <param name="extensionId">The ID of the plugin.</param>
    /// <param name="version">The plugin version that the user accepted.</param>
    /// <param name="accepted">The access that the user accepted. It replaces the access of the last consent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task AcceptAsync(Guid ownerId, string extensionId, string version, PluginGrant accepted, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Write, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.plugin_activations
                (owner_id, extension_id, enabled, updated_at, accepted_version, accepted_scopes, accepted_network, accepted_devices, accepted_sites, accepted_at)
            values (@ownerId, @extensionId, true, @now, @version, @scopes, @network, @devices, @sites, @now)
            on conflict (owner_id, extension_id) do update
            set enabled = true, updated_at = excluded.updated_at, accepted_version = excluded.accepted_version,
                accepted_scopes = excluded.accepted_scopes, accepted_network = excluded.accepted_network,
                accepted_devices = excluded.accepted_devices, accepted_sites = excluded.accepted_sites, accepted_at = excluded.accepted_at,
                settings = content.plugin_activations.settings - 'disabledByCoreAt'
            """,
            new
            {
                ownerId,
                extensionId,
                version,
                scopes = accepted.Scopes.ToArray(),
                network = accepted.Network.ToArray(),
                devices = accepted.Devices.ToArray(),
                sites = accepted.Sites.ToArray(),
                now = time.GetUtcNow(),
            },
            cancellationToken: cancellationToken));
    }

    private sealed class ActivationRow
    {
        public string ExtensionId { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public string? AcceptedVersion { get; set; }

        public string[] AcceptedScopes { get; set; } = [];

        public string[] AcceptedNetwork { get; set; } = [];

        public string[] AcceptedDevices { get; set; } = [];

        public string[] AcceptedSites { get; set; } = [];

        public DateTime? AcceptedAt { get; set; }

        public string? Settings { get; set; }

        public PluginActivation ToActivation() => new(
            ExtensionId,
            Enabled,
            AcceptedVersion,
            new PluginGrant(AcceptedScopes, AcceptedNetwork, AcceptedDevices, AcceptedSites),
            AcceptedAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
            PluginSettings.Parse(Settings));
    }
}
