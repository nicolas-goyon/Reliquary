using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Decks.Analysis;

/// <summary>
/// Analyzes a deck from its lines and the facts of its cards. The analyzer has no input or output operations.
/// </summary>
/// <remarks>
/// The analysis reads the commander, companion and main zones. It applies the rules of ADR 0025 for the cards with more than one face.
/// See docs/architecture/deck-analysis.md.
/// </remarks>
public static class DeckAnalyzer
{
    /// <summary>The highest point of the mana curve. This point also counts the cards with a higher mana value.</summary>
    public const int MaxCurveManaValue = 7;

    /// <summary>The key of the cards that are not lands and have no category.</summary>
    public const string Uncategorized = "uncategorized";

    /// <summary>The key of the colorless cards in the color distribution.</summary>
    public const string Colorless = "C";

    /// <summary>The highest number of lands in the draw probabilities.</summary>
    public const int MaxLandsInProbabilities = 7;

    /// <summary>The part of a source that a mana creature gives. Frank Karsten counts it as half a source.</summary>
    public const decimal CreatureSourceWeight = 0.5m;

    /// <summary>The part of a source that another mana permanent gives, for example a mana rock. Frank Karsten counts it as three-fourths of a source.</summary>
    public const decimal PermanentSourceWeight = 0.75m;

    /// <summary>The color has enough sources for its requirement.</summary>
    public const string Ok = "ok";

    /// <summary>The color has fewer sources than its requirement.</summary>
    public const string Low = "low";

    /// <summary>No card needs mana of the color.</summary>
    public const string NoRequirement = "no_requirement";

    /// <summary>The zones of the analysis.</summary>
    public static readonly IReadOnlyList<string> Zones = ["commander", "companion", "main"];

    /// <summary>
    /// The 100-card Commander formats. They use the 99-card table of Frank Karsten, the category needs and the bracket.
    /// </summary>
    public static readonly IReadOnlyList<string> CommanderFormats = ["commander", "duel", "predh", "paupercommander", "brawl"];

    private static readonly string[] ColorOrder = ["W", "U", "B", "R", "G"];

    private static readonly (string Code, int Turn, bool OnThePlay)[] DrawCheckpoints =
    [
        ("opening_hand", 1, true),
        ("turn_2_play", 2, true),
        ("turn_2_draw", 2, false),
        ("turn_3_play", 3, true),
        ("turn_3_draw", 3, false),
        ("turn_4_play", 4, true),
        ("turn_4_draw", 4, false),
    ];

    /// <summary>
    /// Analyzes a deck.
    /// </summary>
    /// <param name="format">The format of the deck.</param>
    /// <param name="lines">The lines of the deck. The lines of the other zones and the lines of unknown cards have no effect.</param>
    /// <param name="cards">The facts of the cards of the lines.</param>
    /// <returns>The analysis.</returns>
    public static DeckAnalysis Analyze(string format, IEnumerable<AnalysisLine> lines, IReadOnlyDictionary<Guid, CardFacts> cards)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(cards);
        var deck = lines
            .Where(line => line.Quantity > 0 && Zones.Contains(line.Zone))
            .Select(line => (Line: line, Card: cards.GetValueOrDefault(line.OracleId)))
            .Where(entry => entry.Card is { IsExtra: false })
            .Select(entry => (entry.Line, Card: entry.Card!))
            .ToList();
        var isCommander = CommanderFormats.Contains(format);

        var cardCount = deck.Sum(entry => entry.Line.Quantity);
        var landCount = deck.Where(entry => IsLand(entry.Card)).Sum(entry => entry.Line.Quantity);
        var modalLandCount = deck.Where(entry => entry.Card.Analysis.IsModalLand).Sum(entry => entry.Line.Quantity);
        var spells = deck.Where(entry => !entry.Card.Analysis.IsLand).ToList();
        var spellCount = spells.Sum(entry => entry.Line.Quantity);
        var averageManaValue = spellCount == 0
            ? 0m
            : Math.Round(spells.Sum(entry => entry.Card.Analysis.CurveManaValue * entry.Line.Quantity) / spellCount, 2, MidpointRounding.AwayFromZero);

        var categories = CountCategories(deck);
        IReadOnlyList<CategoryNeed>? needs = null;
        BracketAnalysis? bracket = null;
        if (isCommander)
        {
            var cheap = spells
                .Where(entry => entry.Card.Analysis.CurveManaValue <= CommanderNeeds.CheapManaValue
                    && entry.Line.Categories.Any(category => category is "ramp" or "card_draw"))
                .Sum(entry => entry.Line.Quantity);
            needs = CommanderNeeds.Calculate(categories.ToDictionary(entry => entry.Key, entry => entry.Count, StringComparer.Ordinal), averageManaValue, cheap);
            bracket = CommanderBracket.Evaluate(deck.Select(entry => entry.Card));
        }

