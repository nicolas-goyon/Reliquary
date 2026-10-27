using Dapper;
using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Platform.Backup;

namespace Reliquary.Platform.Tests;

public sealed class BackupTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RestoredInstanceGivesTheSameData()
    {
        await using var host = await PlatformHost.CreateAsync(databaseCount: 2);
        var owners = new[] { Ids.New(), Ids.New() };
        await AssignAsync(host, owners);
        await ExecuteAsync(host.Databases[1].ConnectionString, "create table content.backup_items (id uuid primary key, name text not null); insert into content.backup_items values (gen_random_uuid(), 'Sol Ring')");
        var before = await SnapshotAsync(host);
        var folder = Path.Combine(Path.GetTempPath(), $"reliquary-backup-{Guid.NewGuid():N}");
        try
        {
            var manifest = await host.Get<InstanceBackup>().BackupAsync(folder, Cancel);
            await ExecuteAsync(host.Databases[0].ConnectionString, "drop schema users cascade; drop schema content cascade");
            await ExecuteAsync(host.Databases[1].ConnectionString, "drop schema content cascade");

            await host.Get<InstanceBackup>().RestoreAsync(folder, replace: false, Cancel);

            Assert.Equal(2, manifest.Databases.Count);
            Assert.Equal(["users", "reference", "shard:main"], manifest.Databases[0].Roles);
            Assert.Equal(["shard:shard2"], manifest.Databases[1].Roles);
            Assert.Equal(before, await SnapshotAsync(host));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreRefusesAnInstanceWithDataAndBackupRefusesAFullFolder()
    {
        await using var host = await PlatformHost.CreateAsync();
        await AssignAsync(host, [Ids.New()]);
        var folder = Path.Combine(Path.GetTempPath(), $"reliquary-backup-{Guid.NewGuid():N}");
        try
        {
            await host.Get<InstanceBackup>().BackupAsync(folder, Cancel);

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<InstanceBackup>().RestoreAsync(folder, replace: false, Cancel));
            await host.Get<InstanceBackup>().RestoreAsync(folder, replace: true, Cancel);
            var full = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<InstanceBackup>().BackupAsync(folder, Cancel));

            Assert.Contains("--replace", refused.Message, StringComparison.Ordinal);
            Assert.Contains("not empty", full.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task AssignAsync(PlatformHost host, IEnumerable<Guid> owners)
    {
        await using var connection = await host.Get<IDatabase>().OpenUsersAsync(Cancel);
        await using var transaction = await connection.BeginTransactionAsync(Cancel);
        foreach (var owner in owners)
        {
            await host.Get<IShardDirectory>().AssignAsync(owner, transaction, Cancel);
        }

        await transaction.CommitAsync(Cancel);
    }

    private static async Task<string> SnapshotAsync(PlatformHost host)
    {
        await using var users = new NpgsqlConnection(host.Databases[0].ConnectionString);
        await using var shard = new NpgsqlConnection(host.Databases[1].ConnectionString);
        var directory = await users.QueryAsync<string>("select owner_id::text || ':' || shard from users.shard_directory order by owner_id");
        var migrations = await users.ExecuteScalarAsync<int>("select count(*) from information_schema.tables where table_schema in ('users', 'content')");
        var items = await shard.QueryAsync<string>("select name from content.backup_items");
        return $"{string.Join(",", directory)}|{migrations}|{string.Join(",", items)}";
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync(sql);
    }
}
