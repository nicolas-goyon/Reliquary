using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.Decks.Recommendations;

namespace Reliquary.Decks.Tests.Recommendations;

/// <summary>
/// The sample response of recommander.cards and the Oracle IDs of its cards.
/// </summary>
internal static class RecommendationSample
{
    public static readonly Guid Atraxa = Guid.Parse("7e6b9b59-cd68-4e3c-827b-38833c92d6eb");
    public static readonly Guid SolRing = Guid.Parse("6ad8011d-3471-4369-9d68-b264cc027487");
    public static readonly Guid Cultivate = Guid.Parse("8b755881-a72d-4e21-a369-d2924eb4585a");
    public static readonly Guid EnchantressPresence = Guid.Parse("795b096a-2bce-4588-a2c9-abc5ea40dc0c");
    public static readonly Guid Swords = Guid.Parse("b1544f21-7e98-461b-aed5-e748b0168c52");
    public static readonly Guid CommandTower = Guid.Parse("0895c9b7-ae7d-4bb3-af17-3b75deb50a25");
    public static readonly Guid Counterspell = Guid.Parse("cc187110-1148-4090-bbb8-e205694a39f5");
    public static readonly Guid LlanowarElves = Guid.Parse("68954295-54e3-4303-a6bc-fc4547a4e3a3");

    public static string Json { get; } = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recommendations", "recommander-atraxa.json"));
}

/// <summary>
/// An HTTP handler that answers each request with a recorded response, and keeps the requests.
/// </summary>
internal sealed class RecordedHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    private int count;

    public RecordedHandler(string json)
        : this(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") })
    {
    }

    public int Count => count;

    public ConcurrentQueue<(Uri? Uri, string Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref count);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request.RequestUri, body));
        return answer(request);
    }
}

/// <summary>
/// Gives HTTP clients with one handler and the base address of recommander.cards.
/// </summary>
internal sealed class HandlerClients(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new RecommendationOptions().BaseUrl };
}

/// <summary>
/// A cache in memory, with a rate limit that the test controls.
/// </summary>
internal sealed class MemoryRecommendationCache : IRecommendationCache
{
    public ConcurrentDictionary<string, (CachedRecommendations Value, TimeSpan Duration)> Entries { get; } = new();

    public bool AllowRequests { get; set; } = true;

    public Task<CachedRecommendations?> GetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Entries.TryGetValue(key, out var entry) ? entry.Value : null);

    public Task SetAsync(string key, CachedRecommendations value, TimeSpan duration, CancellationToken cancellationToken)
    {
        Entries[key] = (value, duration);
        return Task.CompletedTask;
    }

    public Task<bool> TryCountRequestAsync(int limit, CancellationToken cancellationToken) => Task.FromResult(AllowRequests);
}

/// <summary>
/// A card catalog with a few cards. It does not know the other cards.
/// </summary>
internal sealed class SmallCatalog : ICardCatalog
{
    private readonly Dictionary<Guid, CardFacts> cards = [];

    public SmallCatalog(params (Guid Id, string Name)[] entries)
    {
        foreach (var (id, name) in entries)
        {
            var analysis = new CardAnalysisFacts([0], 0, 1, false, false, ["Artifact"], [], "{1}");
            cards[id] = new CardFacts(id, name, "normal", 1, "Artifact", [], new Dictionary<string, string>(), [], [], false, false, null, [new CardFaceFacts(0, name, "{1}", 1, "Artifact", null, [])], analysis);
        }
    }

    public Task<IReadOnlyDictionary<Guid, CardFacts>> GetCardsAsync(IReadOnlyCollection<Guid> oracleIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CardFacts>>(oracleIds.Where(cards.ContainsKey).Distinct().ToDictionary(id => id, id => cards[id]));

    public Task<IReadOnlyDictionary<string, CardNameMatch>> ResolveNamesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<PrintingReference, PrintingFacts>> FindPrintingsAsync(IReadOnlyCollection<PrintingReference> references, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, PrintingFacts>> GetPrintingsAsync(IReadOnlyCollection<Guid> printingIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PrintingFacts>>(new Dictionary<Guid, PrintingFacts>());
}
