namespace Reliquary.BuildingBlocks.Security;

/// <summary>
/// Tells whether a plugin runs for a user, and with which scopes. The platform implements it with the extension registry.
/// The Accounts component uses it for the plugin tokens. See docs/decisions/0049-plugin-sandbox.md.
/// </summary>
public interface IPluginAccess
{
    /// <summary>
    /// Gets the scopes that a plugin can use for a user.
    /// </summary>
    /// <param name="ownerId">The ID of the account of the user.</param>
    /// <param name="pluginId">The ID of the plugin.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The scopes, or <see langword="null"/> when the plugin does not run for the user:
    /// the instance does not have the plugin, does not enable it, or the plugin does not support the core API, or the user did not activate it.
    /// </returns>
    Task<IReadOnlyList<string>?> ScopesAsync(Guid ownerId, string pluginId, CancellationToken cancellationToken);
}
