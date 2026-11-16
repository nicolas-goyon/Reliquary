using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Platform.Extensions;

/// <summary>A file of a plugin version, before the store keeps it.</summary>
/// <param name="Path">The path relative to the root of the plugin files, for example <c>dist/index.js</c>.</param>
/// <param name="Content">The content of the file.</param>
public sealed record PluginFile(string Path, ReadOnlyMemory<byte> Content);

/// <summary>A file of a plugin version, as the store keeps it.</summary>
/// <param name="Path">The path relative to the root of the plugin files.</param>
/// <param name="ContentType">The media type that the API sends with the file.</param>
/// <param name="Integrity">The integrity value: <c>sha384-</c> and the Base64 SHA-384 hash of the content.</param>
/// <param name="Content">The content of the file.</param>
public sealed record StoredPluginFile(string Path, string ContentType, string Integrity, byte[] Content);

/// <summary>The result of <see cref="PluginFileStore.SaveVersionAsync"/>.</summary>
public enum PluginFileSaveResult
{
    /// <summary>The store keeps the files of the version.</summary>
    Saved,

    /// <summary>The store already has files for this version. The files of a version do not change.</summary>
    VersionExists,
}

/// <summary>
/// Keeps the files of each plugin version in the table <c>users.extension_files</c>.
/// The catalog sync adds the files of each new version. The API sends them to the plugin frames.
/// </summary>
/// <param name="database">The databases of the instance.</param>
/// <param name="time">The clock.</param>
public sealed partial class PluginFileStore(IDatabase database, TimeProvider time)
{
    /// <summary>The maximum total size of the files of one version: 5 MB.</summary>
    public const int MaxVersionBytes = 5 * 1024 * 1024;

    /// <summary>The maximum number of files of one version.</summary>
    public const int MaxFiles = 200;

    /// <summary>The maximum length of a file path.</summary>
    public const int MaxPathLength = 200;

    /// <summary>
    /// The pattern of a file path: segments of letters, digits, dots, hyphens and underscores, with a slash between them.
    /// A segment cannot start with a dot, so a path cannot go up a folder or name a hidden file.
    /// </summary>
    public const string PathPattern = "^[A-Za-z0-9_-][A-Za-z0-9._-]*(/[A-Za-z0-9_-][A-Za-z0-9._-]*)*$";

