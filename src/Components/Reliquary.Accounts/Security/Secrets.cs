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
/// The format of a secret with a visible prefix: <c>&lt;start&gt;&lt;prefix&gt;_&lt;secret&gt;</c>.
/// </summary>
/// <remarks>
/// The start tells the kind of the secret. The prefix identifies the secret and is visible in the lists.
/// The secret is visible only one time, at the creation. The database stores the prefix and a hash of the secret.
/// </remarks>
/// <param name="start">The start of each secret of this kind, for example <c>rlq_</c>.</param>
internal sealed class PrefixedSecretFormat(string start)
{
    private const int PrefixLength = 12;
    private const int SecretLength = 40;

    /// <summary>The format of the personal API keys.</summary>
    public static readonly PrefixedSecretFormat ApiKey = new("rlq_");

    /// <summary>The format of the secrets of the service accounts.</summary>
    public static readonly PrefixedSecretFormat ServiceAccount = new("rls_");

    /// <summary>The format of the delegation tokens.</summary>
    public static readonly PrefixedSecretFormat Delegation = new("rld_");

    /// <summary>Gets the start of each secret of this kind.</summary>
    public string Start { get; } = start;

    /// <summary>
    /// Tells whether an Authorization header contains a secret of this kind.
    /// </summary>
    /// <param name="authorization">The value of the header.</param>
    /// <returns><see langword="true"/> when the header is <c>Bearer</c> with this start.</returns>
    public bool Matches(string authorization) => authorization.StartsWith("Bearer " + Start, StringComparison.Ordinal);

    /// <summary>
    /// Creates a new secret.
    /// </summary>
    /// <returns>The full secret, its prefix and the hash of its secret part.</returns>
    public (string Value, string Prefix, byte[] SecretHash) Create()
    {
        var prefix = Secrets.NewAlphanumeric(PrefixLength);
        var secret = Secrets.NewAlphanumeric(SecretLength);
        return ($"{Start}{prefix}_{secret}", prefix, Secrets.Hash(secret));
    }

    /// <summary>
    /// Reads the prefix and the secret part of a secret.
    /// </summary>
    /// <param name="value">The full secret.</param>
    /// <param name="prefix">The prefix.</param>
    /// <param name="secret">The secret part.</param>
    /// <returns><see langword="true"/> when the value has the correct format.</returns>
    public bool TryParse(string value, out string prefix, out string secret)
    {
        prefix = string.Empty;
        secret = string.Empty;
        if (!value.StartsWith(Start, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value[Start.Length..].Split('_');
        if (parts is not [{ Length: PrefixLength } first, { Length: SecretLength } second] || !first.All(char.IsAsciiLetterOrDigit) || !second.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        (prefix, secret) = (first, second);
        return true;
    }
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

    /// <summary>
    /// Creates a new key.
    /// </summary>
    /// <returns>The full key, its prefix and the hash of its secret.</returns>
    public static (string Key, string Prefix, byte[] SecretHash) Create() => PrefixedSecretFormat.ApiKey.Create();

    /// <summary>
    /// Reads the prefix and the secret of a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="prefix">The prefix.</param>
    /// <param name="secret">The secret.</param>
    /// <returns><see langword="true"/> when the key has the correct format.</returns>
    public static bool TryParse(string key, out string prefix, out string secret) => PrefixedSecretFormat.ApiKey.TryParse(key, out prefix, out secret);
}
