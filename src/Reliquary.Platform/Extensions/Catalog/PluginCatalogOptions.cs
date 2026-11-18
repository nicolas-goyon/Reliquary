namespace Reliquary.Platform.Extensions.Catalog;

/// <summary>
/// The settings of the plugin catalog. The section is <c>Plugins:Catalog</c>.
/// See docs/decisions/0052-plugin-catalog-signature-and-revocation.md.
/// </summary>
public sealed class PluginCatalogOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Plugins:Catalog";

    /// <summary>
    /// Gets or sets the address of the catalog index, for example <c>https://example.org/catalog/index.json</c>.
    /// The signature is at the same address with <c>.sig</c> at the end.
    /// Without a value, the instance does not sync a plugin catalog.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>Gets or sets the minimum time between two syncs of the catalog. The default is one hour.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the public keys that can sign the catalog index.
    /// Without a value, the instance trusts the keys of <see cref="ProjectKeys"/>.
    /// </summary>
    public List<PluginCatalogKey> Keys { get; set; } = [];

    /// <summary>
    /// Gets the keys of the project catalog, the default value of <see cref="Keys"/>.
    /// </summary>
    /// <remarks>
    /// The project catalog (issue #299) does not have a signing key yet, so the list is empty.
    /// Until the project adds its key here, an instance needs the setting <c>Plugins:Catalog:Keys</c> to sync a catalog.
    /// </remarks>
    public static IReadOnlyList<PluginCatalogKey> ProjectKeys { get; } = [];

    /// <summary>Gets the keys that the instance trusts: <see cref="Keys"/>, or <see cref="ProjectKeys"/> when <see cref="Keys"/> is empty.</summary>
    public IReadOnlyList<PluginCatalogKey> TrustedKeys => Keys.Count > 0 ? Keys : ProjectKeys;
}

/// <summary>
/// A public key that can sign the catalog index.
/// </summary>
public sealed class PluginCatalogKey
{
    /// <summary>Gets or sets the ID of the key. The field <c>keyId</c> of the catalog index gives the ID of the key that signed it.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ECDSA P-256 public key: a PEM block <c>-----BEGIN PUBLIC KEY-----</c>,
    /// or the Base64 text of the DER SubjectPublicKeyInfo structure.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;
}
