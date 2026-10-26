using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Reliquary.Accounts.Security;

/// <summary>
/// Creates random tokens and their hashes. The database stores only the hashes.
/// </summary>
internal static class Secrets
{
    private const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// Creates a random token for a link or a cookie.
    /// </summary>
    /// <returns>32 random bytes in base64url.</returns>
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Creates a random text with letters and digits only.
    /// </summary>
    /// <param name="length">The number of characters.</param>
    /// <returns>The text.</returns>
    public static string NewAlphanumeric(int length) =>
        string.Create(length, 0, static (span, _) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = Alphanumeric[RandomNumberGenerator.GetInt32(Alphanumeric.Length)];
            }
        });

    /// <summary>
    /// Gets the SHA-256 hash of a token.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The hash.</returns>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

/// <summary>
/// The format of the personal API keys: <c>rlq_&lt;prefix&gt;_&lt;secret&gt;</c>.
/// </summary>
/// <remarks>
/// The prefix identifies the key and is visible in the list of keys. The secret is visible only one time, at the creation.
/// </remarks>
internal static class ApiKeyFormat
{
    /// <summary>The start of each key. It lets the API tell an API key from an access token.</summary>
    public const string Start = "rlq_";

    private const int PrefixLength = 12;
    private const int SecretLength = 40;

    /// <summary>
    /// Creates a new key.
    /// </summary>
    /// <returns>The full key, its prefix and the hash of its secret.</returns>
    public static (string Key, string Prefix, byte[] SecretHash) Create()
    {
        var prefix = Secrets.NewAlphanumeric(PrefixLength);
        var secret = Secrets.NewAlphanumeric(SecretLength);
        return ($"{Start}{prefix}_{secret}", prefix, Secrets.Hash(secret));
    }

    /// <summary>
    /// Reads the prefix and the secret of a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="prefix">The prefix.</param>
    /// <param name="secret">The secret.</param>
    /// <returns><see langword="true"/> when the key has the correct format.</returns>
    public static bool TryParse(string key, out string prefix, out string secret)
    {
        prefix = string.Empty;
        secret = string.Empty;
        if (!key.StartsWith(Start, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = key[Start.Length..].Split('_');
        if (parts is not [{ Length: PrefixLength } first, { Length: SecretLength } second] || !first.All(char.IsAsciiLetterOrDigit) || !second.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        (prefix, secret) = (first, second);
        return true;
    }
}
