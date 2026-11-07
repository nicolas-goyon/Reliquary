using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reliquary.Decks.Recommendations;
using static Reliquary.Decks.Tests.Recommendations.RecommendationSample;

namespace Reliquary.Decks.Tests.Recommendations;

public sealed class RecommendationServiceTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly SmallCatalog Catalog = new(
        (SolRing, "Sol Ring"),
        (Cultivate, "Cultivate"),
        (Swords, "Swords to Plowshares"),
        (CommandTower, "Command Tower"),
        (Counterspell, "Counterspell"),
        (LlanowarElves, "Llanowar Elves"));

    private static (RecommendationService Service, RecordedHandler Handler, MemoryRecommendationCache Cache) Create(bool enabled = true, RecordedHandler? handler = null)
    {
        handler ??= new RecordedHandler(Json);
        var cache = new MemoryRecommendationCache();
        var options = Options.Create(new RecommendationOptions { Enabled = enabled });
        var service = new RecommendationService(new RecommanderSource(new HandlerClients(handler)), cache, Catalog, options, NullLogger<RecommendationService>.Instance);
        return (service, handler, cache);
    }

    [Fact]
    public async Task SecondRequestUsesTheCache()
    {
        var (service, handler, cache) = Create();

        var first = await service.RecommendAsync([Atraxa], new HashSet<Guid>(), 30, Cancel);
        var second = await service.RecommendAsync([Atraxa], new HashSet<Guid>(), 30, Cancel);

        Assert.Equal(1, handler.Count);
        Assert.Equal(RecommendationCodes.Ok, second.Status);
        Assert.Equal(first.Cards.Select(card => card.OracleId), second.Cards.Select(card => card.OracleId));
        var entry = Assert.Single(cache.Entries);
        Assert.Equal(TimeSpan.FromHours(24), entry.Value.Duration);
        Assert.Equal("Recommander", second.Source.Name);
        Assert.Equal(new Uri("https://recommander.cards/"), second.Source.Url);
    }

    [Fact]
    public async Task ResultHidesTheCardsOfTheDeckAndTheUnknownCards()
    {
        var (service, _, _) = Create();

        var result = await service.RecommendAsync([Atraxa], new HashSet<Guid> { Atraxa, SolRing, Counterspell }, 30, Cancel);

        Assert.Equal(RecommendationCodes.Ok, result.Status);
        Assert.Equal([Atraxa], result.Commanders);
        Assert.Equal([CommandTower, Cultivate, Swords, LlanowarElves], result.Cards.Select(card => card.OracleId));
        Assert.DoesNotContain(result.Cards, card => card.OracleId == EnchantressPresence);
        Assert.Equal("Command Tower", result.Cards[0].Name);
        Assert.Equal("{1}", result.Cards[0].ManaCost);
    }

    [Fact]
    public async Task LimitGivesTheBestCards()
    {
        var (service, _, _) = Create();

        var result = await service.RecommendAsync([Atraxa], new HashSet<Guid>(), 2, Cancel);

        Assert.Equal([SolRing, CommandTower], result.Cards.Select(card => card.OracleId));
    }

    [Fact]
    public async Task DisabledOptionSendsNoRequest()
    {
        var (service, handler, _) = Create(enabled: false);

        var exception = await Assert.ThrowsAsync<DeckException>(() => service.RecommendAsync([Atraxa], new HashSet<Guid>(), 30, Cancel));

        Assert.Equal(404, exception.Status);
        Assert.Equal(RecommendationCodes.Disabled, exception.Code);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task DeckWithNoCommanderSendsNoRequest()
    {
        var (service, handler, _) = Create();

        var result = await service.RecommendAsync([], new HashSet<Guid> { SolRing }, 30, Cancel);

        Assert.Equal(RecommendationCodes.NoCommander, result.Status);
        Assert.Empty(result.Cards);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task RequestSendsOnlyTheCommandersInTheOrderOfTheIds()
    {
        var (service, handler, cache) = Create();
        var partner = Guid.Parse("00000000-0000-0000-0000-000000000001");

        await service.RecommendAsync([Atraxa, partner], new HashSet<Guid> { Atraxa, partner, SolRing }, 30, Cancel);

        var (uri, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.recommander.cards/public-release/api/decks/recommend/top", uri!.ToString());
        using var json = JsonDocument.Parse(body);
        Assert.Equal("oracle_id", json.RootElement.GetProperty("card_format").GetString());
        Assert.Equal(partner.ToString(), json.RootElement.GetProperty("commander").GetString());
        Assert.Equal(Atraxa.ToString(), json.RootElement.GetProperty("partner").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("deck").GetArrayLength());
        Assert.Equal(RecommendationService.CacheKey("recommander", [partner, Atraxa]), Assert.Single(cache.Entries).Key);
    }

    [Fact]
    public async Task SourceFailureGivesUnavailableAndTheCacheKeepsItForAShortTime()
    {
        var failing = new RecordedHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.FromMinutes(30)) } });
        var (service, handler, cache) = Create(handler: failing);

        var first = await service.RecommendAsync([Atraxa], new HashSet<Guid>(), 30, Cancel);
        var second = await service.RecommendAsync([Atraxa], new HashSet<Guid>(), 30, Cancel);

        Assert.Equal(RecommendationCodes.Unavailable, first.Status);
        Assert.Equal(RecommendationCodes.Unavailable, second.Status);
        Assert.Empty(first.Cards);
        Assert.Equal(1, handler.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), Assert.Single(cache.Entries).Value.Duration);
    }

    [Fact]
    public async Task RateLimitSendsNoRequest()
    {
        var (service, handler, cache) = Create();
        cache.AllowRequests = false;

        var result = await service.RecommendAsync([Atraxa], new HashSet<Guid>(), 30, Cancel);

        Assert.Equal(RecommendationCodes.Unavailable, result.Status);
        Assert.Equal(0, handler.Count);
        Assert.Empty(cache.Entries);
    }

    [Theory]
    [InlineData("""{"result_code":"success","data":[{"oracle_id":"6ad8011d-3471-4369-9d68-b264cc027487","name":"Sol Ring","score":0.9}],"error":null}""", "Ok", 1)]
    [InlineData("""{"result_code":"success","data":{"recommendations":[]},"error":null}""", "Ok", 0)]
    [InlineData("""{"result_code":"error_not_found","data":null,"error":{"messages":["Unknown commander"]}}""", "NotFound", 0)]
    [InlineData("""{"result_code":"error_model_loading","data":null,"error":{"messages":["Loading"]}}""", "Unavailable", 0)]
    [InlineData("""[]""", "Unavailable", 0)]
    public void ReaderAcceptsBothShapesOfTheList(string json, string status, int count)
    {
        using var document = JsonDocument.Parse(json);

        var result = RecommanderSource.Read(document.RootElement);

        Assert.Equal(status, result.Status.ToString());
        Assert.Equal(count, result.Cards.Count);
    }
}
