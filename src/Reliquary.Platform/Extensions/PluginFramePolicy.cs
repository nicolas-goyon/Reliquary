using System.Text.RegularExpressions;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// Makes the Content Security Policy of the plugin frames. Each frame endpoint uses this class, so all the frames get the same rules.
/// See docs/decisions/0049-plugin-sandbox.md.
/// </summary>
/// <remarks>
/// All the frames have these rules:
/// <list type="bullet">
/// <item><c>default-src 'none'</c>: a resource type without a directive is blocked.</item>
/// <item><c>worker-src blob:</c>: in a frame with an opaque origin, only a classic Worker from a <c>blob:</c> URL works.</item>
/// <item><c>base-uri 'none'</c> and <c>form-action 'none'</c>.</item>
/// </list>
/// </remarks>
public static partial class PluginFramePolicy
{
    /// <summary>
    /// The Content Security Policy of each plugin file. A browser that opens a file directly gives it an opaque origin,
    /// so a plugin file cannot run on the origin of the instance. The policy has no effect on a script that a frame loads.
    /// </summary>
    public const string ForFile = "sandbox; default-src 'none'";

    /// <summary>
    /// Makes the Content Security Policy of the frame of an installed plugin version.
    /// </summary>
    /// <param name="filesUrl">The absolute URL of the folder of the plugin files, with a slash at the end, for example <c>https://example.org/api/v1/plugins/hello/1.0.0/files/</c>.</param>
    /// <param name="network">The origins that the frame can reach. The policy ignores an origin that does not follow the rule of the plugin manifest.</param>
    /// <returns>The value of the header <c>Content-Security-Policy</c>.</returns>
    /// <exception cref="ArgumentException">The URL of the files is not an absolute http or https URL with a slash at the end.</exception>
    public static string ForPlugin(Uri filesUrl, IEnumerable<string> network)
    {
        ArgumentNullException.ThrowIfNull(filesUrl);
        ArgumentNullException.ThrowIfNull(network);
        if (!filesUrl.IsAbsoluteUri || (filesUrl.Scheme != Uri.UriSchemeHttp && filesUrl.Scheme != Uri.UriSchemeHttps)
            || filesUrl.UserInfo.Length > 0 || filesUrl.Query.Length > 0 || !filesUrl.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException("The URL of the plugin files must be an absolute http or https URL with a slash at the end and no query.", nameof(filesUrl));
        }

        // The path part limits the scripts to the files of this plugin version. A CSP source cannot contain a space, a comma or a semicolon.
        var files = filesUrl.GetLeftPart(UriPartial.Path);
        var origins = NetworkOrigins(network);
        return Join(
            "default-src 'none'",
            $"script-src {files}",
            $"style-src {files} 'unsafe-inline'",
            $"font-src {files}",
            $"img-src {files}{origins} data: blob:",
            $"connect-src {files}{origins}");
    }

    /// <summary>
    /// Makes the Content Security Policy of the frame of a development plugin: a plugin that a user loads from a URL on a development instance.
    /// </summary>
    /// <param name="entry">The address of the entry point of the plugin. The policy uses only its origin.</param>
    /// <param name="network">The origins of the network list of the plugin manifest. The policy uses only the origin of each URL.</param>
    /// <returns>The value of the header <c>Content-Security-Policy</c>.</returns>
    public static string ForDevelopment(Uri entry, IReadOnlyList<Uri> network)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(network);
        var origin = Origin(entry);

        // The development server of the plugin can reload the plugin through a WebSocket on the same host.
        var socket = (entry.Scheme == Uri.UriSchemeHttps ? "wss" : "ws") + origin[entry.Scheme.Length..];
        var networkOrigins = string.Concat(network.Select(uri => " " + Origin(uri)).Distinct(StringComparer.Ordinal));
        return Join(
            "default-src 'none'",
            $"script-src {origin}",
            $"style-src {origin} 'unsafe-inline'",
            $"font-src {origin}",
            $"img-src {origin}{networkOrigins} data: blob:",
            $"connect-src {origin} {socket}{networkOrigins}");
    }

    /// <summary>
    /// The browser features that the Permissions-Policy header of a plugin frame turns off.
    /// The header turns off all these features, except the features of the devices that the user accepted.
    /// </summary>
    public static readonly IReadOnlyList<string> RestrictedFeatures =
    [
        "accelerometer", "ambient-light-sensor", "autoplay", "bluetooth", "browsing-topics", "camera", "clipboard-read", "clipboard-write",
        "display-capture", "encrypted-media", "fullscreen", "gamepad", "geolocation", "gyroscope", "hid", "identity-credentials-get",
        "idle-detection", "local-fonts", "local-network", "local-network-access", "loopback-network", "magnetometer", "microphone", "midi",
        "otp-credentials", "payment", "picture-in-picture", "publickey-credentials-create", "publickey-credentials-get", "screen-wake-lock",
        "serial", "speaker-selection", "storage-access", "usb", "web-share", "window-management", "xr-spatial-tracking",
    ];

    /// <summary>
    /// The browser features of each device of the plugin manifest.
    /// Chrome first called the access to <c>localhost</c> <c>local-network-access</c>, then <c>loopback-network</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DeviceFeatures = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["camera"] = ["camera"],
        ["loopback-network"] = ["loopback-network", "local-network-access"],
    };

    /// <summary>
    /// Makes the Permissions-Policy header of a plugin frame.
    /// The header turns off each feature of <see cref="RestrictedFeatures"/>, except the features of the accepted devices.
    /// A header can only remove a permission. The attribute <c>allow</c> of the iframe gives the accepted devices to the frame.
    /// </summary>
    /// <param name="devices">The devices that the user accepted, for example <c>camera</c>. The method ignores an unknown device.</param>
    /// <returns>The value of the header <c>Permissions-Policy</c>.</returns>
    public static string PermissionsPolicy(IEnumerable<string> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var allowed = devices.SelectMany(device => DeviceFeatures.GetValueOrDefault(device) ?? []).ToHashSet(StringComparer.Ordinal);
        return string.Join(", ", RestrictedFeatures.Where(feature => !allowed.Contains(feature)).Select(feature => $"{feature}=()"));
    }

    /// <summary>
    /// Gets the network origins that a CSP can contain: the origins that follow <see cref="PluginManifestRules.NetworkPattern"/>, with no duplicates.
    /// </summary>
    /// <param name="network">The origins.</param>
    /// <returns>A space and each origin, or an empty text.</returns>
    private static string NetworkOrigins(IEnumerable<string> network) =>
        string.Concat(network.Where(origin => origin is not null && origin.Length <= PluginManifestRules.NetworkMaxLength && NetworkRegex().IsMatch(origin))
            .Distinct(StringComparer.Ordinal)
            .Select(origin => " " + origin));

    /// <summary>Adds the rules of all the frames.</summary>
    private static string Join(params string[] directives) =>
        string.Join("; ", [.. directives, "worker-src blob:", "base-uri 'none'", "form-action 'none'"]);

    private static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    [GeneratedRegex(PluginManifestRules.NetworkPattern)]
    private static partial Regex NetworkRegex();
}
