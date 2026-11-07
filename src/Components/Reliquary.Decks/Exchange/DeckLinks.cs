using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Decks.Exchange;

/// <summary>
/// The settings of the deck import from a link. The section is <c>Decks:LinkImport</c>.
/// </summary>
/// <remarks>
/// Moxfield has no public API, and Cloudflare blocks the requests of a server.
/// Moxfield support can add a user agent to an allow list. See docs/decisions/0041-deck-import-from-a-link.md.
/// </remarks>
public sealed class DeckLinkOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Decks:LinkImport";

    /// <summary>Gets or sets the user agent that Moxfield support allows. When it is empty, the instance cannot read the decks of Moxfield.</summary>
    public string? MoxfieldUserAgent { get; set; }

    /// <summary>Gets or sets the address of a Moxfield deck. The API replaces <c>{id}</c> with the public ID of the deck.</summary>
    public string MoxfieldDeckUrl { get; set; } = "https://api2.moxfield.com/v3/decks/all/{id}";

    /// <summary>Gets or sets the address of an Archidekt deck. The API replaces <c>{id}</c> with the number of the deck.</summary>
    public string ArchidektDeckUrl { get; set; } = "https://archidekt.com/api/decks/{id}/";

    /// <summary>Gets or sets the time after which a request to the other site stops.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets the maximum size of a response of the other site, in bytes.</summary>
    public int MaxResponseBytes { get; set; } = 8 * 1024 * 1024;
}

/// <summary>A deck link: the site and the ID of the deck on the site.</summary>
internal sealed record DeckLink(string Site, string Id);

/// <summary>A deck that the API read from a link: its name and its lines in the deck text format.</summary>
internal sealed record LinkedDeck(string Name, string Text);

/// <summary>
/// Reads the links of the decks of other sites, and converts their data to the deck text format.
/// </summary>
internal static partial class DeckLinks
{
    public const string Moxfield = "moxfield";

    public const string Archidekt = "archidekt";

    private static readonly (string Board, string Header)[] MoxfieldBoards =
    [
        ("commanders", "Commander"),
        ("companions", "Companion"),
        ("mainboard", "Main"),
        ("sideboard", "Sideboard"),
        ("maybeboard", "Maybeboard"),
    ];

