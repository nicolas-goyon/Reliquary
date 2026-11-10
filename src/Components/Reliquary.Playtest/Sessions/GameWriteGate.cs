namespace Reliquary.Playtest.Sessions;

/// <summary>
/// Lets only one change of a game run at a time on one API replica. The other changes of the game on this replica wait in a queue.
/// </summary>
/// <remarks>
/// The game store orders the changes with a compare-and-set. Without this gate, all the writers of a game on all the replicas
/// try again after each conflict, and a writer can lose many times. With the gate, only one writer for each replica is in the
/// compare-and-set, so a conflict comes only from another replica. The gate keeps an entry only for a game with a change in progress.
/// </remarks>
internal sealed class GameWriteGate
{
    private readonly Dictionary<Guid, Entry> entries = [];

    /// <summary>
    /// Waits until no other change of the game runs on this replica.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <returns>An object that lets the next change of the game run when the caller disposes it.</returns>
    public async Task<IDisposable> EnterAsync(Guid gameId)
    {
        Entry entry;
        lock (entries)
        {
            if (!entries.TryGetValue(gameId, out entry!))
            {
                entry = new Entry();
                entries[gameId] = entry;
            }

            entry.Users++;
        }

        try
        {
            await entry.Lock.WaitAsync();
        }
        catch
        {
            Leave(gameId, entry);
            throw;
        }

        return new Release(this, gameId, entry);
    }

    /// <summary>Gets the number of games with a change in progress or in the queue, for the tests.</summary>
    internal int Count
    {
        get
        {
            lock (entries)
            {
                return entries.Count;
            }
        }
    }

    private void Leave(Guid gameId, Entry entry)
    {
        lock (entries)
        {
            if (--entry.Users == 0)
            {
                entries.Remove(gameId);
                entry.Lock.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public int Users { get; set; }
    }

    private sealed class Release(GameWriteGate gate, Guid gameId, Entry entry) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                entry.Lock.Release();
                gate.Leave(gameId, entry);
            }
        }
    }
}
