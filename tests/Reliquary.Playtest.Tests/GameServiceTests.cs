using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Reliquary.BuildingBlocks.Decks;
using Reliquary.Playtest.Sessions;
using StackExchange.Redis;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// Checks the time limits and the daily limit of the game service with a fake clock and the real Redis server.
/// </summary>
[Collection(RedisGroup.Name)]
public sealed class GameServiceTests(RedisFixture redis)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void RetryDelayGrowsWithTheAttemptAndStaysShort()
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var delays = Enumerable.Range(0, 200).Select(_ => GameService.RetryDelay(attempt).TotalMilliseconds).ToList();

            Assert.All(delays, delay => Assert.InRange(delay, 1, Math.Min(attempt + 1, GameService.MaxRetryDelayMilliseconds)));
        }

        // The longest attempts have different waits, so the players that conflict do not write again at the same time.
        Assert.True(Enumerable.Range(0, 200).Select(_ => GameService.RetryDelay(15)).Distinct().Count() > 1);
    }

    [Fact]
    public async Task GameClosesAtItsHardLimitAlsoWithActivity()
    {
        // The fake clock does not move the clock of Redis, so the game stays in Redis. The hard limit counts from the creation time in the state.
        var (service, time, _) = await NewServiceAsync();
        var (grant, secret) = await service.CreateAsync(Host(), null, 4, Cancel);

        time.Advance(TimeSpan.FromHours(7) + TimeSpan.FromMinutes(59));
        await service.GetAsync(grant.Game.Id, grant.Token);
        time.Advance(TimeSpan.FromMinutes(1));

        var exception = await Assert.ThrowsAsync<GameException>(() => service.GetAsync(grant.Game.Id, grant.Token));
        Assert.Equal(404, exception.Status);
        var join = await Assert.ThrowsAsync<GameException>(() => service.JoinAsync(grant.Game.Id, secret, Guest(), Cancel));
        Assert.Equal(PlaytestErrorCodes.InviteInvalid, join.Code);
    }

    [Fact]
    public async Task InviteExpiresAfterItsLifetime()
    {
        var (service, time, _) = await NewServiceAsync(new GameOptions { InviteLifetime = TimeSpan.FromHours(2) });
        var (grant, secret) = await service.CreateAsync(Host(), null, 4, Cancel);

        time.Advance(TimeSpan.FromMinutes(119));
        await service.JoinAsync(grant.Game.Id, secret, Guest(), Cancel);
        time.Advance(TimeSpan.FromMinutes(1));

        var exception = await Assert.ThrowsAsync<GameException>(() => service.JoinAsync(grant.Game.Id, secret, Guest(), Cancel));
        Assert.Equal(PlaytestErrorCodes.InviteInvalid, exception.Code);
    }

    [Fact]
    public async Task GameTokenExpiresWithTheGame()
    {
        var (service, time, tokens) = await NewServiceAsync();
        var (grant, _) = await service.CreateAsync(Host(), null, 4, Cancel);

        Assert.Equal(grant.Game.EndsAt, grant.TokenExpiresAt);
        time.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));

        Assert.Null(await tokens.ReadAsync(grant.Token));
    }

    [Fact]
    public async Task AccountCreatesTwentyGamesInOneDayAtMost()
    {
        var (service, time, _) = await NewServiceAsync(new GameOptions { GamesPerAccountPerDay = 2, OpenGamesPerAccount = 10 });
        var host = Host();

        await service.CreateAsync(host, null, 4, Cancel);
        await service.CreateAsync(host, null, 4, Cancel);
        var third = await Assert.ThrowsAsync<GameException>(() => service.CreateAsync(host, null, 4, Cancel));
        time.Advance(TimeSpan.FromDays(1));
        await service.CreateAsync(host, null, 4, Cancel);

        Assert.Equal((429, "rate_limited"), (third.Status, third.Code));
    }

    [Fact]
    public async Task OpenGameThatExpiredInRedisDoesNotCount()
    {
        var (service, _, _) = await NewServiceAsync(new GameOptions { OpenGamesPerAccount = 1 });
        var host = Host();
        var (grant, _) = await service.CreateAsync(host, null, 4, Cancel);

        // The idle lifetime of Redis deletes the state of a game. The game is then not open.
        await (await redis.GetAsync()).GetDatabase().KeyDeleteAsync(GameStore<OnlineGame>.StateKey(GameKeys.Id(grant.Game.Id)));

        await service.CreateAsync(host, null, 4, Cancel);
    }

    private static SeatRequest Host() => new(Guid.NewGuid(), "Ana", new DeckChoice(null, "deck"));

    private static SeatRequest Guest() => new(null, "Cleo", new DeckChoice(null, "deck"));

    private async Task<(GameService Service, FakeTimeProvider Time, GameTokens Tokens)> NewServiceAsync(GameOptions? options = null)
    {
        var connection = new Lazy<IConnectionMultiplexer>(await redis.GetAsync());
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var settings = Options.Create(options ?? new GameOptions());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:SigningKey"] = "dGVzdC1zaWduaW5nLWtleS1mb3ItdGhlLXBsYXl0ZXN0LXRlc3RzLW9ubHk=",
        }).Build();
        var tokens = new GameTokens(configuration, time);
        var service = new GameService(
            new GameStore<OnlineGame>(connection, Options.Create(new GameStoreOptions())),
            new GameLimits(connection, settings, time),
            tokens,
            new FixedDeckSource(),
            settings,
            time);
        return (service, time, tokens);
    }

    private sealed class FixedDeckSource : IGameDeckSource
    {
        private static readonly GameDeck Deck = new(
            string.Empty,
            null,
            [new GameDeckCard(Guid.NewGuid(), "Mountain", null, true, 0, 30)],
            []);

        public Task<GameDeck?> GetDeckAsync(Guid deckId, Guid? viewerId, CancellationToken cancellationToken = default) => Task.FromResult<GameDeck?>(Deck);

        public Task<GameDeck> ParseDeckTextAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(Deck);
    }
}
