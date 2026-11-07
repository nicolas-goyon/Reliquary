using Dapper;
using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Logging;
using Npgsql;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Platform.Data;

namespace Reliquary.Platform.Migrations;

/// <summary>
/// Applies the migration sets to all the databases of the instance.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The sets of the <see cref="DatabaseTarget.Users"/> target go to the users database.</item>
/// <item>The sets of the <see cref="DatabaseTarget.Reference"/> target go to the reference database.</item>
/// <item>The sets of the <see cref="DatabaseTarget.Content"/> target go to each shard database.</item>
/// </list>
/// Each schema has a journal table, <c>&lt;schema&gt;.schema_migrations</c>, that lists the applied scripts.
/// A lock in each database stops two hosts that apply the migrations at the same time.
/// </remarks>
/// <param name="sets">All the migration sets.</param>
/// <param name="sources">The connection settings.</param>
/// <param name="logger">The logger.</param>
/// <param name="loggerFactory">The logger factory. DbUp writes its messages to the category <see cref="DbUpLogCategory"/>.</param>
public sealed class MigrationRunner(IEnumerable<MigrationSet> sets, DataSources sources, ILogger<MigrationRunner> logger, ILoggerFactory loggerFactory)
{
    /// <summary>The log category of the messages from DbUp.</summary>
    public const string DbUpLogCategory = "Reliquary.Platform.Migrations.DbUp";

    private readonly ILogger dbUpLogger = loggerFactory.CreateLogger(DbUpLogCategory);

    private static readonly long LockKey = StableHash.Of("reliquary.migrations");

    /// <summary>
    /// Applies all the scripts that a database does not have yet.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the operation between two databases.</param>
    /// <returns>The number of scripts that the runner applied.</returns>
    /// <exception cref="MigrationException">A script failed.</exception>
    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var applied = 0;
        applied += await MigrateDatabaseAsync("users", sources.Users, DatabaseTarget.Users, cancellationToken);
        applied += await MigrateDatabaseAsync("reference", sources.Reference, DatabaseTarget.Reference, cancellationToken);

        foreach (var (shard, connectionString) in sources.Shards)
        {
            applied += await MigrateDatabaseAsync($"shard {shard}", connectionString, DatabaseTarget.Content, cancellationToken);
        }

        return applied;
    }

    /// <summary>
    /// Gets the scripts that a database does not have yet, without a change to the database.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The name of each database and the names of its pending scripts.</returns>
    public async Task<IReadOnlyList<(string Database, string Script)>> PendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = new List<(string, string)>();
        var databases = new List<(string Name, string ConnectionString, DatabaseTarget Target)>
        {
            ("users", sources.Users, DatabaseTarget.Users),
            ("reference", sources.Reference, DatabaseTarget.Reference),
        };
        databases.AddRange(sources.Shards.Select(shard => ($"shard {shard.Key}", shard.Value, DatabaseTarget.Content)));

        foreach (var (name, connectionString, target) in databases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var set in SetsFor(target))
            {
                await EnsureSchemaAsync(connectionString, set.Schema, cancellationToken);
                var engine = Engine(connectionString, set);
                pending.AddRange(engine.GetScriptsToExecute().Select(script => (name, script.Name)));
            }
        }

        return pending;
    }

    private async Task<int> MigrateDatabaseAsync(string name, string connectionString, DatabaseTarget target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);
        await lockConnection.ExecuteAsync(new CommandDefinition("select pg_advisory_lock(@key)", new { key = LockKey }, cancellationToken: cancellationToken));

        try
        {
            var applied = 0;
            foreach (var set in SetsFor(target))
            {
                await EnsureSchemaAsync(connectionString, set.Schema, cancellationToken);
                var result = Engine(connectionString, set).PerformUpgrade();
                if (!result.Successful)
                {
                    throw new MigrationException(name, result.ErrorScript?.Name, result.Error);
                }

                var count = result.Scripts.Count();
                applied += count;
                if (count > 0)
                {
                    logger.MigrationSetApplied(count, set.Owner, set.Schema, name);
                }
            }

            return applied;
        }
        finally
        {
            await lockConnection.ExecuteAsync(new CommandDefinition("select pg_advisory_unlock(@key)", new { key = LockKey }, cancellationToken: CancellationToken.None));
        }
    }

    private IEnumerable<MigrationSet> SetsFor(DatabaseTarget target) =>
        sets
            .Where(set => set.Target == target)
            .OrderBy(set => set.Owner == PlatformMigrations.Owner ? 0 : 1);

    private UpgradeEngine Engine(string connectionString, MigrationSet set) =>
        DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScripts(set.ReadScripts().Select(script => new SqlScript(script.Name, script.Contents)))
            .JournalToPostgresqlTable(set.Schema, "schema_migrations")
            .WithTransactionPerScript()
            .WithVariablesDisabled()
            .LogTo(dbUpLogger)
            .Build();

    private static async Task EnsureSchemaAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        if (!SchemaName.IsValid(schema))
        {
            throw new InvalidOperationException($"The schema name \"{schema}\" is not valid. Use lowercase letters, digits and underscores.");
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition($"create schema if not exists {schema}", cancellationToken: cancellationToken));
    }
}

/// <summary>
/// A migration script failed.
/// </summary>
public sealed class MigrationException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="database">The name of the database.</param>
    /// <param name="script">The name of the script that failed.</param>
    /// <param name="innerException">The error of the script.</param>
    public MigrationException(string database, string? script, Exception? innerException)
        : base($"The migration of the database {database} failed at the script {script ?? "(unknown)"}. The database contains the scripts before this script.", innerException)
    {
        Database = database;
        Script = script;
    }

    /// <summary>Gets the name of the database.</summary>
    public string Database { get; }

    /// <summary>Gets the name of the script that failed.</summary>
    public string? Script { get; }
}
