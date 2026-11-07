using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Cards.Catalog;

/// <summary>
/// Calculates the analysis facts of a card from its layout and its faces. See docs/decisions/0025-multi-face-analysis-rules.md.
/// </summary>
public static class CardAnalysisRules
{
    /// <summary>The card types, in the order of the type line.</summary>
    public static readonly IReadOnlyList<string> CardTypes =
        ["Artifact", "Battle", "Creature", "Enchantment", "Instant", "Kindred", "Land", "Planeswalker", "Sorcery", "Tribal"];

    private static readonly string[] ColorOrder = ["W", "U", "B", "R", "G"];

    // The player can cast each face or half of these layouts.
    // For the other layouts, for example transform, flip and meld, the player casts only the front face.
    private static readonly HashSet<string> AllFacesLayouts = new(StringComparer.Ordinal) { "modal_dfc", "split", "adventure", "prepare" };

    /// <summary>
    /// Calculates the analysis facts of a card.
    /// </summary>
    /// <param name="layout">The Scryfall layout of the card.</param>
    /// <param name="faces">The faces of the card, in the order of their index. A card with one face has one face.</param>
    /// <returns>The analysis facts.</returns>
    public static CardAnalysisFacts Calculate(string layout, IReadOnlyList<CardFaceFacts> faces)
    {
        ArgumentNullException.ThrowIfNull(faces);
        if (faces.Count == 0)
        {
            return new CardAnalysisFacts([], 0, 0, false, false, [], [], null);
        }

        int[] castable = AllFacesLayouts.Contains(layout) && faces.Count > 1
            ? [.. faces.Select(face => face.Index)]
            : [faces[0].Index];

        var primary = faces[0];
        if (layout == "split")
        {
            // The player casts one half of a split card or a Room. The curve uses the cheapest half.
            primary = faces.Where(face => castable.Contains(face.Index)).OrderBy(face => face.ManaValue).ThenBy(face => face.Index).First();
        }

        var isLand = TypesOf(faces[0].TypeLine).Contains("Land");
        var isModalLand = layout == "modal_dfc" && !isLand && faces.Skip(1).Any(face => TypesOf(face.TypeLine).Contains("Land"));

        var castableFaces = faces.Where(face => castable.Contains(face.Index)).ToList();
        var types = castableFaces.SelectMany(face => TypesOf(face.TypeLine)).Distinct(StringComparer.Ordinal)
            .OrderBy(type => IndexOf(CardTypes, type)).ToList();
        var colors = castableFaces.SelectMany(face => face.Colors).Distinct(StringComparer.Ordinal)
            .OrderBy(color => IndexOf(ColorOrder, color)).ToList();

        return new CardAnalysisFacts(castable, primary.Index, primary.ManaValue, isLand, isModalLand, types, colors, primary.ManaCost);
    }

    /// <summary>
    /// Gets the card types of a type line, for example <c>Creature</c> and <c>Land</c> for "Land Creature — Forest Dryad".
    /// </summary>
    /// <param name="typeLine">The type line of one face.</param>
    /// <returns>The card types.</returns>
    public static IReadOnlyList<string> TypesOf(string? typeLine)
    {
        if (string.IsNullOrWhiteSpace(typeLine))
        {
            return [];
        }

        var dash = typeLine.IndexOf('—', StringComparison.Ordinal);
        var main = dash >= 0 ? typeLine[..dash] : typeLine;
        return [.. main.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => CardTypes.Contains(word))];
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index] == value)
            {
                return index;
            }
        }

        return list.Count;
    }
}
