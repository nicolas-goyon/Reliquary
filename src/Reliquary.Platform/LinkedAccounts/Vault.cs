using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Reliquary.Platform.LinkedAccounts;

/// <summary>
/// The keys of the vault. The configuration section is <c>Plugins:VaultKeys</c>: one value for each key ID.
/// See docs/decisions/0050-no-plugin-code-on-the-server.md.
/// </summary>
/// <remarks>
/// Each value is a key of 32 bytes in Base64, for example the output of <c>openssl rand -base64 32</c>.
/// The environment variable of the key <c>2026-11</c> is <c>Plugins__VaultKeys__2026-11</c>.
/// The vault ignores a key with an empty value, so a Compose file can declare a key that the operator does not set.
/// </remarks>
public sealed class VaultOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Plugins:VaultKeys";

    /// <summary>Gets the keys, by key ID.</summary>
    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);
}

/// <summary>The encrypted form of a secret, and the ID of the key that encrypted it.</summary>
/// <param name="KeyId">The ID of the key.</param>
/// <param name="Ciphertext">The format version, the nonce, the tag and the encrypted text.</param>
public sealed record SealedSecret(string KeyId, byte[] Ciphertext);

/// <summary>The owner, the plugin and the site of a secret. The vault uses them as associated data.</summary>
/// <param name="OwnerId">The ID of the account of the user.</param>
/// <param name="PluginId">The ID of the plugin.</param>
/// <param name="Site">The host name of the external site.</param>
public sealed record SecretScope(Guid OwnerId, string PluginId, string Site);

/// <summary>The instance has no vault key. The operator must set <c>Plugins:VaultKeys</c>.</summary>
public sealed class VaultNotConfiguredException()
    : Exception("The vault has no key. Set Plugins:VaultKeys with a key ID and a key of 32 bytes in Base64.");

/// <summary>The vault does not have the key that encrypted a secret.</summary>
/// <param name="keyId">The ID of the missing key.</param>
public sealed class VaultKeyMissingException(string keyId)
    : Exception($"The vault has no key with the ID \"{keyId}\". Add the old key to Plugins:VaultKeys, or link the account again.")
{
    /// <summary>Gets the ID of the missing key.</summary>
    public string KeyId { get; } = keyId;
}

/// <summary>
/// Encrypts and decrypts the secrets of linked accounts with AES-GCM. See docs/decisions/0050-no-plugin-code-on-the-server.md.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The vault encrypts each new secret with the current key: the key with the last ID in ordinal order.</item>
/// <item>It decrypts a secret with the key of its key ID, so the operator keeps the old keys after a rotation.</item>
/// <item>The associated data is the owner ID, the plugin ID and the site. A secret thus decrypts only for its owner, its plugin and its site.</item>
/// </list>
/// </remarks>
public sealed partial class Vault
{
    /// <summary>The size of a key, in bytes.</summary>
    public const int KeySize = 32;

    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;

    private readonly Dictionary<string, byte[]> keys;

    /// <summary>
    /// Reads the keys of the configuration.
    /// </summary>
    /// <param name="options">The keys.</param>
    /// <exception cref="InvalidOperationException">A key ID or a key is not valid.</exception>
    public Vault(IOptions<VaultOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var parsed = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (keyId, value) in options.Value.Keys.Where(key => !string.IsNullOrWhiteSpace(key.Value)))
        {
            parsed[keyId] = ParseKey(keyId, value);
        }

        keys = parsed;
        CurrentKeyId = parsed.Keys.Order(StringComparer.Ordinal).LastOrDefault();
    }

    /// <summary>Gets the ID of the key that encrypts the new secrets, or <see langword="null"/> when the vault has no key.</summary>
    public string? CurrentKeyId { get; }

    /// <summary>Gets a value that tells whether the vault has a key.</summary>
    public bool IsConfigured => CurrentKeyId is not null;

    /// <summary>
    /// Tells whether the vault has a key.
    /// </summary>
    /// <param name="keyId">The ID of the key.</param>
    /// <returns><see langword="true"/> when the vault can decrypt the secrets of this key.</returns>
    public bool HasKey(string keyId) => keys.ContainsKey(keyId);

    /// <summary>
    /// Checks a key of the configuration.
    /// </summary>
    /// <param name="keyId">The ID of the key.</param>
    /// <param name="value">The key in Base64.</param>
    /// <returns>The error, or <see langword="null"/> when the key is valid or empty. The vault ignores an empty key.</returns>
    public static string? CheckKey(string keyId, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!KeyIdPattern().IsMatch(keyId))
        {
            return $"The vault key ID \"{keyId}\" is not valid. Use 1 to 64 letters, digits, dots, hyphens and underscores.";
        }

        var bytes = new byte[KeySize + 1];
        if (!Convert.TryFromBase64String(value.Trim(), bytes, out var length) || length != KeySize)
        {
            return $"The vault key \"{keyId}\" is not a key of {KeySize} bytes in Base64. Make a key with \"openssl rand -base64 32\".";
        }

        return null;
    }

    /// <summary>
    /// Encrypts a secret with the current key.
    /// </summary>
    /// <param name="secret">The secret.</param>
    /// <param name="scope">The owner, the plugin and the site of the secret.</param>
    /// <returns>The encrypted secret.</returns>
    /// <exception cref="VaultNotConfiguredException">The vault has no key.</exception>
    public SealedSecret Encrypt(string secret, SecretScope scope)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var keyId = CurrentKeyId ?? throw new VaultNotConfiguredException();
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var output = new byte[HeaderSize + plaintext.Length];
        output[0] = FormatVersion;
        var nonce = output.AsSpan(1, NonceSize);
        var tag = output.AsSpan(1 + NonceSize, TagSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(keys[keyId], TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(HeaderSize), tag, AssociatedData(scope));
        CryptographicOperations.ZeroMemory(plaintext);
        return new SealedSecret(keyId, output);
    }

    /// <summary>
    /// Decrypts a secret.
    /// </summary>
    /// <param name="sealedSecret">The encrypted secret.</param>
    /// <param name="scope">The owner, the plugin and the site of the secret.</param>
    /// <returns>The secret.</returns>
    /// <exception cref="VaultKeyMissingException">The vault does not have the key of the secret.</exception>
    /// <exception cref="CryptographicException">The ciphertext changed, or the scope is not the scope of the encryption.</exception>
    public string Decrypt(SealedSecret sealedSecret, SecretScope scope)
    {
        ArgumentNullException.ThrowIfNull(sealedSecret);
        if (!keys.TryGetValue(sealedSecret.KeyId, out var key))
        {
            throw new VaultKeyMissingException(sealedSecret.KeyId);
        }

        var input = sealedSecret.Ciphertext;
        if (input.Length < HeaderSize || input[0] != FormatVersion)
        {
            throw new CryptographicException("The encrypted secret does not have the format of the vault.");
        }

        var plaintext = new byte[input.Length - HeaderSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(input.AsSpan(1, NonceSize), input.AsSpan(HeaderSize), input.AsSpan(1 + NonceSize, TagSize), plaintext, AssociatedData(scope));
        var secret = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        return secret;
    }

    private static byte[] AssociatedData(SecretScope scope) =>
        Encoding.UTF8.GetBytes($"reliquary-vault-1\n{scope.OwnerId:D}\n{scope.PluginId}\n{scope.Site}");

    private static byte[] ParseKey(string keyId, string value)
    {
        if (CheckKey(keyId, value) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        return Convert.FromBase64String(value.Trim());
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex KeyIdPattern();
}
