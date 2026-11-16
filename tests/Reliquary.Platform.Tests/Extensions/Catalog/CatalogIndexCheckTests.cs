using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reliquary.Platform.Extensions.Catalog;

namespace Reliquary.Platform.Tests.Extensions.Catalog;

public sealed class CatalogIndexCheckTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly TestCatalog catalog = new();

    public CatalogIndexCheckTests() => catalog.AddVersion("hello", "1.0.0");

    public void Dispose() => catalog.Dispose();

    [Fact]
    public void CorrectIndexIsValid()
    {
        catalog.Revoked.Add(new CatalogRevocation("hello", "0.9.0", "The version sends data to an unknown site."));

        var result = Check(catalog.Index());

        Assert.Equal(CatalogIndexStatus.Valid, result.Status);
        var index = result.Index!;
        Assert.Equal(1, index.Serial);
        Assert.Equal("test-key", index.KeyId);
        var version = Assert.Single(Assert.Single(index.Plugins).Versions);
        Assert.Equal(["manifest.json", "dist/index.js"], version.Files.Select(file => file.Path));
        Assert.True(index.IsRevoked("hello", "0.9.0"));
        Assert.False(index.IsRevoked("hello", "1.0.0"));
    }

    [Fact]
    public void ChangedByteMakesTheSignatureBad()
    {
        var index = catalog.Index();
        var signature = catalog.Sign(index);
        index[^2] ^= 0x01;

        Assert.Equal(CatalogIndexStatus.BadSignature, Check(index, signature).Status);
    }

    [Fact]
    public void KeyThatTheInstanceDoesNotTrustIsRefused()
    {
        using var other = new TestCatalog("test-key");
        using var keys = CatalogKeyRing.Create([other.PublicKey]);
        var index = catalog.Index();

        var result = CatalogIndexCheck.Check(index, catalog.Sign(index), keys, null, Now);

        Assert.Equal(CatalogIndexStatus.BadSignature, result.Status);
    }

    [Fact]
    public void KeyIdMustNameTheKeyThatSigned()
    {
        using var other = new TestCatalog("other-key");
        using var keys = CatalogKeyRing.Create([other.PublicKey, catalog.PublicKey]);
        var index = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(catalog.Index()).Replace("\"test-key\"", "\"other-key\"", StringComparison.Ordinal));

        var result = CatalogIndexCheck.Check(index, catalog.Sign(index), keys, null, Now);

        Assert.Equal(CatalogIndexStatus.BadSignature, result.Status);
    }

    [Fact]
    public void SecondKeyOfTheRingCanSign()
    {
        using var old = new TestCatalog("old-key");
        using var keys = CatalogKeyRing.Create([old.PublicKey, catalog.PublicKey]);
        var index = catalog.Index();

        var result = CatalogIndexCheck.Check(index, catalog.Sign(index), keys, null, Now);

        Assert.Equal(CatalogIndexStatus.Valid, result.Status);
    }

    [Fact]
    public void SmallerSerialIsARollback()
    {
        catalog.Serial = 4;

        var result = Check(catalog.Index(), last: new CatalogIndexState(5, new byte[32]));

        Assert.Equal(CatalogIndexStatus.Rollback, result.Status);
        Assert.Null(result.Index);
    }

    [Fact]
    public void SameSerialWithSameBytesIsUnchanged()
    {
        catalog.Serial = 5;
        var index = catalog.Index();

        var result = Check(index, last: new CatalogIndexState(5, SHA256.HashData(index)));

        Assert.Equal(CatalogIndexStatus.Unchanged, result.Status);
        Assert.True(result.Accepted);
    }

    [Fact]
    public void SameSerialWithDifferentBytesIsARollback()
    {
        catalog.Serial = 5;
        var previous = catalog.Index();
        catalog.AddVersion("hello", "1.1.0");

        var result = Check(catalog.Index(), last: new CatalogIndexState(5, SHA256.HashData(previous)));

        Assert.Equal(CatalogIndexStatus.Rollback, result.Status);
    }

    [Fact]
    public void LargerSerialIsValid()
    {
        catalog.Serial = 6;

        Assert.Equal(CatalogIndexStatus.Valid, Check(catalog.Index(), last: new CatalogIndexState(5, new byte[32])).Status);
    }

    [Fact]
    public void PastExpiryDateIsRefused()
    {
        catalog.Expires = Now;

        Assert.Equal(CatalogIndexStatus.Expired, Check(catalog.Index()).Status);
    }

    [Fact]
    public void TooLargeIndexIsRefusedBeforeTheSignatureCheck()
    {
        var index = new byte[CatalogIndexCheck.MaxIndexBytes + 1];

        Assert.Equal(CatalogIndexStatus.TooLarge, Check(index).Status);
    }

    public static TheoryData<string> IncorrectContents() => new()
    {
        """{"format":2}""",
        """{"unknown":true}""",
        """{"serial":0}""",
        """{"serial":"7"}""",
        """{"keyId":null}""",
        """{"plugins":null}""",
        """{"plugins":[{"id":"Hello","versions":[]}]}""",
        """{"plugins":[{"id":"hello","versions":[{"version":"1.0","files":[]}]}]}""",
        """{"plugins":[{"id":"hello","versions":[{"version":"1.0.0","files":[{"path":"dist/index.js","integrity":"sha384-AAAA","size":1}]}]}]}""",
        """{"plugins":[{"id":"hello","versions":[{"version":"1.0.0","files":[{"path":"../index.js","integrity":"sha384-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","size":1}]}]}]}""",
        """{"plugins":[{"id":"hello","versions":[{"version":"1.0.0","files":[{"path":"index.html","integrity":"sha384-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","size":1}]}]}]}""",
        """{"plugins":[{"id":"hello","versions":[{"version":"1.0.0","files":[{"path":"dist/index.js","integrity":"sha384-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","size":1}]}]}]}""",
        """{"revoked":[{"id":"hello","version":"1.0.0","reason":""}]}""",
        """{"revoked":[{"id":"hello","version":"1.0.0"}]}""",
    };

    [Theory]
    [MemberData(nameof(IncorrectContents))]
    public void SignedIndexWithIncorrectContentIsMalformed(string change)
    {
        var index = catalog.IndexObject();
        using (var document = JsonDocument.Parse(change))
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                index[property.Name] = property.Value.Clone();
            }
        }

        var result = Check(JsonSerializer.SerializeToUtf8Bytes(index));

        Assert.Equal(CatalogIndexStatus.Malformed, result.Status);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public void DuplicatePropertyIsMalformed()
    {
        var text = Encoding.UTF8.GetString(catalog.Index());
        var index = Encoding.UTF8.GetBytes(text.Replace("\"serial\":1", "\"serial\":1,\"serial\":9", StringComparison.Ordinal));

        Assert.Equal(CatalogIndexStatus.Malformed, Check(index).Status);
    }

    [Fact]
    public void SameRevocationTwoTimesIsMalformed()
    {
        catalog.Revoked.Add(new CatalogRevocation("hello", "0.9.0", "Reason."));
        catalog.Revoked.Add(new CatalogRevocation("hello", "0.9.0", "Reason."));

        Assert.Equal(CatalogIndexStatus.Malformed, Check(catalog.Index()).Status);
    }

    [Fact]
    public void KeyOfAnotherCurveIsRefused()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var key = new PluginCatalogKey { Id = "p384", PublicKey = Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()) };

        var exception = Assert.Throws<InvalidOperationException>(() => CatalogKeyRing.Create([key]));

        Assert.Contains("P-256", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "MFkw")]
    [InlineData("key", "not base64")]
    [InlineData("key", "-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----")]
    public void IncorrectKeySettingIsRefused(string id, string publicKey) =>
        Assert.Throws<InvalidOperationException>(() => CatalogKeyRing.Create([new PluginCatalogKey { Id = id, PublicKey = publicKey }]));

    [Fact]
    public void TwoKeysWithTheSameIdAreRefused() =>
        Assert.Throws<InvalidOperationException>(() => CatalogKeyRing.Create([catalog.PublicKey, catalog.PublicKey]));

    [Fact]
    public void ProjectKeysAreTheDefault()
    {
        var options = new PluginCatalogOptions();

        Assert.Same(PluginCatalogOptions.ProjectKeys, options.TrustedKeys);

        options.Keys.Add(catalog.PublicKey);
        Assert.Equal("test-key", Assert.Single(options.TrustedKeys).Id);
    }

    /// <summary>
    /// The test vectors come from openssl 3, so the check reads the signatures of the catalog CI.
    /// These commands made them (the private key is not in the repository):
    /// <code>
    /// openssl ecparam -name prime256v1 -genkey -noout -out test-key.pem
    /// openssl ec -in test-key.pem -pubout -out test-key.pub.pem
    /// openssl dgst -sha256 -sign test-key.pem -out index.json.sig index.json
    /// </code>
    /// </summary>
    [Fact]
    public void SignatureOfOpensslIsValid()
    {
        using var keys = CatalogKeyRing.Create([new PluginCatalogKey { Id = "openssl-test", PublicKey = Vector("test-key.pub.pem") }]);
        var index = VectorBytes("index.json");
        var signature = VectorBytes("index.json.sig");

        var result = CatalogIndexCheck.Check(index, signature, keys, null, Now);
        index[10] ^= 0x01;
        var changed = CatalogIndexCheck.Check(index, signature, keys, null, Now);

        Assert.Equal(CatalogIndexStatus.Valid, result.Status);
        Assert.Equal(3, result.Index!.Serial);
        Assert.Equal(2, Assert.Single(Assert.Single(result.Index.Plugins).Versions).Files.Count);
        Assert.True(result.Index.IsRevoked("hello", "0.9.0"));
        Assert.Equal(CatalogIndexStatus.BadSignature, changed.Status);
    }

    private static string Vector(string name) => Encoding.UTF8.GetString(VectorBytes(name));

    private static byte[] VectorBytes(string name)
    {
        using var stream = typeof(CatalogIndexCheckTests).Assembly.GetManifestResourceStream($"CatalogVectors/{name}")
            ?? throw new InvalidOperationException($"The test vector {name} is not an embedded resource.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private CatalogIndexCheckResult Check(byte[] index, byte[]? signature = null, CatalogIndexState? last = null)
    {
        using var keys = CatalogKeyRing.Create([catalog.PublicKey]);
        return CatalogIndexCheck.Check(index, signature ?? catalog.Sign(index), keys, last, Now);
    }
}
