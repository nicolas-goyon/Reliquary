using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// Tells whether a plugin runs for a user, from the extension registry and the consent of the user.
/// A plugin runs when the instance has it, enables it, the plugin supports the core API, the plugin catalog did not revoke the version,
/// the user activated it, and the user accepted all the access of the installed version.
/// </summary>
/// <param name="registry">The extension registry.</param>
/// <param name="activations">The plugin activations of the users.</param>
internal sealed class PluginAccess(ExtensionRegistry registry, PluginActivations activations) : IPluginAccess
{
    /// <inheritdoc />
    /// <remarks>
    /// The scopes are the scopes of the public API that the installed version asks for and that the user accepted.
    /// A paused plugin gets no scopes: the method returns null.
    /// </remarks>
    public async Task<IReadOnlyList<string>?> ScopesAsync(Guid ownerId, string pluginId, CancellationToken cancellationToken)
    {
        var row = await registry.FindAsync(pluginId, cancellationToken);
        if (row is not { Kind: "plugin", Enabled: true, Compatible: true, Revoked: false })
        {
            return null;
        }

        var asked = PluginGrant.FromManifest(row.Manifest);
        var activation = await activations.FindAsync(ownerId, pluginId, cancellationToken);
        if (activation is null || !activation.Runs(asked))
        {
            return null;
        }

        var accepted = asked.Intersect(activation.Accepted);
        return Scopes.All.Where(scope => accepted.Scopes.Contains(scope, StringComparer.Ordinal)).ToList();
    }
}
