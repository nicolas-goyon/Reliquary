using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Decks.Recommendations;

/// <summary>
/// The card recommendations of a deck.
/// </summary>
/// <param name="Status">
/// <c>ok</c>: the list comes from the source.
/// <c>no_commander</c>: the deck has no commander, so the list is empty.
/// <c>unavailable</c>: the source failed or is at its rate limit, so the list is empty. Try again later.
/// </param>
/// <param name="Source">The source of the recommendations. Show its credit with the list.</param>
/// <param name="Commanders">The commanders that the recommendations use.</param>
/// <param name="Cards">The recommended cards, from the highest score. The list does not contain the cards of the deck.</param>
public sealed record DeckRecommendationsResponse(string Status, RecommendationSourceResponse Source, IReadOnlyList<Guid> Commanders, IReadOnlyList<RecommendedCardResponse> Cards);

/// <summary>
/// The source of the recommendations, for the credit.
/// </summary>
/// <param name="Name">The name of the source, for example <c>Recommander</c>.</param>
/// <param name="Url">The address of the site of the source.</param>
public sealed record RecommendationSourceResponse(string Name, Uri Url);

/// <summary>
/// A recommended card.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The Oracle name.</param>
/// <param name="Score">The score of the source, from 0 to 1.</param>
/// <param name="ManaCost">The mana cost of the first face, or <see langword="null"/>.</param>
/// <param name="TypeLine">The type line, or <see langword="null"/>.</param>
/// <param name="ImageUri">The image of the default printing, or <see langword="null"/>.</param>
public sealed record RecommendedCardResponse(Guid OracleId, string Name, double Score, string? ManaCost, string? TypeLine, string? ImageUri);

/// <summary>
/// The stable codes of the recommendations.
/// </summary>
public static class RecommendationCodes
{
    /// <summary>The administrator did not enable the recommendations on the instance.</summary>
    public const string Disabled = "recommendations_disabled";

    /// <summary>The list comes from the source.</summary>
    public const string Ok = "ok";

    /// <summary>The deck has no commander.</summary>
    public const string NoCommander = "no_commander";

    /// <summary>The source is not available now.</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>
/// Gives the card recommendations for the commanders of a deck, from the cache or from the source.
/// See docs/decisions/0028-card-recommendations-source.md.
/// </summary>
internal sealed class RecommendationService(
    IRecommendationSource source,
    IRecommendationCache cache,
    ICardCatalog catalog,
    IOptions<RecommendationOptions> options,
    ILogger<RecommendationService> logger)
{
    /// <summary>The maximum number of commanders that the source accepts: a commander and a partner.</summary>
    public const int MaxCommanders = 2;

    // One request to the source for each key at a time, in this replica.
    private readonly ConcurrentDictionary<string, Lazy<Task<CachedRecommendations>>> pending = new(StringComparer.Ordinal);

    /// <summary>Gets whether the administrator enabled the recommendations.</summary>
    public bool Enabled => options.Value.Enabled;

    /// <summary>
    /// Gets the recommendations for the commanders of a deck.
    /// </summary>
    /// <param name="commanders">The Oracle IDs of the commanders of the deck. The service uses the first two.</param>
    /// <param name="deckCards">The Oracle IDs of all the cards of the deck. The result does not contain them.</param>
    /// <param name="limit">The maximum number of cards in the result.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The recommendations.</returns>
    /// <exception cref="DeckException">The recommendations are not enabled.</exception>
    public async Task<DeckRecommendationsResponse> RecommendAsync(
        IReadOnlyList<Guid> commanders, IReadOnlySet<Guid> deckCards, int limit, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            throw new DeckException(
                StatusCodes.Status404NotFound,
                RecommendationCodes.Disabled,
                "The recommendations are not enabled on this instance. The administrator can enable them with the setting Recommendations:Enabled.");
        }

        var credit = new RecommendationSourceResponse(source.Name, source.Site);
        var pair = commanders.Distinct().Take(MaxCommanders).Order().ToList();
        if (pair.Count == 0)
        {
            return new DeckRecommendationsResponse(RecommendationCodes.NoCommander, credit, [], []);
        }

        var key = CacheKey(source.Id, pair);
        var cached = await cache.GetAsync(key, cancellationToken)
            ?? await pending.GetOrAdd(key, _ => new Lazy<Task<CachedRecommendations>>(() => FetchAsync(key, pair))).Value;
        if (!cached.Available)
        {
            return new DeckRecommendationsResponse(RecommendationCodes.Unavailable, credit, pair, []);
        }

        var cards = await DescribeAsync(cached.Cards.Where(card => !deckCards.Contains(card.OracleId)), limit, cancellationToken);
        return new DeckRecommendationsResponse(RecommendationCodes.Ok, credit, pair, cards);
    }

