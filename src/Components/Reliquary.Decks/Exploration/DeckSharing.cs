using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Reliquary.Decks.Exploration;

/// <summary>
/// The settings of the deck sharing: the address of the web interface for the links of the previews and of the embeds.
/// </summary>
/// <remarks>
/// The value comes from the setting <c>Auth:PublicUrl</c>, so the administrator gives the address one time. See docs/decisions/0046-deck-image-and-embed.md.
/// </remarks>
public sealed class DeckSharingOptions
{
    /// <summary>Gets or sets the address of the web interface, for example <c>https://reliquary.example.org</c>.</summary>
    public Uri PublicUrl { get; set; } = new("http://localhost:5173");

    /// <summary>Gets the address of the web interface with no slash at the end.</summary>
    internal string BaseUrl => PublicUrl.ToString().TrimEnd('/');
}

/// <summary>
/// The response of the oEmbed endpoint for a public deck. See https://oembed.com.
/// </summary>
/// <param name="Type">Always <c>rich</c>: the response gives HTML.</param>
/// <param name="Version">Always <c>1.0</c>.</param>
/// <param name="Title">The deck name.</param>
/// <param name="AuthorName">The display name of the author.</param>
/// <param name="AuthorUrl">The page of the author.</param>
/// <param name="ProviderName">Always <c>Reliquary</c>.</param>
/// <param name="ProviderUrl">The address of the web interface.</param>
/// <param name="CacheAge">The number of seconds that a client can keep the response.</param>
/// <param name="ThumbnailUrl">The image of the first commander, or <see langword="null"/>.</param>
/// <param name="ThumbnailWidth">The width of the image, or <see langword="null"/>.</param>
/// <param name="ThumbnailHeight">The height of the image, or <see langword="null"/>.</param>
/// <param name="Html">The HTML that shows the deck: an iframe on the embed page.</param>
/// <param name="Width">The width of the iframe in pixels.</param>
/// <param name="Height">The height of the iframe in pixels.</param>
public sealed record OEmbedResponse(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("author_name")] string AuthorName,
    [property: JsonPropertyName("author_url")] string AuthorUrl,
    [property: JsonPropertyName("provider_name")] string ProviderName,
    [property: JsonPropertyName("provider_url")] string ProviderUrl,
    [property: JsonPropertyName("cache_age")] int CacheAge,
    [property: JsonPropertyName("thumbnail_url")] string? ThumbnailUrl,
    [property: JsonPropertyName("thumbnail_width")] int? ThumbnailWidth,
    [property: JsonPropertyName("thumbnail_height")] int? ThumbnailHeight,
    [property: JsonPropertyName("html")] string Html,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height);

/// <summary>
/// Makes the link preview tags and the oEmbed response of a public deck.
/// </summary>
internal static partial class DeckSharing
{
    /// <summary>The width of the <c>normal</c> image of Scryfall.</summary>
    public const int ImageWidth = 488;

    /// <summary>The height of the <c>normal</c> image of Scryfall.</summary>
    public const int ImageHeight = 680;

    /// <summary>The default width of the iframe of an embed.</summary>
    public const int EmbedWidth = 600;

    /// <summary>The default height of the iframe of an embed.</summary>
    public const int EmbedHeight = 700;

    /// <summary>The smallest width or height of the iframe of an embed.</summary>
    public const int MinimumEmbedSize = 200;

    /// <summary>The maximum length of the description in a preview.</summary>
    public const int MaxDescriptionLength = 300;

    private static readonly Dictionary<string, string> FormatNames = new(StringComparer.Ordinal)
    {
        ["standardbrawl"] = "Standard Brawl",
        ["paupercommander"] = "Pauper Commander",
        ["predh"] = "PreDH",
        ["oldschool"] = "Old School",
        ["freeform"] = "Freeform",
    };

