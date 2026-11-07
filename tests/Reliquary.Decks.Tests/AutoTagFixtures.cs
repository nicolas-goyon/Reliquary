using System.Text.Json;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.Cards.Catalog;

namespace Reliquary.Decks.Tests;

/// <summary>
/// Reads the fixtures of auto-tag: real cards and Oracle tags of Scryfall in the format of the bulk files, and the reference decks.
/// See Fixtures/AutoTag/README.md.
/// </summary>
internal static class AutoTagFixtures
{
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AutoTag");

    private static readonly Lazy<IReadOnlyDictionary<string, CardFacts>> LazyCards = new(ReadCards);

    private static readonly Lazy<IReadOnlyDictionary<Guid, IReadOnlyList<CardOracleTag>>> LazyTags = new(ReadTags);

    /// <summary>Gets the cards by name.</summary>
    public static IReadOnlyDictionary<string, CardFacts> Cards => LazyCards.Value;

    /// <summary>Gets the Oracle tags of each card, with the ancestors of each tag.</summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<CardOracleTag>> Tags => LazyTags.Value;

    public static CardFacts Card(string name) => Cards[name];

    public static IReadOnlyList<CardOracleTag> TagsOf(CardFacts card) => Tags.GetValueOrDefault(card.OracleId) ?? [];

    public static IReadOnlyList<ReferenceDeck> ReferenceDecks()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, "reference-decks.json")));
        return [.. document.RootElement.GetProperty("decks").EnumerateArray().Select(deck => new ReferenceDeck(
            deck.GetProperty("name").GetString()!,
            [.. deck.GetProperty("cards").EnumerateArray().Select(card => new ReferenceCard(
                card.GetProperty("name").GetString()!,
                Strings(card, "categories"),
                Strings(card, "accepted")))]))];
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var list) ? [.. list.EnumerateArray().Select(item => item.GetString()!)] : [];

    private static Dictionary<string, CardFacts> ReadCards()
    {
        var cards = new Dictionary<string, CardFacts>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(Directory, "cards.jsonl")).Where(line => line.Length > 0))
        {
            using var document = JsonDocument.Parse(line);
            var card = document.RootElement;
            var faceElements = card.TryGetProperty("card_faces", out var list) ? [.. list.EnumerateArray()] : new[] { card };
            var faces = faceElements.Select((face, index) => new CardFaceFacts(
                index,
                face.GetProperty("name").GetString()!,
                face.TryGetProperty("mana_cost", out var cost) ? cost.GetString() : null,
                0,
                face.TryGetProperty("type_line", out var type) ? type.GetString() : null,
                face.TryGetProperty("oracle_text", out var text) ? text.GetString() : null,
                face.TryGetProperty("colors", out var colors) ? [.. colors.EnumerateArray().Select(color => color.GetString()!)] : [])).ToList();
            var layout = card.GetProperty("layout").GetString()!;
            var facts = new CardFacts(
                card.GetProperty("oracle_id").GetGuid(),
                card.GetProperty("name").GetString()!,
                layout,
                card.GetProperty("cmc").GetDecimal(),
                card.GetProperty("type_line").GetString(),
                Strings(card, "color_identity"),
                new Dictionary<string, string> { ["commander"] = "legal" },
                Strings(card, "keywords"),
                Strings(card, "produced_mana"),
                false,
                false,
                null,
                faces,
                CardAnalysisRules.Calculate(layout, faces));
            cards[facts.Name] = facts;
        }

        return cards;
    }

    private static Dictionary<Guid, IReadOnlyList<CardOracleTag>> ReadTags()
    {
        var tags = new Dictionary<string, (string Slug, string[] Parents, (Guid OracleId, string Weight)[] Taggings)>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(Directory, "oracle_tags.jsonl")).Where(line => line.Length > 0))
        {
            using var document = JsonDocument.Parse(line);
            var tag = document.RootElement;
            tags[tag.GetProperty("id").GetString()!] = (
                tag.GetProperty("slug").GetString()!,
                Strings(tag, "parent_ids"),
                [.. tag.GetProperty("taggings").EnumerateArray().Select(tagging => (tagging.GetProperty("oracle_id").GetGuid(), tagging.GetProperty("weight").GetString()!))]);
        }

        List<string> Ancestors(string id)
        {
            var result = new List<string>();
            var pending = new Stack<string>(tags[id].Parents);
            while (pending.TryPop(out var parent))
            {
                if (tags.TryGetValue(parent, out var tag) && !result.Contains(tag.Slug))
                {
                    result.Add(tag.Slug);
                    foreach (var next in tag.Parents)
                    {
                        pending.Push(next);
                    }
                }
            }

            return result;
        }

        return tags
            .SelectMany(pair => pair.Value.Taggings.Select(tagging => (tagging.OracleId, Tag: new CardOracleTag(pair.Value.Slug, tagging.Weight, Ancestors(pair.Key)))))
            .GroupBy(item => item.OracleId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CardOracleTag>)[.. group.Select(item => item.Tag)]);
    }
}

/// <summary>A reference deck with the expected categories of its cards.</summary>
internal sealed record ReferenceDeck(string Name, IReadOnlyList<ReferenceCard> Cards);

/// <summary>
/// A card of a reference deck. A suggestion is correct when it contains each category of <paramref name="Categories"/>,
/// and no category outside <paramref name="Categories"/> and <paramref name="Accepted"/>.
/// </summary>
internal sealed record ReferenceCard(string Name, IReadOnlyList<string> Categories, IReadOnlyList<string> Accepted)
{
    public bool IsCorrect(IReadOnlyList<string> suggested) =>
        Categories.All(suggested.Contains) && suggested.All(category => Categories.Contains(category) || Accepted.Contains(category));
}
