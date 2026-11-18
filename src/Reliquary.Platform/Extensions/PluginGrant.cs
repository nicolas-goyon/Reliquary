using System.Text.Json;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// The access that a plugin version asks for, or the access that a user accepted: scopes, network origins, devices and linked account sites.
/// See issue #287 and docs/architecture/extensions.md.
/// </summary>
/// <param name="Scopes">The scopes of the public API, for example <c>decks:read</c>.</param>
/// <param name="Network">The network origins, with the text of the plugin manifest.</param>
/// <param name="Devices">The devices: <c>camera</c> and <c>loopback-network</c>.</param>
/// <param name="Sites">The host names of the linked account sites.</param>
public sealed record PluginGrant(IReadOnlyList<string> Scopes, IReadOnlyList<string> Network, IReadOnlyList<string> Devices, IReadOnlyList<string> Sites)
{
    /// <summary>Gets a grant with no access.</summary>
    public static PluginGrant Empty { get; } = new([], [], [], []);

    /// <summary>Tells whether the grant has no access.</summary>
    public bool IsEmpty => Scopes.Count == 0 && Network.Count == 0 && Devices.Count == 0 && Sites.Count == 0;

    /// <summary>
    /// Reads the access that a plugin manifest asks for. The API checked the manifest at install time.
    /// </summary>
    /// <param name="manifest">The JSON text of the plugin manifest.</param>
    /// <returns>The grant. A missing field gives an empty list.</returns>
    public static PluginGrant FromManifest(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Empty;
        }

        var sites = new List<string>();
        if (root.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
        {
            foreach (var account in accounts.EnumerateArray())
            {
                if (account.ValueKind == JsonValueKind.Object && account.TryGetProperty("site", out var site) && site.ValueKind == JsonValueKind.String)
                {
                    sites.Add(site.GetString()!);
                }
            }
        }

        return new PluginGrant(Strings(root, "permissions"), Strings(root, "network"), Strings(root, "devices"), Distinct(sites));
    }

    /// <summary>
    /// Gets the access of <paramref name="asked"/> that this grant does not contain. The comparison uses the same text, so a wildcard covers only the same wildcard.
    /// </summary>
    /// <param name="asked">The access that a plugin version asks for.</param>
    /// <returns>The difference. It is empty when this grant covers all the access.</returns>
    public PluginGrant Missing(PluginGrant asked)
    {
        ArgumentNullException.ThrowIfNull(asked);
        return new PluginGrant(Except(asked.Scopes, Scopes), Except(asked.Network, Network), Except(asked.Devices, Devices), Except(asked.Sites, Sites));
    }

    /// <summary>Tells whether this grant contains all the access of <paramref name="asked"/>.</summary>
    /// <param name="asked">The access that a plugin version asks for.</param>
    /// <returns><see langword="true"/> when no access is missing.</returns>
    public bool Covers(PluginGrant asked) => Missing(asked).IsEmpty;

    /// <summary>Gets the access that is in this grant and in <paramref name="other"/>.</summary>
    /// <param name="other">The other grant.</param>
    /// <returns>The common access, in the order of this grant.</returns>
    public PluginGrant Intersect(PluginGrant other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new PluginGrant(Common(Scopes, other.Scopes), Common(Network, other.Network), Common(Devices, other.Devices), Common(Sites, other.Sites));
    }

    private static string[] Strings(JsonElement root, string name) =>
        root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? Distinct(list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!))
            : [];

    private static string[] Distinct(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).ToArray();

    private static string[] Except(IReadOnlyList<string> values, IReadOnlyList<string> known) =>
        values.Where(value => !known.Contains(value, StringComparer.Ordinal)).ToArray();

    private static string[] Common(IReadOnlyList<string> values, IReadOnlyList<string> other) =>
        values.Where(value => other.Contains(value, StringComparer.Ordinal)).ToArray();
}
