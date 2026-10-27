using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using Reliquary.Platform.Data;

namespace Reliquary.Platform.Backup;

/// <summary>
/// The settings of the backup tools.
/// </summary>
public sealed class BackupOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Backup";

    /// <summary>
    /// Gets or sets the folder of <c>pg_dump</c> and <c>pg_restore</c>, or an empty text to find them in the PATH.
    /// The major version of the tools must be the version of the server or a more recent version.
    /// </summary>
    public string ToolsDirectory { get; set; } = string.Empty;
}

/// <summary>
/// One database of a backup.
/// </summary>
/// <param name="File">The name of the dump file in the backup folder.</param>
/// <param name="Roles">The roles of the database in the instance: <c>users</c>, <c>reference</c> and <c>shard:NAME</c>.</param>
public sealed record BackupDatabase(string File, IReadOnlyList<string> Roles);

/// <summary>
/// The description of a backup, in the file <c>manifest.json</c> of the backup folder.
/// </summary>
/// <param name="Format">The version of the backup format.</param>
/// <param name="CreatedAt">The time of the backup.</param>
/// <param name="Databases">The databases.</param>
public sealed record BackupManifest(int Format, DateTimeOffset CreatedAt, IReadOnlyList<BackupDatabase> Databases);

/// <summary>
/// Backs up and restores all the databases of an instance: the users database, the reference database and each shard.
/// See docs/development/backup-and-restore.md.
/// </summary>
/// <remarks>
/// The class runs <c>pg_dump</c> and <c>pg_restore</c>. Several roles can use the same database, so the backup contains each database one time.
/// Redis contains only caches and counters that the worker copies to PostgreSQL, so the backup does not contain Redis.
/// </remarks>
public sealed class InstanceBackup(DataSources sources, IOptions<BackupOptions> options, TimeProvider time)
{
    /// <summary>The name of the description file of a backup.</summary>
    public const string ManifestFile = "manifest.json";

    private const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Writes a backup of all the databases to a folder. The folder must be empty or not exist.
    /// </summary>
    /// <param name="directory">The folder of the backup.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The description of the backup.</returns>
    /// <exception cref="InvalidOperationException">The folder is not empty, or a tool failed.</exception>
    public async Task<BackupManifest> BackupAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        if (Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new InvalidOperationException($"The folder {directory} is not empty. Give an empty folder for the backup.");
        }

        var databases = new List<BackupDatabase>();
        var number = 0;
        foreach (var (connectionString, roles) in Databases())
        {
            number++;
            var file = $"database-{number}.dump";
            await RunAsync("pg_dump", connectionString, ["--format=custom", "--no-owner", "--no-privileges", $"--file={Path.Combine(directory, file)}"], cancellationToken);
            databases.Add(new BackupDatabase(file, roles));
        }

        var manifest = new BackupManifest(CurrentFormat, time.GetUtcNow(), databases);
        await File.WriteAllTextAsync(Path.Combine(directory, ManifestFile), JsonSerializer.Serialize(manifest, Json), cancellationToken);
        return manifest;
    }

    /// <summary>
    /// Restores a backup into the databases of the instance. Each role of the backup goes to the database of the same role.
    /// </summary>
    /// <param name="directory">The folder of the backup.</param>
    /// <param name="replace">
    /// <see langword="true"/> to replace the data of an instance that has data.
    /// Without it, the method refuses to restore into an instance with accounts or shard assignments.
    /// </param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The description of the restored backup.</returns>
    /// <exception cref="InvalidOperationException">The backup is not valid, the instance has data, or a tool failed.</exception>
    public async Task<BackupManifest> RestoreAsync(string directory, bool replace, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(directory, ManifestFile);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"The folder {directory} has no file {ManifestFile}. Give the folder of a backup.");
        }

        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), Json)
            ?? throw new InvalidOperationException($"The file {ManifestFile} is empty.");
        if (manifest.Format != CurrentFormat)
        {
            throw new InvalidOperationException($"The backup has the format {manifest.Format}. This version of Reliquary reads the format {CurrentFormat} only.");
        }

        if (!replace && await HasDataAsync(cancellationToken))
        {
            throw new InvalidOperationException("The instance has data. Use --replace to replace its data with the backup.");
        }

        var targets = Databases().SelectMany(database => database.Roles.Select(role => (role, database.ConnectionString))).ToDictionary(pair => pair.role, pair => pair.ConnectionString, StringComparer.Ordinal);
        foreach (var database in manifest.Databases)
        {
            var role = database.Roles.FirstOrDefault(targets.ContainsKey)
                ?? throw new InvalidOperationException($"The configuration has no database for the roles {string.Join(", ", database.Roles)}. Add the shards of the backup to Database:Shards.");
            var file = Path.Combine(directory, Path.GetFileName(database.File));
            await RunAsync("pg_restore", targets[role], ["--clean", "--if-exists", "--no-owner", "--no-privileges", "--single-transaction", file], cancellationToken);
        }

        return manifest;
    }

    /// <summary>
    /// Gets each distinct database of the instance with its roles.
    /// </summary>
    private List<(string ConnectionString, List<string> Roles)> Databases()
    {
        var databases = new List<(string ConnectionString, List<string> Roles)>();
        void Add(string connectionString, string role)
        {
            var key = Key(connectionString);
            var existing = databases.FindIndex(database => Key(database.ConnectionString) == key);
            if (existing >= 0)
            {
                databases[existing].Roles.Add(role);
            }
            else
            {
                databases.Add((connectionString, [role]));
            }
        }

        Add(sources.Users, "users");
        Add(sources.Reference, "reference");
        foreach (var (name, connectionString) in sources.Shards.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Add(connectionString, $"shard:{name}");
        }

        return databases;
    }

    private static string Key(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Host}:{builder.Port}/{builder.Database}";
    }

    private async Task<bool> HasDataAsync(CancellationToken cancellationToken)
    {
        await using var connection = await sources.Pool(sources.Users).OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "select to_regclass('users.shard_directory') is not null and exists (select 1 from users.shard_directory)",
            connection);
        try
        {
            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return false;
        }
    }

    private async Task RunAsync(string tool, string connectionString, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var start = new ProcessStartInfo
        {
            FileName = options.Value.ToolsDirectory is { Length: > 0 } folder ? Path.Combine(folder, tool) : tool,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add($"--host={builder.Host}");
        start.ArgumentList.Add($"--port={builder.Port}");
        start.ArgumentList.Add($"--username={builder.Username}");
        start.ArgumentList.Add($"--dbname={builder.Database}");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The password goes in the environment of the tool, never in its arguments.
        start.Environment["PGPASSWORD"] = builder.Password ?? string.Empty;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"The tool {tool} did not start.");
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The tool {tool} failed with the exit code {process.ExitCode} on the database {builder.Database}: {(await errors).Trim()}");
        }
    }
}
