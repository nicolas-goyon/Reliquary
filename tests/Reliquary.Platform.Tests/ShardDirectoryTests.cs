using Dapper;
using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Platform.Tests;

public sealed class ShardDirectoryTests
{
    [Fact]
    public async Task ContentOfEachOwnerGoesToTheDatabaseOfItsShard()
    {
        await using var host = await PlatformHost.CreateAsync(databaseCount: 2);
        var ownerA = Ids.New();
        var ownerB = Ids.New();
        await AssignAsync(host, ownerA);
        await SetShardAsync(host, ownerB, "shard2");
        var database = host.Get<IDatabase>();

        await InsertItemAsync(database, ownerA, "item of A");
        await InsertItemAsync(database, ownerB, "item of B");

        Assert.Equal(["item of A"], await ItemNamesAsync(host.Databases[0].ConnectionString));
        Assert.Equal(["item of B"], await ItemNamesAsync(host.Databases[1].ConnectionString));
    }

    [Fact]
    public async Task NewOwnerGoesToTheDefaultShardAndKeepsItsAssignment()
    {
        await using var host = await PlatformHost.CreateAsync(databaseCount: 2);
        var owner = Ids.New();

        var first = await AssignAsync(host, owner);
        await SetShardAsync(host, owner, "shard2");
        var second = await AssignAsync(host, owner);

        Assert.Equal("main", first.Shard);
        Assert.Equal("shard2", second.Shard);
    }

    [Fact]
    public async Task OwnerWithoutAssignmentHasNoContentDatabase()
    {
        await using var host = await PlatformHost.CreateAsync();

        await Assert.ThrowsAsync<OwnerNotFoundException>(async () =>
            await host.Get<IDatabase>().OpenContentAsync(Ids.New(), ContentAccess.Read, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadOnlyOwnerCanReadButNotWrite()
    {
        await using var host = await PlatformHost.CreateAsync();
        var owner = Ids.New();
        await AssignAsync(host, owner);
        await using (var connection = new NpgsqlConnection(host.Databases[0].ConnectionString))
        {
            await connection.ExecuteAsync("update users.shard_directory set read_only = true where owner_id = @owner", new { owner });
        }

        var database = host.Get<IDatabase>();
        await using (var read = await database.OpenContentAsync(owner, ContentAccess.Read, TestContext.Current.CancellationToken))
        {
            Assert.Equal(System.Data.ConnectionState.Open, read.State);
        }

        await Assert.ThrowsAsync<OwnerReadOnlyException>(async () =>
            await database.OpenContentAsync(owner, ContentAccess.Write, TestContext.Current.CancellationToken));
    }

    private static async Task<ShardAssignment> AssignAsync(PlatformHost host, Guid owner)
    {
        await using var connection = await host.Get<IDatabase>().OpenUsersAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var assignment = await host.Get<IShardDirectory>().AssignAsync(owner, transaction, TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        return assignment;
    }

    private static async Task SetShardAsync(PlatformHost host, Guid owner, string shard)
    {
        await using var connection = new NpgsqlConnection(host.Databases[0].ConnectionString);
        await connection.ExecuteAsync(
            """
            insert into users.shard_directory (owner_id, shard) values (@owner, @shard)
            on conflict (owner_id) do update set shard = excluded.shard
            """,
            new { owner, shard });
    }

    private static async Task InsertItemAsync(IDatabase database, Guid owner, string name)
    {
        await using var connection = await database.OpenContentAsync(owner, ContentAccess.Write, TestContext.Current.CancellationToken);
        await connection.ExecuteAsync(
            "insert into content.test_items (id, owner_id, name) values (@id, @owner, @name)",
            new { id = Ids.New(), owner, name });
    }

    private static async Task<List<string>> ItemNamesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return [.. await connection.QueryAsync<string>("select name from content.test_items order by name")];
    }
}
