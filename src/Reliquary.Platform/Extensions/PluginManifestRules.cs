using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// The rules of the plugin manifest.
/// The JSON Schema <c>sdk/schema/manifest.schema.json</c> gives the same rules to the SDK and to the plugin catalog.
/// A test compares the schema with these values, so change the two files together.
/// </summary>
public static class PluginManifestRules
{
    /// <summary>The fields that a plugin manifest can contain.</summary>
    public static readonly IReadOnlyList<string> Fields =
        ["$schema", "id", "kind", "name", "description", "version", "coreApi", "entry", "permissions", "slots", "network", "devices", "accounts", "panels", "formats", "i18n", "settings"];

    /// <summary>The fields that a plugin manifest must contain.</summary>
    public static readonly IReadOnlyList<string> RequiredFields = ["id", "name", "version", "coreApi", "entry"];

    /// <summary>The pattern of the plugin ID.</summary>
    public const string IdPattern = "^[a-z0-9][a-z0-9-]{1,63}$";

    /// <summary>The maximum length of the name.</summary>
    public const int NameMaxLength = 100;

    /// <summary>The maximum length of the description.</summary>
    public const int DescriptionMaxLength = 500;

    /// <summary>The pattern of the version: major.minor.patch with an optional pre-release part, and no build metadata.</summary>
    public const string VersionPattern = "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\\.[0-9A-Za-z-]+)*)?$";

    /// <summary>The maximum length of the version.</summary>
    public const int VersionMaxLength = 64;

    /// <summary>The maximum length of the range of core API versions.</summary>
    public const int CoreApiMaxLength = 100;

    /// <summary>The pattern of the entry: a relative path of a JavaScript module. A path segment cannot start with a dot.</summary>
    public const string EntryPattern = "^[A-Za-z0-9_-][A-Za-z0-9._-]*(/[A-Za-z0-9_-][A-Za-z0-9._-]*)*\\.m?js$";

    /// <summary>The maximum length of the entry.</summary>
    public const int EntryMaxLength = 200;

    /// <summary>The scopes that a plugin can ask for: all the scopes of the public API.</summary>
    public static IReadOnlyList<string> Permissions => Scopes.All;

    /// <summary>The pattern of a slot name.</summary>
    public const string SlotPattern = "^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$";

    /// <summary>The maximum length of a slot name.</summary>
    public const int SlotMaxLength = 64;

    /// <summary>The maximum number of slots.</summary>
    public const int MaxSlots = 20;

    /// <summary>
    /// The pattern of a network origin: an HTTPS origin with an optional wildcard for one level, or <c>http://localhost</c> or <c>http://127.0.0.1</c>.
    /// The CSP syntax does not accept an IPv6 address, so <c>[::1]</c> is not permitted.
    /// </summary>
    public const string NetworkPattern = "^(https://(\\*\\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}|https?://(localhost|127\\.0\\.0\\.1))(:[0-9]{1,5})?$";

    /// <summary>The maximum length of a network origin.</summary>
    public const int NetworkMaxLength = 260;

    /// <summary>The maximum number of network origins.</summary>
    public const int MaxNetwork = 20;

    /// <summary>The devices that a plugin can ask for.</summary>
    public static readonly IReadOnlyList<string> Devices = ["camera", "loopback-network"];

    /// <summary>The maximum number of linked account sites.</summary>
    public const int MaxAccounts = 10;

    /// <summary>The pattern of the host name of a linked account site.</summary>
    public const string SitePattern = "^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$";

    /// <summary>The maximum length of a site.</summary>
    public const int SiteMaxLength = 253;

    /// <summary>The authentication methods of a linked account.</summary>
    public static readonly IReadOnlyList<string> AuthMethods = ["public-username", "api-key", "oauth2-pkce"];

    /// <summary>The sync modes of a linked account.</summary>
    public static readonly IReadOnlyList<string> SyncModes = ["manual", "on-open", "periodic"];

    /// <summary>The minimum interval of a periodic sync, in minutes.</summary>
    public const int MinSyncIntervalMinutes = 15;

    /// <summary>The maximum interval of a periodic sync, in minutes.</summary>
    public const int MaxSyncIntervalMinutes = 1440;

    /// <summary>The maximum number of panels.</summary>
    public const int MaxPanels = 20;

    /// <summary>The pattern of the ID of a panel or of a format.</summary>
    public const string ItemIdPattern = "^[a-z][a-z0-9-]{0,31}$";

    /// <summary>The slots where a panel can render.</summary>
    public static readonly IReadOnlyList<string> PanelSlots = ["analysis.tabs", "card.page", "plugin.page", "plugin.dialog"];

    /// <summary>The maximum length of the title of a panel or of a format.</summary>
    public const int TitleMaxLength = 60;

    /// <summary>The maximum number of formats.</summary>
    public const int MaxFormats = 10;

    /// <summary>The directions of a format.</summary>
    public static readonly IReadOnlyList<string> FormatDirections = ["import", "export"];

    /// <summary>The data of a format.</summary>
    public static readonly IReadOnlyList<string> FormatTargets = ["deck", "collection"];

    /// <summary>The pattern of a file name extension.</summary>
    public const string FileExtensionPattern = "^\\.[a-z0-9]{1,10}$";

    /// <summary>The maximum number of file name extensions of a format.</summary>
    public const int MaxFileExtensions = 5;

    /// <summary>The pattern of a language code.</summary>
    public const string LocalePattern = "^[a-z]{2}(-[A-Z]{2})?$";

    /// <summary>The maximum number of languages.</summary>
    public const int MaxLocales = 10;

    /// <summary>The pattern of a text key.</summary>
    public const string TextKeyPattern = "^[a-z][A-Za-z0-9]*(\\.[A-Za-z0-9-]+)*$";

    /// <summary>The maximum length of a text key.</summary>
    public const int TextKeyMaxLength = 100;

    /// <summary>The maximum number of texts for each language.</summary>
    public const int MaxTexts = 200;

    /// <summary>The maximum length of a text.</summary>
    public const int TextMaxLength = 200;
}
