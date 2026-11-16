using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// Tells whether a plugin runs for a user, from the extension registry.
/// A plugin runs when the instance has it, enables it, the plugin supports the core API, and the user activated it.
/// </summary>
/// <param name="registry">The extension registry.</param>
internal sealed class PluginAccess(ExtensionRegistry registry) : IPluginAccess
{
    /// <inheritdoc />
    /// <remarks>
    /// The scopes are the permissions of the manifest that are scopes of the public API.
    /// The consent of issue #287 will keep the scopes that the user accepted, and this method will then give only these scopes.
    /// </remarks>
    public async Task<IReadOnlyList<string>?> ScopesAsync(Guid ownerId, string pluginId, CancellationToken cancellationToken)
    {
        var row = await registry.FindAsync(pluginId, cancellationToken);
        if (row is not { Kind: "plugin", Enabled: true, Compatible: true })
        {
            return null;
        }

        var activations = await registry.PluginActivationsAsync(ownerId, cancellationToken);
        if (!activations.GetValueOrDefault(pluginId))
        {
            return null;
        }

        return Scopes.All.Where(scope => row.Permissions.Contains(scope, StringComparer.Ordinal)).ToList();
    }
}