    /// <summary>
    /// Gets the deck ID of a link to a deck page or to an embed page, for example <c>https://example.org/decks/{id}</c>.
    /// </summary>
    /// <returns>The deck ID, or <see langword="null"/> when the link is not a link to a deck.</returns>
    public static Guid? ReadDeckLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var match = DeckPath().Match(uri.AbsolutePath);
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out var id) ? id : null;
    }

    /// <summary>
    /// Makes the HTML tags of the link preview: Open Graph, Twitter card and the oEmbed discovery link.
    /// Nginx puts the tags in the head of the deck page. See docs/decisions/0046-deck-image-and-embed.md.
    /// </summary>
    public static string PreviewTags(PublicDeckResponse deck, string description, DeckSharingOptions options)
    {
        var baseUrl = options.BaseUrl;
        var pageUrl = $"{baseUrl}/decks/{deck.Id}";
        var image = deck.Commanders.Select(commander => commander.ImageUri).FirstOrDefault(uri => uri is not null);
        var html = new StringBuilder();
        Meta(html, "property", "og:type", "website");
        Meta(html, "property", "og:site_name", "Reliquary");
        Meta(html, "property", "og:title", deck.Name);
        Meta(html, "property", "og:description", Description(deck, description));
        Meta(html, "property", "og:url", pageUrl);
        if (image is not null)
        {
            Meta(html, "property", "og:image", image);
            Meta(html, "property", "og:image:width", ImageWidth.ToString(CultureInfo.InvariantCulture));
            Meta(html, "property", "og:image:height", ImageHeight.ToString(CultureInfo.InvariantCulture));
            Meta(html, "property", "og:image:alt", deck.Commanders[0].Name);
        }

        Meta(html, "name", "twitter:card", "summary");
        html.Append(CultureInfo.InvariantCulture, $"<link rel=\"alternate\" type=\"application/json+oembed\" href=\"{Encode($"{baseUrl}/api/v1/oembed?url={Uri.EscapeDataString(pageUrl)}")}\" title=\"{Encode(deck.Name)}\">\n");
        return html.ToString();
    }

    /// <summary>
    /// Makes the oEmbed response of a public deck.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="maxWidth">The maximum width that the client gives, or <see langword="null"/>.</param>
    /// <param name="maxHeight">The maximum height that the client gives, or <see langword="null"/>.</param>
    /// <param name="options">The settings.</param>
    public static OEmbedResponse OEmbed(PublicDeckResponse deck, int? maxWidth, int? maxHeight, DeckSharingOptions options)
    {
        var baseUrl = options.BaseUrl;
        var width = Size(EmbedWidth, maxWidth);
        var height = Size(EmbedHeight, maxHeight);
        var image = deck.Commanders.Select(commander => commander.ImageUri).FirstOrDefault(uri => uri is not null);
        return new OEmbedResponse(
            "rich",
            "1.0",
            deck.Name,
            deck.Author.DisplayName,
            $"{baseUrl}/authors/{deck.Author.Id}",
            "Reliquary",
            baseUrl,
            3600,
            image,
            image is null ? null : ImageWidth,
            image is null ? null : ImageHeight,
            EmbedHtml(deck, width, height, options),
            width,
            height);
    }

    /// <summary>
    /// Makes the iframe of an embed. The web app makes the same snippet for the copy button.
    /// </summary>
    public static string EmbedHtml(PublicDeckResponse deck, int width, int height, DeckSharingOptions options) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"<iframe src=\"{Encode($"{options.BaseUrl}/embed/decks/{deck.Id}")}\" title=\"{Encode($"{deck.Name} · Reliquary")}\" width=\"{width}\" height=\"{height}\" style=\"border:0\" loading=\"lazy\"></iframe>");

    /// <summary>
    /// Makes the description of a preview: the format, the author, the card count and the commanders, then the start of the deck description.
    /// </summary>
    public static string Description(PublicDeckResponse deck, string description)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{FormatName(deck.Format)} deck by {deck.Author.DisplayName} · {deck.CardCount} cards");
        if (deck.Commanders.Count > 0)
        {
            text.Append(" · ").Append(string.Join(" + ", deck.Commanders.Select(commander => commander.Name)));
        }

        var extra = Whitespace().Replace(description, " ").Trim();
        if (extra.Length > 0)
        {
            text.Append(". ").Append(extra);
        }

        return text.Length <= MaxDescriptionLength ? text.ToString() : string.Concat(text.ToString(0, MaxDescriptionLength - 1).TrimEnd(), "…");
    }

    /// <summary>Gets the display name of a format, for example <c>Standard Brawl</c> for <c>standardbrawl</c>.</summary>
    public static string FormatName(string format) =>
        FormatNames.TryGetValue(format, out var name) ? name
        : format.Length == 0 ? format
        : string.Concat(char.ToUpperInvariant(format[0]).ToString(), format[1..]);

    private static int Size(int preferred, int? maximum) =>
        maximum is { } value ? Math.Max(MinimumEmbedSize, Math.Min(preferred, value)) : preferred;

    private static void Meta(StringBuilder html, string attribute, string name, string content) =>
        html.Append(CultureInfo.InvariantCulture, $"<meta {attribute}=\"{name}\" content=\"{Encode(content)}\">\n");

    /// <summary>
    /// Encodes the five special characters of HTML. The other characters stay, so a French or Japanese deck name stays readable.
    /// </summary>
    private static string Encode(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    [GeneratedRegex(@"^/(?:embed/)?decks/(?<id>[0-9a-fA-F-]{36})/?$")]
    private static partial Regex DeckPath();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
