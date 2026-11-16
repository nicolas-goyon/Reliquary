using System.Globalization;
using System.Text.Json;
using Reliquary.Playtest.Engine;
using Reliquary.Playtest.Sessions;
using StackExchange.Redis;

namespace Reliquary.Playtest.Tests.Gate;

/// <summary>
/// The full state of the table at each sequence number, as the server knows it: the state in Redis at the start,
/// then each action of the action stream, applied with the C# table engine.
/// The gate test compares the view of each client with the view of this state.
/// </summary>
internal sealed class GateTruth(IDatabase redis, Guid gameId, long startSeq, TableState start) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<long, TableState> states = new() { [startSeq] = start };
    private long lastSeq = startSeq;

    /// <summary>Gets the sequence number of the start of the game.</summary>
    public long StartSeq { get; } = startSeq;

    /// <summary>Gets the last sequence number that the truth read from the action stream.</summary>
    public long LastSeq => Volatile.Read(ref lastSeq);

    /// <summary>
    /// Reads the state of the game in Redis just after the start, before the first action.
    /// </summary>
    public static async Task<GateTruth> ReadStartAsync(IDatabase redis, Guid gameId)
    {
        var id = GameKeys.Id(gameId);
        var values = await redis.StringGetAsync([GameStore<OnlineGame>.SeqKey(id), GameStore<OnlineGame>.StateKey(id)]);
        var seq = long.Parse((string)values[0]!, CultureInfo.InvariantCulture);
        var game = JsonSerializer.Deserialize<OnlineGame>((string)values[1]!, TableJson.Options)!;
        return new GateTruth(redis, gameId, seq, game.Table ?? throw new InvalidOperationException("The game did not start."));
    }

    /// <summary>
    /// Gets the full state at a sequence number. When the number is after the last known number, reads the new entries of the action stream.
    /// </summary>
    public async Task<TableState> StateAtAsync(long seq)
    {
        await gate.WaitAsync();
        try
        {
            if (seq > lastSeq)
            {
                await ReadToAsync(seq);
            }

            return states.TryGetValue(seq, out var state)
                ? state
                : throw new InvalidOperationException($"The action stream has no state for the sequence number {seq}. The last number is {lastSeq}.");
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Reads the state in Redis now, to compare it with the replay of the action stream.</summary>
    public async Task<(long Seq, TableState Table)> ReadStoredAsync()
    {
        var id = GameKeys.Id(gameId);
        var values = await redis.StringGetAsync([GameStore<OnlineGame>.SeqKey(id), GameStore<OnlineGame>.StateKey(id)]);
        var game = JsonSerializer.Deserialize<OnlineGame>((string)values[1]!, TableJson.Options)!;
        return (long.Parse((string)values[0]!, CultureInfo.InvariantCulture), game.Table!);
    }

    public void Dispose() => gate.Dispose();

    private async Task ReadToAsync(long seq)
    {
        var key = GameStore<OnlineGame>.LogKey(GameKeys.Id(gameId));
        while (lastSeq < seq)
        {
            // The stream entry of a change has the ID "{seq}-1".
            var entries = await redis.StreamRangeAsync(key, $"{lastSeq + 1}-0", "+", count: 500);
            if (entries.Length == 0)
            {
                throw new InvalidOperationException($"The action stream has no entry after {lastSeq}, but a client got the sequence number {seq}.");
            }

            foreach (var entry in entries)
            {
                var entrySeq = long.Parse((string)entry["seq"]!, CultureInfo.InvariantCulture);
                if (entrySeq != lastSeq + 1)
                {
                    throw new InvalidOperationException($"The action stream has a gap: {entrySeq} after {lastSeq}.");
                }

                var action = JsonSerializer.Deserialize<TableAction>((string)entry["action"]!, TableJson.Options)!;
                states[entrySeq] = TableEngine.Apply(states[lastSeq], action);
                lastSeq = entrySeq;
            }
        }
    }
}
