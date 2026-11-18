using System.Text.Json;

namespace Reliquary.Platform.LinkedAccounts;

/// <summary>An external site of a plugin manifest, from the field <c>accounts</c>.</summary>
/// <param name="Site">The host name of the site, for example <c>moxfield.com</c>.</param>
/// <param name="Auth">The authentication method: <c>public-username</c>, <c>api-key</c> or <c>oauth2-pkce</c>.</param>
/// <param name="SyncMode">The sync mode: <c>manual</c>, <c>on-open</c> or <c>periodic</c>.</param>
/// <param name="IntervalMinutes">The interval of a periodic sync, in minutes, or <see langword="null"/>.</param>
internal sealed record ManifestAccount(string Site, string Auth, string SyncMode, int? IntervalMinutes);

/// <summary>
/// Reads the field <c>accounts</c> of a plugin manifest. <c>PluginManifestCheck</c> checked the manifest at the installation.
/// </summary>
internal static class ManifestAccounts
{
    /// <summary>The authentication method with a public user name and no secret.</summary>
    public const string PublicUserName = "public-username";

    /// <summary>The authentication method with an API key. The vault keeps the key.</summary>
    public const string ApiKey = "api-key";

    /// <summary>The authentication method with OAuth 2 and PKCE. The core does not support it yet.</summary>
    public const string OAuth = "oauth2-pkce";

    /// <summary>The minimum interval of a periodic sync, in minutes.</summary>
    public const int MinIntervalMinutes = 15;

    /// <summary>
    /// Reads the sites of a plugin manifest.
    /// </summary>
    /// <param name="manifestJson">The JSON text of the manifest.</param>
    /// <returns>The sites, in the order of the manifest. The list is empty when the manifest has no field <c>accounts</c>.</returns>
    public static IReadOnlyList<ManifestAccount> Read(string manifestJson)
    {
        using var document = JsonDocument.Parse(manifestJson);
        if (!document.RootElement.TryGetProperty("accounts", out var accounts) || accounts.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ManifestAccount>();
        foreach (var account in accounts.EnumerateArray())
        {
            if (account.ValueKind != JsonValueKind.Object
                || !account.TryGetProperty("site", out var site) || site.GetString() is not { Length: > 0 } siteName
                || !account.TryGetProperty("auth", out var auth) || auth.GetString() is not { Length: > 0 } method)
            {
                continue;
            }

            var mode = "manual";
            int? interval = null;
            if (account.TryGetProperty("sync", out var sync) && sync.ValueKind == JsonValueKind.Object)
            {
                mode = sync.TryGetProperty("mode", out var value) && value.GetString() is { Length: > 0 } text ? text : mode;
                if (sync.TryGetProperty("intervalMinutes", out var minutes) && minutes.TryGetInt32(out var number))
                {
                    interval = Math.Max(number, MinIntervalMinutes);
                }
            }

            result.Add(new ManifestAccount(siteName, method, mode, interval));
        }

        return result;
    }
}
