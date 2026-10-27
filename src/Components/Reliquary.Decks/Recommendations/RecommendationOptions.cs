namespace Reliquary.Decks.Recommendations;

/// <summary>
/// The settings of the card recommendations. The section is <c>Recommendations</c>.
/// </summary>
/// <remarks>
/// The recommendations use the public API of recommander.cards. See docs/decisions/0028-card-recommendations-source.md.
/// The terms of recommander.cards allow personal, non-commercial use. A commercial instance needs an agreement with Recommander.
/// </remarks>
public sealed class RecommendationOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Recommendations";

    /// <summary>Gets or sets whether the instance gives card recommendations. The default is <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the base address of the public API of recommander.cards.</summary>
    public Uri BaseUrl { get; set; } = new("https://api.recommander.cards/public-release/");

    /// <summary>Gets or sets how long Redis keeps the recommendations of a commander pair.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Gets or sets how long Redis keeps a failure or an unknown commander, so the API does not send the same request again at once.</summary>
    public TimeSpan FailureCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets the maximum number of requests to recommander.cards in one minute, for all the API replicas.</summary>
    public int RequestsPerMinute { get; set; } = 20;

    /// <summary>Gets or sets the time after which a request to recommander.cards stops.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