    /// <summary>Gets the cache key of a commander pair, in the order of the IDs.</summary>
    internal static string CacheKey(string sourceId, IReadOnlyList<Guid> pair) =>
        $"{RedisRecommendationCache.KeyPrefix}v1:{sourceId}:{string.Join(':', pair.Select(id => id.ToString("N")))}";

    private async Task<CachedRecommendations> FetchAsync(string key, List<Guid> pair)
    {
        try
        {
            // The request continues when the client goes away, so the next request finds the result in the cache.
            var settings = options.Value;
            if (!await cache.TryCountRequestAsync(settings.RequestsPerMinute, CancellationToken.None))
            {
                logger.RecommendationRateLimit(source.Id, settings.RequestsPerMinute);
                return new CachedRecommendations(false, []);
            }

            var result = await source.RecommendAsync(pair[0], pair.Count > 1 ? pair[1] : null, CancellationToken.None);
            CachedRecommendations value;
            TimeSpan duration;
            switch (result.Status)
            {
                case SourceStatus.Ok:
                    value = new CachedRecommendations(true, result.Cards);
                    duration = settings.CacheDuration;
                    break;
                case SourceStatus.NotFound:
                    value = new CachedRecommendations(true, []);
                    duration = settings.FailureCacheDuration;
                    break;
                default:
                    logger.RecommendationSourceFailed(source.Id, string.Join(", ", pair), result.Reason ?? "unknown");
                    value = new CachedRecommendations(false, []);
                    duration = result.RetryAfter is { } wait && wait > settings.FailureCacheDuration ? wait : settings.FailureCacheDuration;
                    break;
            }

            await cache.SetAsync(key, value, duration, CancellationToken.None);
            return value;
        }
        finally
        {
            pending.TryRemove(key, out _);
        }
    }

    private async Task<IReadOnlyList<RecommendedCardResponse>> DescribeAsync(IEnumerable<SourceCard> candidates, int limit, CancellationToken cancellationToken)
    {
        // The catalog drops the cards that it does not know, so the service asks for a few more cards than the limit.
        var selected = candidates.Take(limit + 20).ToList();
        if (selected.Count == 0)
        {
            return [];
        }

        var facts = await catalog.GetCardsAsync([.. selected.Select(card => card.OracleId)], cancellationToken);
        var known = selected.Where(card => facts.TryGetValue(card.OracleId, out var fact) && !fact.IsExtra).Take(limit).ToList();
        var printingIds = known.Select(card => facts[card.OracleId].DefaultPrintingId).OfType<Guid>().Distinct().ToList();
        var printings = printingIds.Count == 0 ? new Dictionary<Guid, PrintingFacts>() : await catalog.GetPrintingsAsync(printingIds, cancellationToken);

        return [.. known.Select(card =>
        {
            var fact = facts[card.OracleId];
            var image = fact.DefaultPrintingId is { } printingId && printings.TryGetValue(printingId, out var printing) ? printing.ImageUri : null;
            return new RecommendedCardResponse(card.OracleId, fact.Name, card.Score, fact.Faces.Count > 0 ? fact.Faces[0].ManaCost : null, fact.TypeLine, image);
        })];
    }
}
