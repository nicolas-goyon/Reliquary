using System.Security.Cryptography;

namespace Reliquary.Platform.Extensions.Catalog;

/// <summary>
/// The ECDSA P-256 public keys that can sign the catalog index.
/// </summary>
public sealed class CatalogKeyRing : IDisposable
{
    /// <summary>The maximum length of a key ID.</summary>
    public const int MaxKeyIdLength = 64;

    private readonly List<(string Id, ECDsa Key)> keys;

    private CatalogKeyRing(List<(string Id, ECDsa Key)> keys) => this.keys = keys;

    /// <summary>Gets the IDs of the keys.</summary>
    public IReadOnlyList<string> KeyIds => keys.Select(key => key.Id).ToList();

    /// <summary>
    /// Reads the keys of the settings.
    /// </summary>
    /// <param name="keys">The keys of the setting <c>Plugins:Catalog:Keys</c>.</param>
    /// <returns>The key ring. Dispose it after use.</returns>
    /// <exception cref="InvalidOperationException">A key is not a valid ECDSA P-256 public key, or two keys have the same ID.</exception>
    public static CatalogKeyRing Create(IReadOnlyList<PluginCatalogKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var loaded = new List<(string Id, ECDsa Key)>();
        try
        {
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key.Id) || key.Id.Length > MaxKeyIdLength)
                {
                    throw new InvalidOperationException($"A key of the setting Plugins:Catalog:Keys has no ID, or an ID with more than {MaxKeyIdLength} characters. Give each key an ID.");
                }

                if (loaded.Any(item => item.Id == key.Id))
                {
                    throw new InvalidOperationException($"The setting Plugins:Catalog:Keys has two keys with the ID \"{key.Id}\". Give each key a different ID.");
                }

                loaded.Add((key.Id, Import(key)));
            }
        }
        catch
        {
            foreach (var item in loaded)
            {
                item.Key.Dispose();
            }

            throw;
        }

        return new CatalogKeyRing(loaded);
    }

    /// <summary>
    /// Finds the key that made a signature.
    /// </summary>
    /// <param name="data">The signed bytes.</param>
    /// <param name="signature">The ECDSA signature in the DER format of <c>openssl dgst -sha256 -sign</c>.</param>
    /// <returns>The ID of the key, or <see langword="null"/> when no key of the ring made the signature.</returns>
    public string? FindSigner(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        foreach (var (id, key) in keys)
        {
            if (key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            {
                return id;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var (_, key) in keys)
        {
            key.Dispose();
        }

        keys.Clear();
    }

    /// <summary>
    /// Tells whether a curve is P-256. Some platforms give only the OID value of a named curve, and other platforms give only its name.
    /// </summary>
    private static bool IsP256(ECCurve curve) =>
        curve.IsNamed && (curve.Oid.Value == "1.2.840.10045.3.1.7" || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256" or "secp256r1" or "prime256v1");

    private static ECDsa Import(PluginCatalogKey key)
    {
        var ecdsa = ECDsa.Create();
        try
        {
            var text = key.PublicKey.Trim();
            if (text.StartsWith("-----", StringComparison.Ordinal))
            {
                ecdsa.ImportFromPem(text);
            }
            else
            {
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(text), out _);
            }

            var curve = ecdsa.ExportParameters(includePrivateParameters: false).Curve;
            if (!IsP256(curve))
            {
                throw new InvalidOperationException($"The key \"{key.Id}\" of the setting Plugins:Catalog:Keys is not a P-256 key. Use an ECDSA P-256 public key.");
            }

            return ecdsa;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException($"The key \"{key.Id}\" of the setting Plugins:Catalog:Keys is not a valid public key. Use a PEM block or the Base64 text of a SubjectPublicKeyInfo structure.", exception);
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }
    }
}