    private static readonly Dictionary<string, string> ArchidektZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Commander"] = "Commander",
        ["Companion"] = "Companion",
        ["Sideboard"] = "Sideboard",
        ["Maybeboard"] = "Maybeboard",
    };

    /// <summary>
    /// Reads a deck link. The API accepts only the addresses of the deck pages of Moxfield and Archidekt.
    /// </summary>
    /// <returns>The link, or <see langword="null"/> when the address is not a deck page of a known site.</returns>
    public static DeckLink? Parse(string? address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (host.Equals("moxfield.com", StringComparison.OrdinalIgnoreCase) && MoxfieldPath().Match(uri.AbsolutePath) is { Success: true } moxfield)
        {
            return new DeckLink(Moxfield, moxfield.Groups["id"].Value);
        }

        if (host.Equals("archidekt.com", StringComparison.OrdinalIgnoreCase) && ArchidektPath().Match(uri.AbsolutePath) is { Success: true } archidekt)
        {
            return new DeckLink(Archidekt, archidekt.Groups["id"].Value);
        }

        return null;
    }

    /// <summary>
    /// Converts a deck of the Moxfield API. The converter accepts the boards of the version 3 (<c>boards.mainboard.cards</c>) and the boards of the version 2 (<c>mainboard</c>).
    /// </summary>
    public static LinkedDeck FromMoxfield(JsonElement deck)
    {
        var boards = deck.TryGetProperty("boards", out var value) && value.ValueKind == JsonValueKind.Object ? value : deck;
        var text = new StringBuilder();
        foreach (var (board, header) in MoxfieldBoards)
        {
            if (!boards.TryGetProperty(board, out var section) || section.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var cards = section.TryGetProperty("cards", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : section;
            var lines = new List<string>();
            foreach (var entry in cards.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object || !entry.Value.TryGetProperty("card", out var card) || card.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var finish = String(entry.Value, "finish") ?? (Boolean(entry.Value, "isFoil") ? "foil" : null);
                lines.Add(Line(Quantity(entry.Value), String(card, "name"), String(card, "set"), String(card, "cn"), finish, []));
            }

            AppendSection(text, header, lines);
        }

        return new LinkedDeck(String(deck, "name") ?? string.Empty, text.ToString());
    }

    /// <summary>
    /// Converts a deck of the Archidekt API. A category with the name of a zone gives the zone. A category that is not in the deck gives the zone Maybeboard. The other categories become local tags.
    /// </summary>
    public static LinkedDeck FromArchidekt(JsonElement deck)
    {
        var outside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (deck.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
        {
            foreach (var category in categories.EnumerateArray())
            {
                if (category.ValueKind == JsonValueKind.Object && category.TryGetProperty("includedInDeck", out var included) && included.ValueKind == JsonValueKind.False && String(category, "name") is { } name)
                {
                    outside.Add(name);
                }
            }
        }

        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (deck.TryGetProperty("cards", out var cards) && cards.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in cards.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("card", out var card) || card.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var names = entry.TryGetProperty("categories", out var values) && values.ValueKind == JsonValueKind.Array
                    ? values.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList()
                    : [];
                var header = names.Select(name => ArchidektZones.GetValueOrDefault(name)).FirstOrDefault(zone => zone is not null)
                    ?? (names.Any(outside.Contains) ? "Maybeboard" : "Main");
                var tags = names.Where(name => !ArchidektZones.ContainsKey(name) && !outside.Contains(name)).ToList();
                var name = card.TryGetProperty("oracleCard", out var oracle) && oracle.ValueKind == JsonValueKind.Object ? String(oracle, "name") : null;
                var set = card.TryGetProperty("edition", out var edition) && edition.ValueKind == JsonValueKind.Object ? String(edition, "editioncode") : null;
                var finish = String(entry, "modifier") switch
                {
                    "Foil" => "foil",
                    "Etched" => "etched",
                    _ => null,
                };
                var line = Line(Quantity(entry), name, set, String(card, "collectorNumber"), finish, tags);
                if (!sections.TryGetValue(header, out var lines))
                {
                    sections[header] = lines = [];
                }

                lines.Add(line);
            }
        }

        var text = new StringBuilder();
        foreach (var header in new[] { "Commander", "Companion", "Main", "Sideboard", "Maybeboard" })
        {
            AppendSection(text, header, sections.GetValueOrDefault(header) ?? []);
        }

        return new LinkedDeck(String(deck, "name") ?? string.Empty, text.ToString());
    }

    private static void AppendSection(StringBuilder text, string header, List<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        text.Append(header).Append('\n');
        foreach (var line in lines)
        {
            text.Append(line).Append('\n');
        }

        text.Append('\n');
    }

    /// <summary>Writes one line of the deck text format. The line keeps only the values that the format can hold.</summary>
    private static string Line(int quantity, string? name, string? set, string? collectorNumber, string? finish, IReadOnlyList<string> tags)
    {
        var builder = new StringBuilder();
        builder.Append(quantity.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Clean(name));
        if (set is { } code && SetCode().IsMatch(code))
        {
            builder.Append(" (").Append(code.ToUpperInvariant()).Append(')');
            if (collectorNumber is { } number && CollectorNumber().IsMatch(number))
            {
                builder.Append(' ').Append(number);
            }
        }

        builder.Append(finish switch
        {
            "foil" => " *F*",
            "etched" => " *E*",
            _ => string.Empty,
        });
        foreach (var tag in tags.Select(Clean).Where(tag => tag.Length is > 0 and <= 50).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(" #").Append(tag);
        }

        return builder.ToString();
    }

    /// <summary>Removes the characters that have a meaning in the deck text format: line breaks, <c>#</c> and <c>--</c>.</summary>
    private static string Clean(string? value) =>
        WhiteSpace().Replace((value ?? string.Empty).Replace('#', ' ').Replace("--", "-", StringComparison.Ordinal), " ").Trim();

    private static int Quantity(JsonElement entry) =>
        entry.TryGetProperty("quantity", out var value) && value.TryGetInt32(out var quantity) && quantity > 0 ? quantity : 1;

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Boolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^/decks/(?<id>[A-Za-z0-9_-]{1,64})/?$")]
    private static partial Regex MoxfieldPath();

    [GeneratedRegex(@"^/decks/(?<id>[0-9]{1,12})(?:/[^/]*)?/?$")]
    private static partial Regex ArchidektPath();

    [GeneratedRegex(@"^[A-Za-z0-9]{2,6}$")]
    private static partial Regex SetCode();

    [GeneratedRegex(@"^[^\s()]{1,16}$")]
    private static partial Regex CollectorNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}

/// <summary>
/// Reads a deck from the API of another site.
/// </summary>
internal sealed class DeckLinkReader(IHttpClientFactory clients, IOptions<DeckLinkOptions> options)
{
    /// <summary>The name of the HTTP client.</summary>
    public const string HttpClientName = "Reliquary.Decks.LinkImport";

    /// <summary>
    /// Reads the deck of a link.
    /// </summary>
    /// <exception cref="DeckException">The site is not available on this instance, the deck does not exist, or the site did not answer correctly.</exception>
    public async Task<LinkedDeck> ReadAsync(DeckLink link, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var template = link.Site == DeckLinks.Moxfield ? settings.MoxfieldDeckUrl : settings.ArchidektDeckUrl;
        if (link.Site == DeckLinks.Moxfield && string.IsNullOrWhiteSpace(settings.MoxfieldUserAgent))
        {
            throw new DeckException(
                StatusCodes.Status503ServiceUnavailable,
                DeckErrorCodes.LinkSiteUnavailable,
                "This instance cannot read the decks of Moxfield. On Moxfield, export the deck as text and paste the text. An administrator can set Decks:LinkImport:MoxfieldUserAgent.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, template.Replace("{id}", Uri.EscapeDataString(link.Id), StringComparison.Ordinal));
        if (link.Site == DeckLinks.Moxfield)
        {
            request.Headers.UserAgent.Clear();
            request.Headers.TryAddWithoutValidation("User-Agent", settings.MoxfieldUserAgent);
        }

        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            // Archidekt answers 403 for a private deck.
            if (response.StatusCode == HttpStatusCode.NotFound || (response.StatusCode == HttpStatusCode.Forbidden && link.Site == DeckLinks.Archidekt))
            {
                throw new DeckException(StatusCodes.Status422UnprocessableEntity, DeckErrorCodes.LinkDeckNotFound, "The site has no public deck at this link. Make sure that the deck is public or unlisted.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw SiteFailed();
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw SiteFailed();
            }

            return link.Site == DeckLinks.Moxfield ? DeckLinks.FromMoxfield(document.RootElement) : DeckLinks.FromArchidekt(document.RootElement);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw SiteFailed();
        }
    }

    private static DeckException SiteFailed() =>
        new(StatusCodes.Status502BadGateway, DeckErrorCodes.LinkSiteFailed, "The other site did not give the deck. Try again later, or export the deck as text and paste the text.");
}
