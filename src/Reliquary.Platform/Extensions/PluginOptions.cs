namespace Reliquary.Platform.Extensions;

/// <summary>
/// The settings of the plugins on the instance. The section is <c>Plugins</c>.
/// See docs/decisions/0049-plugin-sandbox.md.
/// </summary>
public sealed class PluginOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Plugins";

    /// <summary>The default maximum number of active plugins for each user.</summary>
    public const int DefaultMaxActivePlugins = 8;

    /// <summary>Gets or sets the maximum number of active plugins for each user. The default is 8.</summary>
    public int MaxActivePlugins { get; set; } = DefaultMaxActivePlugins;

    /// <summary>
    /// Gets or sets the origin of the plugin frames, for example <c>https://plugins.example.org</c>.
    /// This second host name must send the requests to the same API.
    /// Without a value, the frames use the host of the instance, with an opaque origin.
    /// </summary>
    public string? FrameOrigin { get; set; }

    /// <summary>
    /// Gets or sets whether a user can load a plugin from a URL, for the development of plugins.
    /// The default is off. Only a development instance must turn it on, because the core does not check these plugins.
    /// </summary>
    public bool AllowDevelopmentPlugins { get; set; }
}
