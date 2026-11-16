using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reliquary.Platform.Extensions;
using Reliquary.Platform.Extensions.Catalog;

namespace Reliquary.Platform.Tests.Extensions.Catalog;

/// <summary>
/// A plugin catalog for the tests, signed with a key that the test makes.
/// TEST KEY ONLY: the private key exists only in the memory of the test process.
/// </summary>
internal sealed class TestCatalog : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public TestCatalog(string keyId = "test-key") => KeyId = keyId;

    public string KeyId { get; }

    public long Serial { get; set; } = 1;

    public DateTimeOffset Expires { get; set; } = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Gets the plugin versions: the ID, the version and the files.</summary>
    public List<(string Id, string Version, Dictionary<string, byte[]> Files)> Versions { get; } = [];

    public List<CatalogRevocation> Revoked { get; } = [];

    /// <summary>Gets the setting of the public key, as the Base64 text of the SubjectPublicKeyInfo structure.</summary>
    public PluginCatalogKey PublicKey => new() { Id = KeyId, PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) };

    /// <summary>
    /// Makes the manifest of a test plugin.
    /// </summary>
    public static byte[] Manifest(string id, string version, params (string Key, object? Value)[] fields)
    {
        var manifest = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["name"] = "Test plugin",
            ["version"] = version,
            ["coreApi"] = "^1.0",
            ["entry"] = "dist/index.js",
        };
        foreach (var (field, value) in fields)
        {
            manifest[field] = value;
        }

        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }

    /// <summary>
    /// Adds a plugin version with a manifest and an entry file.
    /// </summary>
    public Dictionary<string, byte[]> AddVersion(string id, string version, params (string Key, object? Value)[] fields)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CatalogIndex.ManifestPath] = Manifest(id, version, fields),
            ["dist/index.js"] = Encoding.UTF8.GetBytes($"console.log('{id} {version}');\n"),
        };
        Versions.Add((id, version, files));
        return files;
    }

    /// <summary>Makes the JSON object of the index.</summary>
    public Dictionary<string, object?> IndexObject() => new(StringComparer.Ordinal)
    {
        ["format"] = 1,
        ["serial"] = Serial,
        ["expires"] = Expires,
        ["keyId"] = KeyId,
        ["plugins"] = Versions
            .GroupBy(item => item.Id)
            .Select(plugin => new
            {
                id = plugin.Key,
                versions = plugin.Select(item => new
                {
                    version = item.Version,
                    files = item.Files.Select(file => new { path = file.Key, integrity = PluginFileStore.Integrity(file.Value), size = file.Value.Length }).ToList(),
                }).ToList(),
            })
            .ToList(),
        ["revoked"] = Revoked.Select(item => new { id = item.Id, version = item.Version, reason = item.Reason }).ToList(),
    };

    /// <summary>Makes the bytes of <c>index.json</c>.</summary>
    public byte[] Index() => JsonSerializer.SerializeToUtf8Bytes(IndexObject());

    /// <summary>Signs bytes with the test key, in the DER format of openssl.</summary>
    public byte[] Sign(byte[] data) => key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    public void Dispose() => key.Dispose();
}
