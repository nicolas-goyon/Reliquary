using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Decks.AutoTag;

/// <summary>
/// The sources of a suggestion of auto-tag. The API gives these stable codes.
/// </summary>
public static class AutoTagSources
{
    /// <summary>A rule of the user: the user chose these categories for the card before.</summary>
    public const string UserRule = "user_rule";

    /// <summary>An Oracle tag of Scryfall Tagger.</summary>
    public const string OracleTag = "oracle_tag";

    /// <summary>A rule on the card text or on the type line.</summary>
    public const string TextRule = "text_rule";

    /// <summary>No source gives a category.</summary>
    public const string None = "none";
}

/// <summary>
/// The reason for one suggested category.
/// </summary>
/// <param name="Category">The category.</param>
/// <param name="Source">The source: <c>user_rule</c>, <c>oracle_tag</c> or <c>text_rule</c>.</param>
/// <param name="Detail">The slug of the Oracle tag, or the code of the text rule. <see langword="null"/> for a user rule.</param>
internal sealed record AutoTagReason(string Category, string Source, string? Detail);

/// <summary>
/// The suggested categories of one card.
/// </summary>
/// <param name="Source">The main source of the suggestion. See <see cref="AutoTagSources"/>.</param>
/// <param name="Categories">The categories, in the order of <see cref="DeckRules.Categories"/>.</param>
/// <param name="Reasons">One reason for each category.</param>
internal sealed record AutoTagSuggestion(string Source, IReadOnlyList<string> Categories, IReadOnlyList<AutoTagReason> Reasons);

/// <summary>
/// Suggests the categories of a card. The class is a pure function of the card facts, the Oracle tags and the user rule. See ADR 0031.
/// </summary>
internal static class AutoTagger
{
    /// <summary>
    /// Suggests the categories of a card, in this order of priority:
    /// <list type="number">
    /// <item>The rule of the user for the card, when it exists.</item>
    /// <item>The categories of the Oracle tags of the card.</item>
    /// <item>The text rules, when the Oracle tags give no category.</item>
    /// </list>
    /// A card with a land as its first face also gets the category <c>land</c>, and gets no <c>ramp</c>.
    /// </summary>
    /// <param name="card">The facts of the card.</param>
    /// <param name="tags">The Oracle tags of the card. An empty list for a card with no tag.</param>
    /// <param name="userRule">The categories of the rule of the user, or <see langword="null"/> when the user has no rule for the card.</param>
    /// <returns>The suggestion.</returns>
    public static AutoTagSuggestion Suggest(CardFacts card, IReadOnlyList<CardOracleTag> tags, IReadOnlyList<string>? userRule = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(tags);
        if (userRule is not null)
        {
            var categories = Ordered(userRule.Where(DeckRules.Categories.Contains));
            return new AutoTagSuggestion(AutoTagSources.UserRule, categories, [.. categories.Select(category => new AutoTagReason(category, AutoTagSources.UserRule, null))]);
        }

        var isLand = card.Analysis.IsLand;
        var reasons = FromTags(tags);
        var source = AutoTagSources.OracleTag;
        if (reasons.Count == 0)
        {
            reasons = FromText(card);
            source = reasons.Count > 0 ? AutoTagSources.TextRule : AutoTagSources.None;
        }

        if (isLand)
        {
            // A land makes mana, but a deck counts its lands apart from its ramp.
            reasons.RemoveAll(reason => reason.Category == "ramp");
            reasons.Insert(0, new AutoTagReason("land", AutoTagSources.TextRule, AutoTagRules.LandRule));
            if (source == AutoTagSources.None || reasons.Count == 1)
            {
                source = AutoTagSources.TextRule;
            }
        }

        var ordered = Ordered(reasons.Select(reason => reason.Category));
        return new AutoTagSuggestion(source, ordered, [.. ordered.Select(category => reasons.First(reason => reason.Category == category))]);
    }

    /// <summary>
    /// Gets the categories of the Oracle tags, with one reason for each category.
    /// </summary>
    private static List<AutoTagReason> FromTags(IReadOnlyList<CardOracleTag> tags)
    {
        var found = new List<(AutoTagReason Reason, bool IsGeneral)>();
        foreach (var tag in tags.Where(tag => tag.Weight != "weak").OrderBy(tag => tag.Slug, StringComparer.Ordinal))
        {
            foreach (var rule in AutoTagRules.OracleTags)
            {
                if (tag.Slug == rule.Slug || tag.Ancestors.Contains(rule.Slug))
                {
                    found.Add((new AutoTagReason(rule.Category, AutoTagSources.OracleTag, tag.Slug), rule.IsGeneral));
                }
            }
        }

        // A board wipe is also a removal in the tag tree. Only a specific tag, for example spot-removal, gives it a targeted interaction too.
        if (found.Any(item => item.Reason.Category == "board_wipe"))
        {
            found.RemoveAll(item => item.IsGeneral);
        }

        // A specific tag gives a better reason than a general tag.
        return [.. found.OrderBy(item => item.IsGeneral).Select(item => item.Reason).DistinctBy(reason => reason.Category)];
    }

    /// <summary>
    /// Gets the categories of the text rules, with one reason for each category.
    /// </summary>
    private static List<AutoTagReason> FromText(CardFacts card)
    {
        var allFaces = string.Join('\n', card.Faces.Select(face => AutoTagRules.Clean(face.OracleText)));
        var spellFaces = string.Join('\n', card.Faces
            .Where(face => face.TypeLine?.Contains("Land", StringComparison.Ordinal) != true)
            .Select(face => AutoTagRules.Clean(face.OracleText)));
        var reasons = new List<AutoTagReason>();
        foreach (var rule in AutoTagRules.TextRules)
        {
            if (reasons.Any(reason => reason.Category == rule.Category))
            {
                continue;
            }

            if (rule.Pattern.IsMatch(rule.SkipLandFaces ? spellFaces : allFaces))
            {
                reasons.Add(new AutoTagReason(rule.Category, AutoTagSources.TextRule, rule.Code));
            }
        }

        return reasons;
    }

    private static List<string> Ordered(IEnumerable<string> categories)
    {
        var set = categories.ToHashSet(StringComparer.Ordinal);
        return [.. DeckRules.Categories.Where(set.Contains)];
    }
}
