using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Reliquary.Platform.Extensions.Catalog;

/// <summary>The result status of <see cref="CatalogIndexCheck.Check"/>.</summary>
public enum CatalogIndexStatus
{
    /// <summary>The index is correct and newer than the last index of the instance.</summary>
    Valid,

    /// <summary>The index is correct and has the same bytes as the last index of the instance.</summary>
    Unchanged,

    /// <summary>The index or its signature is larger than the limit.</summary>
    TooLarge,

    /// <summary>No trusted key made the signature, or the field <c>keyId</c> does not name the key that made it.</summary>
    BadSignature,

    /// <summary>The signature is correct, but the content does not follow the format of the index.</summary>
    Malformed,

    /// <summary>The serial is smaller than the serial of the last index, or the serial is the same with different bytes.</summary>
    Rollback,

    /// <summary>The date <c>expires</c> is in the past.</summary>
    Expired,
}

/// <summary>The last catalog index that the instance accepted.</summary>
/// <param name="Serial">The serial of the index.</param>
/// <param name="Sha256">The SHA-256 hash of the bytes of the index.</param>
public sealed record CatalogIndexState(long Serial, byte[] Sha256);

/// <summary>The result of the check of a catalog index.</summary>
/// <param name="Status">The status.</param>
/// <param name="Index">The index, for the status <see cref="CatalogIndexStatus.Valid"/> and <see cref="CatalogIndexStatus.Unchanged"/>.</param>
/// <param name="Message">The reason of a refusal, in the strict mode of the writing rules. Empty for a valid index.</param>
public sealed record CatalogIndexCheckResult(CatalogIndexStatus Status, CatalogIndex? Index, string Message)
{
    /// <summary>Gets whether the instance can use the index.</summary>
    public bool Accepted => Status is CatalogIndexStatus.Valid or CatalogIndexStatus.Unchanged;
}

/// <summary>
/// Checks a catalog index and its signature. See docs/decisions/0052-plugin-catalog-signature-and-revocation.md.
/// </summary>
/// <remarks>
/// The signature covers the exact bytes of <c>index.json</c>, so the check does not serialize the JSON again.
/// The check verifies the signature before it reads the JSON.
/// </remarks>
public static partial class CatalogIndexCheck
{
    /// <summary>The maximum size of the index: 1 MB.</summary>
    public const int MaxIndexBytes = 1024 * 1024;

    /// <summary>The maximum size of the signature. A DER signature of P-256 has 72 bytes or fewer.</summary>
    public const int MaxSignatureBytes = 256;

    /// <summary>The maximum length of the reason of a revocation.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>The pattern of an integrity value: <c>sha384-</c> and the Base64 text of 48 bytes.</summary>
    public const string IntegrityPattern = "^sha384-[A-Za-z0-9+/]{64}$";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Checks a catalog index.
    /// </summary>
    /// <param name="index">The bytes of <c>index.json</c>.</param>
    /// <param name="signature">The bytes of <c>index.json.sig</c>: a DER ECDSA signature with SHA-256.</param>
    /// <param name="keys">The trusted keys.</param>
    /// <param name="last">The last index that the instance accepted, or <see langword="null"/>.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The result.</returns>
    public static CatalogIndexCheckResult Check(ReadOnlySpan<byte> index, ReadOnlySpan<byte> signature, CatalogKeyRing keys, CatalogIndexState? last, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (index.Length > MaxIndexBytes || signature.Length > MaxSignatureBytes)
        {
            return Refuse(CatalogIndexStatus.TooLarge, $"The catalog index has more than {MaxIndexBytes} bytes, or its signature has more than {MaxSignatureBytes} bytes.");
        }

        if (keys.FindSigner(index, signature) is not { } signer)
        {
            return Refuse(CatalogIndexStatus.BadSignature, "No trusted key made the signature of the catalog index.");
        }

        CatalogIndex? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CatalogIndex>(index, Json);
        }
        catch (JsonException exception)
        {
            return Refuse(CatalogIndexStatus.Malformed, $"The catalog index is not valid JSON of the format {CatalogIndex.CurrentFormat}: {exception.Message}");
        }

        if (parsed is null)
        {
            return Refuse(CatalogIndexStatus.Malformed, "The catalog index is empty.");
        }

        if (parsed.KeyId != signer)
        {
            return Refuse(CatalogIndexStatus.BadSignature, $"The key \"{signer}\" made the signature, but the field keyId of the catalog index is \"{parsed.KeyId}\".");
        }

        if (Problem(parsed) is { } problem)
        {
            return Refuse(CatalogIndexStatus.Malformed, problem);
        }

