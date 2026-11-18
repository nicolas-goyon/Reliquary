using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Reliquary.Platform.LinkedAccounts;

namespace Reliquary.Platform.Tests.LinkedAccounts;

/// <summary>
/// The vault of the linked accounts. See docs/decisions/0050-no-plugin-code-on-the-server.md.
/// </summary>
public sealed class VaultTests
{
    private static readonly string OldKey = NewKey();

    private static readonly string NewerKey = NewKey();

    private static readonly SecretScope Scope = new(Guid.Parse("0196f3a0-0000-7000-8000-000000000001"), "deck-sync", "example.com");

    [Fact]
    public void SecretDecryptsOnlyForItsOwnerItsPluginAndItsSite()
    {
        var vault = Create(("2026-01", OldKey));

        var sealedSecret = vault.Encrypt("api-key-1", Scope);

        Assert.Equal("2026-01", sealedSecret.KeyId);
        Assert.Equal("api-key-1", vault.Decrypt(sealedSecret, Scope));
        Assert.Throws<AuthenticationTagMismatchException>(() => vault.Decrypt(sealedSecret, Scope with { OwnerId = Guid.NewGuid() }));
        Assert.Throws<AuthenticationTagMismatchException>(() => vault.Decrypt(sealedSecret, Scope with { PluginId = "other-plugin" }));
        Assert.Throws<AuthenticationTagMismatchException>(() => vault.Decrypt(sealedSecret, Scope with { Site = "example.org" }));
    }

    [Fact]
    public void CiphertextDoesNotContainTheSecretAndChangesEachTime()
    {
        var vault = Create(("2026-01", OldKey));

        var first = vault.Encrypt("api-key-1", Scope);
        var second = vault.Encrypt("api-key-1", Scope);

        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        Assert.DoesNotContain("api-key-1", System.Text.Encoding.UTF8.GetString(first.Ciphertext), StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedCiphertextDoesNotDecrypt()
    {
        var vault = Create(("2026-01", OldKey));
        var sealedSecret = vault.Encrypt("api-key-1", Scope);

        var changed = (byte[])sealedSecret.Ciphertext.Clone();
        changed[^1] ^= 1;
        var truncated = sealedSecret.Ciphertext[..10];

        Assert.Throws<AuthenticationTagMismatchException>(() => vault.Decrypt(sealedSecret with { Ciphertext = changed }, Scope));
        Assert.Throws<CryptographicException>(() => vault.Decrypt(sealedSecret with { Ciphertext = truncated }, Scope));
    }

    [Fact]
    public void RotationEncryptsWithTheNewKeyAndReadsWithTheOldKey()
    {
        var before = Create(("2026-01", OldKey));
        var oldSecret = before.Encrypt("api-key-1", Scope);

        var after = Create(("2026-01", OldKey), ("2026-02", NewerKey));
        var newSecret = after.Encrypt("api-key-2", Scope);

        Assert.Equal("2026-02", after.CurrentKeyId);
        Assert.Equal("2026-02", newSecret.KeyId);
        Assert.Equal("api-key-1", after.Decrypt(oldSecret, Scope));
        Assert.Equal("api-key-2", after.Decrypt(newSecret, Scope));
    }

    [Fact]
    public void MissingKeyGivesAClearError()
    {
        var sealedSecret = Create(("2026-01", OldKey)).Encrypt("api-key-1", Scope);
        var vault = Create(("2026-02", NewerKey));

        var exception = Assert.Throws<VaultKeyMissingException>(() => vault.Decrypt(sealedSecret, Scope));

        Assert.Equal("2026-01", exception.KeyId);
        Assert.False(vault.HasKey("2026-01"));
    }

    [Fact]
    public void KeyWithTheSameIdAndADifferentValueDoesNotDecrypt()
    {
        var sealedSecret = Create(("2026-01", OldKey)).Encrypt("api-key-1", Scope);

        Assert.Throws<AuthenticationTagMismatchException>(() => Create(("2026-01", NewerKey)).Decrypt(sealedSecret, Scope));
    }

    [Fact]
    public void VaultWithoutKeysRefusesToEncrypt()
    {
        var vault = Create();

        Assert.False(vault.IsConfigured);
        Assert.Null(vault.CurrentKeyId);
        Assert.Throws<VaultNotConfiguredException>(() => vault.Encrypt("api-key-1", Scope));
    }

    [Theory]
    [InlineData("2026-01", "c2hvcnQ=")]
    [InlineData("2026-01", "not base64")]
    [InlineData("key with spaces", "VALID")]
    [InlineData("key/slash", "VALID")]
    public void InvalidKeyStopsTheVault(string keyId, string value)
    {
        value = value == "VALID" ? OldKey : value;

        Assert.NotNull(Vault.CheckKey(keyId, value));
        Assert.Throws<InvalidOperationException>(() => Create((keyId, value)));
    }

    [Fact]
    public void VaultIgnoresAnEmptyKey()
    {
        var vault = Create(("1", OldKey), ("2", " "), ("3", string.Empty));

        Assert.Equal("1", vault.CurrentKeyId);
        Assert.False(vault.HasKey("2"));
        Assert.False(Create(("1", string.Empty)).IsConfigured);
    }

    [Fact]
    public void ValidKeyPassesTheCheck() => Assert.Null(Vault.CheckKey("2026-11_a.b", OldKey));

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(Vault.KeySize));

    private static Vault Create(params (string KeyId, string Key)[] keys)
    {
        var options = new VaultOptions();
        foreach (var (keyId, key) in keys)
        {
            options.Keys[keyId] = key;
        }

        return new Vault(Options.Create(options));
    }
}
