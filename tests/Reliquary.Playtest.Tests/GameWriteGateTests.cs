using Reliquary.Playtest.Sessions;

namespace Reliquary.Playtest.Tests;

public sealed class GameWriteGateTests
{
    [Fact]
    public async Task OnlyOneChangeOfAGameRunsAtATime()
    {
        var gate = new GameWriteGate();
        var game = Guid.NewGuid();
        var running = 0;
        var most = 0;

        async Task ChangeAsync()
        {
            using var turn = await gate.EnterAsync(game);
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            await Task.Delay(2, TestContext.Current.CancellationToken);
            Interlocked.Decrement(ref running);
        }

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(ChangeAsync, TestContext.Current.CancellationToken)));

        Assert.Equal(1, most);
        // The gate keeps no entry for a game with no change in progress.
        Assert.Equal(0, gate.Count);
    }

    [Fact]
    public async Task ChangesOfTwoGamesRunAtTheSameTime()
    {
        var gate = new GameWriteGate();
        using var first = await gate.EnterAsync(Guid.NewGuid());

        var second = gate.EnterAsync(Guid.NewGuid());

        Assert.True(second.IsCompletedSuccessfully);
        (await second).Dispose();
        Assert.Equal(1, gate.Count);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
