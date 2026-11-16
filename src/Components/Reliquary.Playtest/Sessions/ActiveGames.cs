using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Reliquary.Playtest.Sessions;

/// <summary>
/// Gives the number of open games to the gauge <c>reliquary.playtest.games.active</c>.
/// </summary>
/// <remarks>
/// The callback of a gauge cannot wait for Redis. Thus the callback gives the last count, and starts a new count in the background
/// when the last count is older than <see cref="RefreshInterval"/>. The first observation has no value.
/// </remarks>
internal sealed class ActiveGames(GameStore<OnlineGame> store, TimeProvider time, ILogger<ActiveGames> logger) : IHostedService
{
    /// <summary>The time after which the gauge counts the games again.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

    private readonly Lock gate = new();
    private long? count;
    private DateTimeOffset countedAt = DateTimeOffset.MinValue;
    private Task? refresh;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        PlaytestMetrics.SetActiveGamesSource(Observe);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        PlaytestMetrics.SetActiveGamesSource(null);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gives the last count, and starts a new count when the last count is old.
    /// </summary>
    /// <returns>The number of open games, or <see langword="null"/> before the first count.</returns>
    public long? Observe()
    {
        lock (gate)
        {
            if (time.GetUtcNow() - countedAt >= RefreshInterval && refresh is null or { IsCompleted: true })
            {
                refresh = Task.Run(RefreshAsync);
            }

            return count;
        }
    }

    /// <summary>Counts the open games in the game store.</summary>
    /// <returns>A task that completes after the count.</returns>
    public async Task RefreshAsync()
    {
        try
        {
            var value = await store.CountActiveAsync();
            lock (gate)
            {
                count = value;
                countedAt = time.GetUtcNow();
            }
        }
        catch (Exception exception) when (exception is StackExchange.Redis.RedisException or TimeoutException)
        {
            logger.ActiveGamesUnavailable(exception);
        }
    }
}
