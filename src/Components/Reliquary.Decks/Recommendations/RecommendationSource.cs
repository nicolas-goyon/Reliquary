using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reliquary.Decks.Recommendations;

/// <summary>
/// A card that the source recommends.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The name that the source gives.</param>
/// <param name="Score">The score of the source. A higher score comes first.</param>
internal sealed record SourceCard(Guid OracleId, string Name, double Score);

/// <summary>
/// The result of a request to the source.
/// </summary>
internal enum SourceStatus
{
    /// <summary>The source gave a list. The list can be empty.</summary>
    Ok,

    /// <summary>The source does not know the commander.</summary>
    NotFound,

    /// <summary>The source failed, stopped after the timeout or is at its rate limit.</summary>
    Unavailable,
}

/// <summary>
/// The answer of the source.
/// </summary>
/// <param name="Status">The result of the request.</param>
/// <param name="Cards">The recommended cards, from the highest score.</param>
/// <param name="RetryAfter">The time that the source asks the client to wait, or <see langword="null"/>.</param>
/// <param name="Reason">A short reason of a failure, for the logs.</param>
internal sealed record SourceResult(SourceStatus Status, IReadOnlyList<SourceCard> Cards, TimeSpan? RetryAfter = null, string? Reason = null)
{
    public static SourceResult Unavailable(string reason, TimeSpan? retryAfter = null) => new(SourceStatus.Unavailable, [], retryAfter, reason);
}

/// <summary>
/// A source of card recommendations for the commanders of a deck.
/// </summary>
internal interface IRecommendationSource
{
    /// <summary>Gets the stable ID of the source, for the cache keys, for example <c>recommander</c>.</summary>
    string Id { get; }

    /// <summary>Gets the name of the source for the credit.</summary>
    string Name { get; }

    /// <summary>Gets the address of the site of the source for the credit.</summary>
    Uri Site { get; }

    /// <summary>
    /// Gets the recommendations for a commander and an optional partner.
    /// </summary>
    /// <param name="commander">The Oracle ID of the commander.</param>
    /// <param name="partner">The Oracle ID of the partner, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The result. A failure of the source gives <see cref="SourceStatus.Unavailable"/>, not an exception.</returns>
    Task<SourceResult> RecommendAsync(Guid commander, Guid? partner, CancellationToken cancellationToken);
}

/// <summary>
/// Gets the recommendations from the public API of recommander.cards: <c>POST api/decks/recommend/top</c>.
/// </summary>
/// <remarks>
/// The request gives the Oracle IDs of the commanders only. The deck list of the user stays on the instance.
/// The documentation of the API shows the list in <c>data</c> and in <c>data.recommendations</c>, so the reader accepts both shapes.
/// </remarks>
internal sealed class RecommanderSource(IHttpClientFactory httpClients) : IRecommendationSource
{
    /// <summary>The name of the HTTP client. The tests replace its handler.</summary>
    public const string HttpClientName = "Reliquary.Decks.Recommendations";

    private const string RecommendPath = "api/decks/recommend/top";

    private static readonly JsonSerializerOptions RequestJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string Id => "recommander";

    public string Name => "Recommander";

    public Uri Site { get; } = new("https://recommander.cards/");

    public async Task<SourceResult> RecommendAsync(Guid commander, Guid? partner, CancellationToken cancellationToken)
    {
        var body = new RecommendRequest("oracle_id", commander.ToString(), partner?.ToString(), []);
        try
        {
            using var http = httpClients.CreateClient(HttpClientName);
            using var response = await http.PostAsJsonAsync(RecommendPath, body, RequestJson, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return SourceResult.Unavailable("rate_limited", RetryAfterOf(response));
            }

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound && response.StatusCode != HttpStatusCode.BadRequest)
            {
                return SourceResult.Unavailable($"http_{(int)response.StatusCode}", RetryAfterOf(response));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return Read(document.RootElement);
        }
        catch (HttpRequestException exception)
        {
            return SourceResult.Unavailable($"network: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceResult.Unavailable("timeout");
        }
        catch (JsonException)
        {
            return SourceResult.Unavailable("invalid_json");
        }
    }

    /// <summary>
    /// Reads the body of a response of the API.
    /// </summary>
    internal static SourceResult Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return SourceResult.Unavailable("invalid_response");
        }

        var code = root.TryGetProperty("result_code", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        switch (code)
        {
            case "success":
                break;
            case "error_not_found" or "error_invalid_cards":
                return new SourceResult(SourceStatus.NotFound, []);
            case "error_rate_limited":
                return SourceResult.Unavailable("rate_limited");
            default:
                return SourceResult.Unavailable(code ?? "no_result_code");
        }

        var list = root.TryGetProperty("data", out var data) ? data : default;
        if (list.ValueKind == JsonValueKind.Object && list.TryGetProperty("recommendations", out var nested))
        {
            list = nested;
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            return new SourceResult(SourceStatus.Ok, []);
        }

        var cards = new List<SourceCard>();
        var seen = new HashSet<Guid>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("oracle_id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var oracleId)
                && seen.Add(oracleId))
            {
                var name = item.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() ?? string.Empty : string.Empty;
                var score = item.TryGetProperty("score", out var scoreValue) && scoreValue.ValueKind == JsonValueKind.Number ? scoreValue.GetDouble() : 0;
                cards.Add(new SourceCard(oracleId, name, score));
            }
        }

        return new SourceResult(SourceStatus.Ok, [.. cards.OrderByDescending(card => card.Score)]);
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => null,
        };

    private sealed record RecommendRequest(
        [property: JsonPropertyName("card_format")] string CardFormat,
        [property: JsonPropertyName("commander")] string Commander,
        [property: JsonPropertyName("partner")] string? Partner,
        [property: JsonPropertyName("deck")] IReadOnlyList<string> Deck);
}
