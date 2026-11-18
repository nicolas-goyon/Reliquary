namespace Reliquary.Platform.Extensions.Catalog;

/// <summary>
/// The catalog index: the signed JSON file <c>index.json</c> of the plugin catalog.
/// The section "Plugin catalog" of docs/architecture/extensions.md gives the format.
/// </summary>
/// <param name="Format">The version of the format. The core reads only the format 1.</param>
/// <param name="Serial">A number that increases at each publication. The instance refuses an index with a smaller serial.</param>
/// <param name="Expires">The end of the validity. The instance refuses an index after this time.</param>
/// <param name="KeyId">The ID of the key that signed the index.</param>
/// <param name="Plugins">The plugins and their versions.</param>
/// <param name="Revoked">The revoked plugin versions.</param>
public sealed record CatalogIndex(
    int Format,
    long Serial,
    DateTimeOffset Expires,
    string KeyId,
    IReadOnlyList<CatalogPlugin> Plugins,
    IReadOnlyList<CatalogRevocation> Revoked)
{
    /// <summary>The version of the format that the core reads.</summary>
    public const int CurrentFormat = 1;

    /// <summary>The path of the plugin manifest in the files of each version.</summary>
    public const string ManifestPath = "manifest.json";

    /// <summary>
    /// Tells whether the index revokes a plugin version.
    /// </summary>
    /// <param name="id">The ID of the plugin.</param>
    /// <param name="version">The version of the plugin.</param>
    /// <returns><see langword="true"/> when the version is in <see cref="Revoked"/>.</returns>
    public bool IsRevoked(string id, string version) =>
        Revoked.Any(revocation => revocation.Id == id && revocation.Version == version);
}

/// <summary>A plugin of the catalog index.</summary>
/// <param name="Id">The ID of the plugin.</param>
/// <param name="Versions">The versions that the catalog publishes.</param>
public sealed record CatalogPlugin(string Id, IReadOnlyList<CatalogVersion> Versions);

/// <summary>A plugin version of the catalog index.</summary>
/// <param name="Version">The version.</param>
/// <param name="Files">The files of the version. The file <c>manifest.json</c> is necessary.</param>
public sealed record CatalogVersion(string Version, IReadOnlyList<CatalogFile> Files);

/// <summary>A file of a plugin version.</summary>
/// <param name="Path">The path relative to the folder of the version, for example <c>dist/index.js</c>.</param>
/// <param name="Integrity">The integrity value: <c>sha384-</c> and the Base64 SHA-384 hash of the content.</param>
/// <param name="Size">The size of the content in bytes.</param>
public sealed record CatalogFile(string Path, string Integrity, long Size);

/// <summary>A revoked plugin version.</summary>
/// <param name="Id">The ID of the plugin.</param>
/// <param name="Version">The revoked version.</param>
/// <param name="Reason">The reason, for the administrators.</param>
public sealed record CatalogRevocation(string Id, string Version, string Reason);
