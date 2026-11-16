using Microsoft.Extensions.Logging;

namespace Reliquary.Platform.LinkedAccounts;

/// <summary>
/// The log messages of the linked accounts. The messages never contain a secret.
/// </summary>
internal static partial class LinkedAccountLog
{
    [LoggerMessage(EventId = 1500, Level = LogLevel.Warning, Message = "The vault has no key {KeyId} for a secret of the plugin {PluginId} and the site {Site}. Add the old key to Plugins:VaultKeys, or ask the user to link the account again.")]
    public static partial void VaultKeyMissing(this ILogger logger, string keyId, string pluginId, string site);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Error, Message = "The vault did not decrypt a secret of the plugin {PluginId} and the site {Site}. The secret or its key changed.")]
    public static partial void SecretUnreadable(this ILogger logger, Exception exception, string pluginId, string site);
}
