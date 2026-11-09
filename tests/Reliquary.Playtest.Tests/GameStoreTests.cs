using Microsoft.Extensions.Options;
using Reliquary.Playtest.Engine;
using Reliquary.Playtest.Sessions;
using StackExchange.Redis;

namespace Reliquary.Playtest.Tests;

[Collection(RedisGroup.Name)]
public sealed class GameStoreTests(RedisFixture redis)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NewGameHasTheSequenceNumberZeroAndTheSameState()
    {
        var (store, gameId) = await NewStoreAsync();
        var state = TableStateTests.SampleState();

        Assert.True(await store.CreateAsync(gameId, state, Created()));
        var game = await store.GetAsync(gameId);

        Assert.NotNull(game);
        Assert.Equal(0, game.Seq);
        Assert.Equal(TableStateTests.ToJson(state), TableStateTests.ToJson(game.State));
    }

    [Fact]
    public async Task SecondCreateWithTheSameIdChangesNothing()
    {
        var (store, gameId) = await NewStoreAsync();
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());

        Assert.False(await store.CreateAsync(gameId, state with { Turn = 9 }, Created()));
        Assert.Equal(1, (await store.GetAsync(gameId))!.State.Turn);
    }

    [Fact]
    public async Task UnknownGameGivesNoGameAndNotFound()
    {
        var (store, gameId) = await NewStoreAsync();

        Assert.Null(await store.GetAsync(gameId));
        var result = await store.WriteAsync(gameId, 0, TableStateTests.SampleState(), Act(0, "a1"));
        Assert.Equal(GameWriteStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task WriteWithTheCurrentSequenceNumberGivesTheNextNumber()
    {
        var (store, gameId) = await NewStoreAsync();
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());

        var first = await store.WriteAsync(gameId, 0, state with { Turn = 2 }, Act(0, "a1"));
        var second = await store.WriteAsync(gameId, 1, state with { Turn = 3 }, Act(1, "b1"));

        Assert.Equal(new GameWriteResult(GameWriteStatus.Written, 1), first);
        Assert.Equal(new GameWriteResult(GameWriteStatus.Written, 2), second);
        var game = await store.GetAsync(gameId);
        Assert.Equal((2L, 3), (game!.Seq, game.State.Turn));
    }

    [Fact]
    public async Task WriteWithAnOldSequenceNumberIsAConflictAndChangesNothing()
    {
        var (store, gameId) = await NewStoreAsync();
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());
        await store.WriteAsync(gameId, 0, state with { Turn = 2 }, Act(0, "a1"));

        var result = await store.WriteAsync(gameId, 0, state with { Turn = 7 }, Act(1, "b1"));

        Assert.Equal(new GameWriteResult(GameWriteStatus.Conflict, 1), result);
        Assert.Equal(2, (await store.GetAsync(gameId))!.State.Turn);
    }

    [Fact]
    public async Task ConcurrentWritersGetOneTotalOrder()
    {
        var (store, gameId) = await NewStoreAsync();
        await store.CreateAsync(gameId, TableStateTests.SampleState(), Created());
        const int writers = 8;
        const int actionsPerWriter = 25;

        // Each writer reads, changes the turn and writes, with a retry after a conflict, as the game service does.
        async Task<List<long>> WriterAsync(int seat)
        {
            var sequences = new List<long>();
            for (var action = 0; action < actionsPerWriter; action++)
            {
                while (true)
                {
                    var game = (await store.GetAsync(gameId))!;
                    var result = await store.WriteAsync(gameId, game.Seq, game.State with { Turn = game.State.Turn + 1 }, Act(seat % 4, $"w{seat}-{action}"));
                    if (result.Status == GameWriteStatus.Written)
                    {
                        sequences.Add(result.Seq);
                        break;
                    }

                    Assert.Equal(GameWriteStatus.Conflict, result.Status);
                }
            }

            return sequences;
        }

        var all = (await Task.WhenAll(Enumerable.Range(0, writers).Select(seat => Task.Run(() => WriterAsync(seat), Cancel)))).SelectMany(list => list).ToList();

        const int total = writers * actionsPerWriter;
        Assert.Equal(Enumerable.Range(1, total).Select(value => (long)value), all.Order());
        var final = (await store.GetAsync(gameId))!;
        Assert.Equal(total, final.Seq);
        Assert.Equal(1 + total, final.State.Turn);
    }

    [Fact]
    public async Task RetryOfAnActionGivesTheFirstSequenceNumberAndChangesNothing()
    {
        var (store, gameId) = await NewStoreAsync();
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());
        await store.WriteAsync(gameId, 0, state with { Turn = 2 }, Act(0, "a1"));
        await store.WriteAsync(gameId, 1, state with { Turn = 3 }, Act(1, "b1"));

        // The retry comes with the current sequence number, after a reconnect.
        var retry = await store.WriteAsync(gameId, 2, state with { Turn = 99 }, Act(0, "a1"));
        // The same ID from another seat is a different action.
        var otherSeat = await store.WriteAsync(gameId, 2, state with { Turn = 4 }, Act(1, "a1"));

        Assert.Equal(new GameWriteResult(GameWriteStatus.Duplicate, 1), retry);
        Assert.Equal(new GameWriteResult(GameWriteStatus.Written, 3), otherSeat);
        Assert.Equal(4, (await store.GetAsync(gameId))!.State.Turn);
    }

    [Fact]
    public async Task StoreForgetsTheOldestClientActionIdsOfASeat()
    {
        var (store, gameId) = await NewStoreAsync(new GameStoreOptions { ClientActionIdsPerSeat = 3 });
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());
        for (var index = 1; index <= 4; index++)
        {
            await store.WriteAsync(gameId, index - 1, state, Act(0, $"a{index}"));
        }

        var forgotten = await store.WriteAsync(gameId, 4, state, Act(0, "a1"));
        var kept = await store.WriteAsync(gameId, 5, state, Act(0, "a4"));

        Assert.Equal(new GameWriteResult(GameWriteStatus.Written, 5), forgotten);
        Assert.Equal(new GameWriteResult(GameWriteStatus.Duplicate, 4), kept);
        var fields = await (await redis.GetAsync()).GetDatabase().HashLengthAsync(GameStore<TableState>.ActionsKey(gameId));
        // For each ID: the ID field and the slot field. Plus the counter of the seat.
        Assert.Equal((3 * 2) + 1, fields);
    }

    [Fact]
    public async Task ServerChangesHaveNoClientActionId()
    {
        var (store, gameId) = await NewStoreAsync();
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());

        var first = await store.WriteAsync(gameId, 0, state, new GameChange(null, "x", "{\"type\":\"start\"}"));
        var second = await store.WriteAsync(gameId, 1, state, new GameChange(null, "x", "{\"type\":\"start\"}"));

        Assert.Equal((GameWriteStatus.Written, GameWriteStatus.Written), (first.Status, second.Status));
    }

    [Fact]
    public async Task ActionStreamKeepsEachChangeInOrder()
    {
        var (store, gameId) = await NewStoreAsync();
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());
        await store.WriteAsync(gameId, 0, state, Act(0, "a1", """{"type":"draw","playerId":"p0"}"""));
        await store.WriteAsync(gameId, 1, state, Act(1, "b1", """{"type":"nextTurn"}"""));

        var log = await store.ReadLogAsync(gameId, 10);

        Assert.Equal(
            [(0L, (int?)null, """{"type":"create"}"""), (1L, 0, """{"type":"draw","playerId":"p0"}"""), (2L, 1, """{"type":"nextTurn"}""")],
            log);
    }

    [Fact]
    public async Task ActionStreamHasAnApproximateMaximumLength()
    {
        var (store, gameId) = await NewStoreAsync(new GameStoreOptions { ActionStreamLength = 10 });
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());
        for (var seq = 0; seq < 500; seq++)
        {
            await store.WriteAsync(gameId, seq, state, Act(0, null));
        }

        var length = await (await redis.GetAsync()).GetDatabase().StreamLengthAsync(GameStore<TableState>.LogKey(gameId));
        var log = await store.ReadLogAsync(gameId, 1000);

        // With "~", Redis removes whole nodes of the stream, so the stream can keep more entries than the limit, but not much more.
        Assert.InRange(length, 10, 200);
        Assert.Equal(500, log[^1].Seq);
    }

    [Fact]
    public async Task EachWriteSetsTheIdleLifetimeOnAllKeys()
    {
        var (store, gameId) = await NewStoreAsync(new GameStoreOptions { IdleLifetime = TimeSpan.FromMinutes(10) });
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());
        var database = (await redis.GetAsync()).GetDatabase();
        await database.KeyExpireAsync(GameStore<TableState>.StateKey(gameId), TimeSpan.FromSeconds(30));

        await store.WriteAsync(gameId, 0, state, Act(0, "a1"));

        foreach (var key in new[] { GameStore<TableState>.StateKey(gameId), GameStore<TableState>.SeqKey(gameId), GameStore<TableState>.LogKey(gameId), GameStore<TableState>.ActionsKey(gameId) })
        {
            var ttl = await database.KeyTimeToLiveAsync(key);
            Assert.NotNull(ttl);
            Assert.InRange(ttl.Value, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));
        }
    }

    [Fact]
    public async Task GameExpiresAfterTheIdleLifetime()
    {
        var (store, gameId) = await NewStoreAsync(new GameStoreOptions { IdleLifetime = TimeSpan.FromMilliseconds(300) });
        await store.CreateAsync(gameId, TableStateTests.SampleState(), Created());

        // Redis counts the lifetime with its own clock, so a fake time provider cannot replace this wait.
        await Task.Delay(TimeSpan.FromMilliseconds(600), Cancel);

        Assert.Null(await store.GetAsync(gameId));
        Assert.Equal(GameWriteStatus.NotFound, (await store.WriteAsync(gameId, 0, TableStateTests.SampleState(), Act(0, "a1"))).Status);
    }

    [Fact]
    public async Task DeleteRemovesAllKeys()
    {
        var (store, gameId) = await NewStoreAsync();
        var state = TableStateTests.SampleState();
        await store.CreateAsync(gameId, state, Created());
        await store.WriteAsync(gameId, 0, state, Act(0, "a1"));

        Assert.True(await store.DeleteAsync(gameId));
        Assert.False(await store.DeleteAsync(gameId));
        var database = (await redis.GetAsync()).GetDatabase();
        foreach (var key in new[] { GameStore<TableState>.StateKey(gameId), GameStore<TableState>.SeqKey(gameId), GameStore<TableState>.LogKey(gameId), GameStore<TableState>.ActionsKey(gameId) })
        {
            Assert.False(await database.KeyExistsAsync(key));
        }
    }

    [Fact]
    public async Task OpenGamesSetHasTheGamesWithARecentChange()
    {
        var connection = await redis.GetAsync();
        var database = connection.GetDatabase();
        var (store, gameId) = await NewStoreAsync();
        var old = $"test-{Guid.NewGuid():N}";
        // A game with no change for 2 hours: the count removes it.
        await database.SortedSetAddAsync(GameStore<TableState>.ActiveKey, old, DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds());

        await store.CreateAsync(gameId, TableStateTests.SampleState(), Created());
        await store.WriteAsync(gameId, 0, TableStateTests.SampleState(), Act(0, "a"));
        var count = await store.CountActiveAsync();
        var created = await database.SortedSetScoreAsync(GameStore<TableState>.ActiveKey, gameId);
        var removed = await database.SortedSetScoreAsync(GameStore<TableState>.ActiveKey, old);
        await store.DeleteAsync(gameId);
        var deleted = await database.SortedSetScoreAsync(GameStore<TableState>.ActiveKey, gameId);

        Assert.True(count >= 1);
        Assert.NotNull(created);
        Assert.Null(removed);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task FindActionGivesTheSequenceNumberOfAnAppliedAction()
    {
        var (store, gameId) = await NewStoreAsync();
        await store.CreateAsync(gameId, TableStateTests.SampleState(), Created());
        await store.WriteAsync(gameId, 0, TableStateTests.SampleState(), Act(1, "x"));

        Assert.Equal(1, await store.FindActionAsync(gameId, 1, "x"));
        Assert.Null(await store.FindActionAsync(gameId, 0, "x"));
        Assert.Null(await store.FindActionAsync(gameId, 1, "y"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a:b")]
    [InlineData("a}b")]
    public async Task GameIdWithOtherCharactersIsRefused(string gameId)
    {
        var (store, _) = await NewStoreAsync();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.GetAsync(gameId));
    }

    private async Task<(GameStore<TableState> Store, string GameId)> NewStoreAsync(GameStoreOptions? options = null)
    {
        var connection = await redis.GetAsync();
        var store = new GameStore<TableState>(new Lazy<IConnectionMultiplexer>(connection), Options.Create(options ?? new GameStoreOptions()));
        return (store, $"test-{Guid.NewGuid():N}");
    }

    private static GameChange Created() => new(null, null, """{"type":"create"}""");

    private static GameChange Act(int seat, string? clientActionId, string action = """{"type":"nextTurn"}""") => new(seat, clientActionId, action);
}