        var unchanged = false;
        if (last is not null)
        {
            if (parsed.Serial < last.Serial)
            {
                return Refuse(CatalogIndexStatus.Rollback, $"The catalog index has the serial {parsed.Serial}, but the instance has the serial {last.Serial}. The instance refuses an older index.");
            }

            if (parsed.Serial == last.Serial)
            {
                if (!SHA256.HashData(index).AsSpan().SequenceEqual(last.Sha256))
                {
                    return Refuse(CatalogIndexStatus.Rollback, $"The catalog index has the serial {parsed.Serial} of the last index, but different bytes. A new index must have a larger serial.");
                }

                unchanged = true;
            }
        }

        if (parsed.Expires <= now)
        {
            return Refuse(CatalogIndexStatus.Expired, $"The catalog index expired at {parsed.Expires:O}. The catalog must sign the index again.");
        }

        return new CatalogIndexCheckResult(unchanged ? CatalogIndexStatus.Unchanged : CatalogIndexStatus.Valid, parsed, string.Empty);
    }

    /// <summary>Finds the first problem of the content of an index.</summary>
    /// <returns>The problem, or <see langword="null"/> when the content is correct.</returns>
    private static string? Problem(CatalogIndex index)
    {
        if (index.Format != CatalogIndex.CurrentFormat)
        {
            return $"The catalog index has the format {index.Format}. This core reads only the format {CatalogIndex.CurrentFormat}.";
        }

        if (index.Serial < 1)
        {
            return "The serial of the catalog index must be 1 or more.";
        }

        var plugins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in index.Plugins)
        {
            if (plugin is null || !IdRegex().IsMatch(plugin.Id) || !plugins.Add(plugin.Id))
            {
                return $"The plugin \"{plugin?.Id}\" has an incorrect ID, or the catalog index has this ID two times.";
            }

            var versions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var version in plugin.Versions)
            {
                if (version is null || !VersionRegex().IsMatch(version.Version) || !versions.Add(version.Version))
                {
                    return $"The plugin \"{plugin.Id}\" has an incorrect version \"{version?.Version}\", or it has this version two times.";
                }

                if (FilesProblem(version.Files) is { } files)
                {
                    return $"The files of {plugin.Id} {version.Version} are not correct. {files}";
                }
            }
        }

        var revoked = new HashSet<(string, string)>();
        foreach (var revocation in index.Revoked)
        {
            if (revocation is null
                || !IdRegex().IsMatch(revocation.Id)
                || !VersionRegex().IsMatch(revocation.Version)
                || revocation.Reason.Length is 0 or > MaxReasonLength
                || !revoked.Add((revocation.Id, revocation.Version)))
            {
                return $"The revocation of \"{revocation?.Id}\" \"{revocation?.Version}\" is not correct. It needs a plugin ID, a version and a reason of {MaxReasonLength} characters or fewer, one time only.";
            }
        }

        return null;
    }

    private static string? FilesProblem(IReadOnlyList<CatalogFile> files)
    {
        if (files.Count is 0 or > PluginFileStore.MaxFiles)
        {
            return $"A version must have 1 to {PluginFileStore.MaxFiles} files.";
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in files)
        {
            if (file is null || !PluginFileStore.IsValidPath(file.Path) || PluginFileStore.ContentType(file.Path) is null || !paths.Add(file.Path))
            {
                return $"The path \"{file?.Path}\" is not a valid path of a plugin file, or it occurs two times.";
            }

            if (!IntegrityRegex().IsMatch(file.Integrity))
            {
                return $"The integrity value of \"{file.Path}\" is not a SHA-384 integrity value.";
            }

            if (file.Size < 0 || file.Size > PluginFileStore.MaxVersionBytes)
            {
                return $"The size of \"{file.Path}\" is not correct.";
            }

            total += file.Size;
        }

        if (total > PluginFileStore.MaxVersionBytes)
        {
            return $"The files of a version must have {PluginFileStore.MaxVersionBytes} bytes or fewer in total.";
        }

        return paths.Contains(CatalogIndex.ManifestPath) ? null : $"A version must have the file {CatalogIndex.ManifestPath}.";
    }

    private static CatalogIndexCheckResult Refuse(CatalogIndexStatus status, string message) => new(status, null, message);

    [GeneratedRegex(PluginManifestRules.IdPattern)]
    private static partial Regex IdRegex();

    [GeneratedRegex(PluginManifestRules.VersionPattern)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(IntegrityPattern)]
    private static partial Regex IntegrityRegex();
}