        return new DeckAnalysis(
            format,
            cardCount,
            landCount,
            modalLandCount,
            spellCount,
            averageManaValue,
            Curve(spells),
            CountTypes(deck),
            CountColors(spells),
            categories,
            Mana(deck, isCommander ? 99 : 60),
            Draws(deck),
            needs,
            bracket);
    }

    /// <summary>
    /// Tells whether a card counts as a land: a land, or a modal land (ADR 0025, rule 3).
    /// </summary>
    /// <param name="card">The card.</param>
    /// <returns><see langword="true"/> for a land or a modal land.</returns>
    public static bool IsLand(CardFacts card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Analysis.IsLand || card.Analysis.IsModalLand;
    }

    private static List<CurvePoint> Curve(List<(AnalysisLine Line, CardFacts Card)> spells)
    {
        var counts = new int[MaxCurveManaValue + 1];
        foreach (var (line, card) in spells)
        {
            var point = (int)Math.Clamp(Math.Floor(card.Analysis.CurveManaValue), 0, MaxCurveManaValue);
            counts[point] += line.Quantity;
        }

        return [.. counts.Select((count, manaValue) => new CurvePoint(manaValue, count))];
    }

    private static List<CountEntry> CountTypes(List<(AnalysisLine Line, CardFacts Card)> deck)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var (line, card) in deck)
        {
            foreach (var type in card.Analysis.CardTypes.Select(TypeKey).Distinct(StringComparer.Ordinal))
            {
                if (!counts.ContainsKey(type))
                {
                    order.Add(type);
                }

                counts[type] = counts.GetValueOrDefault(type) + line.Quantity;
            }
        }

        return [.. order.Order(StringComparer.Ordinal).Select(type => new CountEntry(type, counts[type]))];
    }

    private static string TypeKey(string type) => type == "Tribal" ? "kindred" : type.ToLowerInvariant();

    private static List<CountEntry> CountColors(List<(AnalysisLine Line, CardFacts Card)> spells)
    {
        var counts = ColorOrder.Append(Colorless).ToDictionary(color => color, _ => 0, StringComparer.Ordinal);
        foreach (var (line, card) in spells)
        {
            var colors = card.Analysis.Colors.Where(counts.ContainsKey).ToList();
            if (colors.Count == 0)
            {
                counts[Colorless] += line.Quantity;
            }

            foreach (var color in colors)
            {
                counts[color] += line.Quantity;
            }
        }

        return [.. counts.Select(pair => new CountEntry(pair.Key, pair.Value))];
    }

    /// <summary>
    /// Counts the cards of each category. Each land and each modal land is in the category <c>land</c>, also with no category on its line.
    /// </summary>
    private static List<CountEntry> CountCategories(List<(AnalysisLine Line, CardFacts Card)> deck)
    {
        var counts = DeckRules.Categories.Append(Uncategorized).ToDictionary(category => category, _ => 0, StringComparer.Ordinal);
        foreach (var (line, card) in deck)
        {
            var categories = line.Categories.Where(counts.ContainsKey).Where(category => category != Uncategorized).ToHashSet(StringComparer.Ordinal);
            if (IsLand(card))
            {
                categories.Add("land");
            }

            if (categories.Count == 0)
            {
                counts[Uncategorized] += line.Quantity;
            }

            foreach (var category in categories)
            {
                counts[category] += line.Quantity;
            }
        }

        return [.. counts.Select(pair => new CountEntry(pair.Key, pair.Value))];
    }

    private static ManaAnalysis Mana(List<(AnalysisLine Line, CardFacts Card)> deck, int tableDeckSize)
    {
        // The sources are the cards of the library: the main zone.
        var library = deck.Where(entry => entry.Line.Zone == "main").ToList();
        var lands = library.Where(entry => IsLand(entry.Card)).ToList();
        var permanents = library
            .Where(entry => !IsLand(entry.Card) && entry.Card.ProducedMana.Count > 0 && IsPermanent(entry.Card))
            .Select(entry => (entry.Line.Quantity, entry.Card.ProducedMana, entry.Card.Analysis.CurveManaValue, Weight: IsCreature(entry.Card) ? CreatureSourceWeight : PermanentSourceWeight))
            .ToList();

        // The requirements are the cards that are not lands, in the three zones.
        var requirements = deck
            .Where(entry => !entry.Card.Analysis.IsLand)
            .Select(entry => (entry.Line.Quantity, entry.Card, Symbols: ManaRequirements.CountSymbols(entry.Card.Analysis.PrimaryManaCost)))
            .Where(entry => entry.Symbols.Count > 0)
            .ToList();

        var result = new List<ColorMana>();
        foreach (var color in ManaRequirements.Colors)
        {
            var landSources = lands.Where(entry => entry.Card.ProducedMana.Contains(color)).Sum(entry => entry.Line.Quantity);
            var producers = permanents.Where(entry => entry.ProducedMana.Contains(color)).ToList();
            var symbols = requirements.Sum(entry => entry.Symbols.GetValueOrDefault(color) * entry.Quantity);
            if (landSources == 0 && producers.Count == 0 && symbols == 0)
            {
                continue;
            }

            // A permanent with mana value V helps to cast a card with a mana value higher than V.
            decimal SourcesFor(decimal manaValue) =>
                landSources + producers.Where(entry => entry.CurveManaValue < manaValue).Sum(entry => entry.Weight * entry.Quantity);

            var candidates = requirements
                .Where(entry => entry.Symbols.ContainsKey(color))
                .Select(entry =>
                {
                    var pips = entry.Symbols[color];
                    var manaValue = entry.Card.Analysis.CurveManaValue;

                    // Frank Karsten adds one source to each color of a card that needs two colors or more.
                    var gold = color != Colorless && entry.Symbols.Keys.Count(key => key != Colorless) > 1 ? 1 : 0;
                    var required = ManaRequirements.Required(tableDeckSize, pips, (int)Math.Floor(manaValue)) + gold;
                    return (entry.Card, Pips: pips, ManaValue: manaValue, Required: required, Sources: SourcesFor(manaValue));
                })
                .OrderByDescending(entry => entry.Required - entry.Sources)
                .ThenByDescending(entry => entry.Required)
                .ThenBy(entry => entry.ManaValue)
                .ThenBy(entry => entry.Card.Name, StringComparer.Ordinal)
                .ToList();

            var otherSources = producers.Sum(entry => entry.Quantity);
            if (candidates.Count == 0)
            {
                result.Add(new ColorMana(color, symbols, landSources, otherSources, landSources, null, null, null, null, NoRequirement));
                continue;
            }

            // The card with the largest difference between its requirement and its sources sets the result of the color.
            var need = candidates[0];
            result.Add(new ColorMana(
                color,
                symbols,
                landSources,
                otherSources,
                need.Sources,
                need.Required,
                need.Card.OracleId,
                need.Pips,
                need.ManaValue,
                need.Sources >= need.Required ? Ok : Low));
        }

        return new ManaAnalysis(tableDeckSize, result);
    }

    private static bool IsPermanent(CardFacts card)
    {
        var types = FrontTypeLine(card);
        return !types.Contains("Instant", StringComparison.Ordinal) && !types.Contains("Sorcery", StringComparison.Ordinal);
    }

    private static bool IsCreature(CardFacts card) => FrontTypeLine(card).Contains("Creature", StringComparison.Ordinal);

    private static string FrontTypeLine(CardFacts card) => (card.Faces.Count > 0 ? card.Faces[0].TypeLine : card.TypeLine) ?? string.Empty;

    private static DrawAnalysis Draws(List<(AnalysisLine Line, CardFacts Card)> deck)
    {
        var library = deck.Where(entry => entry.Line.Zone == "main").ToList();
        var size = library.Sum(entry => entry.Line.Quantity);
        var lands = library.Where(entry => IsLand(entry.Card)).Sum(entry => entry.Line.Quantity);
        var checkpoints = DrawCheckpoints.Select(checkpoint =>
        {
            // The opening hand has 7 cards. The player draws one card each turn, except on turn 1 on the play.
            var seen = 7 + checkpoint.Turn - (checkpoint.OnThePlay ? 1 : 0);
            var draws = Math.Min(seen, size);
            var probabilities = Hypergeometric.AtLeastEach(size, lands, draws, MaxLandsInProbabilities);
            var atLeast = Enumerable.Range(1, MaxLandsInProbabilities)
                .Select(count => new LandProbability(count, Math.Round(probabilities[count], 4)))
                .ToList();
            return new DrawCheckpoint(checkpoint.Code, checkpoint.Turn, checkpoint.OnThePlay, seen, atLeast);
        }).ToList();
        return new DrawAnalysis(size, lands, checkpoints);
    }
}