    /// <summary>
    /// The media type of each file name extension that a plugin can contain.
    /// The list has no HTML file: the API makes the HTML of the plugin frames.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".wasm"] = "application/wasm",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
    };

    /// <summary>
    /// Checks the files of a plugin version.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>The field errors. The list is empty when the files are valid.</returns>
    public static IReadOnlyList<FieldError> Check(IReadOnlyList<PluginFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var errors = new List<FieldError>();
        if (files.Count == 0)
        {
            errors.Add(new FieldError("files", FieldErrorCodes.Required, "A plugin version must have one file or more."));
        }
        else if (files.Count > MaxFiles)
        {
            errors.Add(new FieldError("files", FieldErrorCodes.TooLong, $"A plugin version must have {MaxFiles} files or fewer."));
        }

        if (files.Sum(file => (long)file.Content.Length) > MaxVersionBytes)
        {
            errors.Add(new FieldError("files", FieldErrorCodes.TooLong, $"The files of a plugin version must have {MaxVersionBytes} bytes (5 MB) or fewer in total."));
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < files.Count; index++)
        {
            var path = files[index].Path;
            var field = $"files[{index}].path";
            if (!IsValidPath(path))
            {
                errors.Add(new FieldError(field, FieldErrorCodes.InvalidFormat, $"The path must have {MaxPathLength} characters or fewer, with segments of letters, digits, dots, hyphens and underscores. A segment cannot start with a dot."));
            }
            else if (ContentType(path) is null)
            {
                errors.Add(new FieldError(field, FieldErrorCodes.NotAllowed, $"The file name extension is not permitted. Use one of these extensions: {string.Join(", ", ContentTypes.Keys)}."));
            }
            else if (!paths.Add(path))
            {
                errors.Add(new FieldError(field, FieldErrorCodes.NotAllowed, "Each path can occur only one time in a plugin version."));
            }
        }

        return errors;
    }

    /// <summary>
    /// Tells whether a text is a valid path of a plugin file.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> for a valid path.</returns>
    public static bool IsValidPath(string? path) => path is { Length: > 0 and <= MaxPathLength } && PathRegex().IsMatch(path);

    /// <summary>
    /// Gets the media type of a file from its file name extension.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The media type, or <see langword="null"/> when a plugin cannot contain this type of file.</returns>
    public static string? ContentType(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var dot = path.LastIndexOf('.');
        return dot < 0 || dot < path.LastIndexOf('/') ? null : ContentTypes.GetValueOrDefault(path[dot..]);
    }

    /// <summary>
    /// Calculates the integrity value of a content, for the attribute <c>integrity</c> of HTML.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns><c>sha384-</c> and the Base64 SHA-384 hash of the content.</returns>
    public static string Integrity(ReadOnlySpan<byte> content) => "sha384-" + Convert.ToBase64String(SHA384.HashData(content));

    /// <summary>
    /// Keeps the files of a plugin version. Check the files with <see cref="Check"/> first.
    /// </summary>
    /// <param name="extensionId">The ID of the plugin.</param>
    /// <param name="version">The version of the plugin.</param>
    /// <param name="files">The files of the version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see cref="PluginFileSaveResult.VersionExists"/> when the store already has files for this version.</returns>
    /// <exception cref="ArgumentException">The files are not valid.</exception>
    public async Task<PluginFileSaveResult> SaveVersionAsync(string extensionId, string version, IReadOnlyList<PluginFile> files, CancellationToken cancellationToken)
    {
        var errors = Check(files);
        if (errors.Count > 0)
        {
            throw new ArgumentException($"The files of {extensionId} {version} are not valid: {string.Join(" ", errors.Select(error => $"{error.Field}: {error.Message}"))}", nameof(files));
        }

        var now = time.GetUtcNow();
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The lock stops two catalog syncs that add the same version at the same time.
        await connection.ExecuteAsync(new CommandDefinition(
            "select pg_advisory_xact_lock(hashtext('users.extension_files'), hashtext(@key))",
            new { key = $"{extensionId}/{version}" },
            transaction,
            cancellationToken: cancellationToken));
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from users.extension_files where extension_id = @extensionId and version = @version)",
            new { extensionId, version },
            transaction,
            cancellationToken: cancellationToken));
        if (exists)
        {
            return PluginFileSaveResult.VersionExists;
        }

        foreach (var file in files)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into users.extension_files (extension_id, version, path, content, content_type, integrity, size, created_at)
                values (@extensionId, @version, @path, @content, @contentType, @integrity, @size, @now)
                """,
                new
                {
                    extensionId,
                    version,
                    path = file.Path,
                    content = file.Content.ToArray(),
                    contentType = ContentType(file.Path),
                    integrity = Integrity(file.Content.Span),
                    size = file.Content.Length,
                    now,
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return PluginFileSaveResult.Saved;
    }

    /// <summary>
    /// Gets a file of a plugin version.
    /// </summary>
    /// <returns>The file, or <see langword="null"/> when the store does not have it.</returns>
    public async Task<StoredPluginFile?> FindAsync(string extensionId, string version, string path, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<StoredPluginFile>(new CommandDefinition(
            """
            select path, content_type as ContentType, integrity, content
            from users.extension_files
            where extension_id = @extensionId and version = @version and path = @path
            """,
            new { extensionId, version, path },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Gets the integrity value of a file of a plugin version, without its content.
    /// </summary>
    /// <returns>The integrity value, or <see langword="null"/> when the store does not have the file.</returns>
    public async Task<string?> IntegrityAsync(string extensionId, string version, string path, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "select integrity from users.extension_files where extension_id = @extensionId and version = @version and path = @path",
            new { extensionId, version, path },
            cancellationToken: cancellationToken));
    }

    [GeneratedRegex(PathPattern)]
    private static partial Regex PathRegex();
}
