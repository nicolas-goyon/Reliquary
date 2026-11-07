using System.Text.RegularExpressions;
using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Decks.Legality;

/// <summary>
/// A problem that makes a deck not legal in its format.
/// </summary>
/// <param name="Code">The stable code of the problem, for the translation.</param>
/// <param name="OracleId">The card of the problem, or <see langword="null"/> for a problem of the deck.</param>
/// <param name="Count">The actual count, for a problem of a count.</param>
/// <param name="Limit">The permitted count, for a problem of a count.</param>
public sealed record LegalityProblem(string Code, Guid? OracleId = null, int? Count = null, int? Limit = null);

/// <summary>
/// The rules of a format for the legality check.
/// </summary>
/// <param name="MinCards">The minimum number of cards in the deck, with the commanders.</param>
/// <param name="MaxCards">The maximum number of cards in the deck, or <see langword="null"/> for no maximum.</param>
/// <param name="MaxCopies">The maximum number of copies of a card.</param>
/// <param name="MaxSideboard">The maximum number of cards in the sideboard, or <see langword="null"/> for no sideboard check.</param>
/// <param name="HasCommander">Whether the deck has commanders and a color identity.</param>
internal sealed record FormatRules(int MinCards, int? MaxCards, int MaxCopies, int? MaxSideboard, bool HasCommander);

/// <summary>
/// Checks the legality of a deck in its format: deck size, copies, card legalities, and the Commander rules.
/// </summary>
/// <remarks>
/// The check reads the commander, companion and main zones. The sideboard, considering and tokens zones are outside the deck,
/// except for the size of the sideboard in the constructed formats.
/// </remarks>
public static partial class DeckLegality
{
    /// <summary>The deck does not have the permitted number of cards.</summary>
    public const string DeckSize = "deck_size";

    /// <summary>The deck has too many copies of a card.</summary>
    public const string TooManyCopies = "too_many_copies";

    /// <summary>The sideboard has too many cards.</summary>
    public const string SideboardSize = "sideboard_size";

    /// <summary>The format bans the card.</summary>
    public const string CardBanned = "card_banned";

    /// <summary>The card is not legal in the format.</summary>
    public const string CardNotLegal = "card_not_legal";

    /// <summary>The format permits one copy only of a restricted card.</summary>
    public const string CardRestricted = "card_restricted";

    /// <summary>The deck has no commander.</summary>
    public const string CommanderMissing = "commander_missing";

    /// <summary>The card cannot be a commander.</summary>
    public const string CommanderNotAllowed = "commander_not_allowed";

    /// <summary>The deck has more commanders than the rules permit.</summary>
    public const string TooManyCommanders = "too_many_commanders";

    /// <summary>The two commanders cannot be together: no Partner, Friends forever, Background or Doctor pair.</summary>
    public const string CommandersCannotPair = "commanders_cannot_pair";

    /// <summary>The card is outside the color identity of the commanders.</summary>
    public const string OutsideColorIdentity = "outside_color_identity";

    /// <summary>The card in the companion zone has no companion ability.</summary>
    public const string CompanionNotAllowed = "companion_not_allowed";

    private static readonly FormatRules Commander = new(100, 100, 1, null, true);
    private static readonly FormatRules Constructed = new(60, null, 4, 15, false);

    private static readonly Dictionary<string, FormatRules> Formats = new(StringComparer.Ordinal)
    {
        ["commander"] = Commander,
        ["duel"] = Commander,
        ["predh"] = Commander,
        ["paupercommander"] = Commander,
        ["brawl"] = Commander,
        ["standardbrawl"] = new(60, 60, 1, null, true),
        ["oathbreaker"] = new(60, 60, 1, null, false),
        ["freeform"] = new(0, null, int.MaxValue, null, false),
    };

    private static readonly string[] PairKeywords = ["Partner", "Friends forever"];

    /// <summary>
    /// Checks a deck.
    /// </summary>
    /// <param name="format">The format of the deck.</param>
    /// <param name="lines">The lines: zone, card and quantity.</param>
    /// <param name="cards">The facts of the cards of the lines.</param>
    /// <returns>The problems. The deck is legal when the list is empty.</returns>
    public static IReadOnlyList<LegalityProblem> Check(string format, IEnumerable<(string Zone, Guid OracleId, int Quantity)> lines, IReadOnlyDictionary<Guid, CardFacts> cards)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(cards);
        var rules = Formats.GetValueOrDefault(format) ?? Constructed;
        var all = lines.Where(line => cards.ContainsKey(line.OracleId)).ToList();
        var deck = all.Where(line => line.Zone is "commander" or "companion" or "main").ToList();
        var problems = new List<LegalityProblem>();

        // The companion is outside the deck: it does not count in the size.
        var size = deck.Where(line => line.Zone != "companion").Sum(line => line.Quantity);
        if (size < rules.MinCards || (rules.MaxCards is { } max && size > max))
        {
            problems.Add(new LegalityProblem(DeckSize, Count: size, Limit: size < rules.MinCards ? rules.MinCards : rules.MaxCards));
        }

        if (rules.MaxSideboard is { } maxSideboard)
        {
            var sideboard = all.Where(line => line.Zone == "sideboard").Sum(line => line.Quantity);
            if (sideboard > maxSideboard)
            {
                problems.Add(new LegalityProblem(SideboardSize, Count: sideboard, Limit: maxSideboard));
            }
        }

        var counted = rules.MaxSideboard is null ? deck : [.. deck, .. all.Where(line => line.Zone == "sideboard")];
        foreach (var group in counted.GroupBy(line => line.OracleId).OrderBy(group => cards[group.Key].Name, StringComparer.Ordinal))
        {
            var card = cards[group.Key];
            var copies = group.Sum(line => line.Quantity);
            var legality = format == "freeform" ? "legal" : card.LegalityIn(format);
            if (legality == "banned")
            {
                problems.Add(new LegalityProblem(CardBanned, card.OracleId));
            }
            else if (legality == "not_legal")
            {
                problems.Add(new LegalityProblem(CardNotLegal, card.OracleId));
            }
            else if (legality == "restricted" && copies > 1)
            {
                problems.Add(new LegalityProblem(CardRestricted, card.OracleId, copies, 1));
            }

            var limit = CopyLimit(card, rules.MaxCopies);
            if (copies > limit)
            {
                problems.Add(new LegalityProblem(TooManyCopies, card.OracleId, copies, limit));
            }
        }

        if (rules.HasCommander)
        {
            CheckCommanders(problems, deck, cards);
        }

        foreach (var line in deck.Where(line => line.Zone == "companion"))
        {
            if (!cards[line.OracleId].Keywords.Contains("Companion", StringComparer.OrdinalIgnoreCase))
            {
                problems.Add(new LegalityProblem(CompanionNotAllowed, line.OracleId));
            }
        }

        return problems;
    }

    /// <summary>
    /// Gets the maximum number of copies of a card: basic lands and some cards have no limit, and some cards have their own limit.
    /// </summary>
    internal static int CopyLimit(CardFacts card, int formatLimit)
    {
        if (card.TypeLine?.Contains("Basic", StringComparison.Ordinal) == true)
        {
            return int.MaxValue;
        }

        var text = string.Join('\n', card.Faces.Select(face => face.OracleText));
        if (text.Contains("A deck can have any number of cards named", StringComparison.Ordinal))
        {
            return int.MaxValue;
        }

        var upTo = UpToPattern().Match(text);
        if (upTo.Success && Numbers.TryGetValue(upTo.Groups["count"].Value, out var count))
        {
            return count;
        }

        return formatLimit;
    }

    private static void CheckCommanders(List<LegalityProblem> problems, List<(string Zone, Guid OracleId, int Quantity)> deck, IReadOnlyDictionary<Guid, CardFacts> cards)
    {
        var commanders = deck.Where(line => line.Zone == "commander").Select(line => cards[line.OracleId]).ToList();
        if (commanders.Count == 0)
        {
            problems.Add(new LegalityProblem(CommanderMissing));
            return;
        }

        foreach (var commander in commanders.Where(commander => !CanBeCommander(commander)))
        {
            problems.Add(new LegalityProblem(CommanderNotAllowed, commander.OracleId));
        }

        if (commanders.Count > 2 || deck.Where(line => line.Zone == "commander").Sum(line => line.Quantity) > 2)
        {
            problems.Add(new LegalityProblem(TooManyCommanders, Count: commanders.Count, Limit: 2));
        }
        else if (commanders.Count == 2 && !CanPair(commanders[0], commanders[1]))
        {
            problems.Add(new LegalityProblem(CommandersCannotPair));
        }

        var identity = commanders.SelectMany(commander => commander.ColorIdentity).ToHashSet(StringComparer.Ordinal);
        foreach (var oracleId in deck.Where(line => line.Zone != "commander").Select(line => line.OracleId).Distinct())
        {
            if (!cards[oracleId].ColorIdentity.All(identity.Contains))
            {
                problems.Add(new LegalityProblem(OutsideColorIdentity, oracleId));
            }
        }
    }

    /// <summary>
    /// Tells whether a card can be a commander: a legendary creature, a Background with a pair, or a card that says so.
    /// </summary>
    internal static bool CanBeCommander(CardFacts card)
    {
        var front = card.Faces.Count > 0 ? card.Faces[0].TypeLine : card.TypeLine;
        if (front?.Contains("Legendary", StringComparison.Ordinal) == true && front.Contains("Creature", StringComparison.Ordinal))
        {
            return true;
        }

        if (card.TypeLine?.Contains("Background", StringComparison.Ordinal) == true)
        {
            return true;
        }

        return card.Faces.Any(face => face.OracleText?.Contains("can be your commander", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// Tells whether two cards can be the two commanders of a deck.
    /// </summary>
    internal static bool CanPair(CardFacts first, CardFacts second)
    {
        bool Has(CardFacts card, string keyword) => card.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);
        string Text(CardFacts card) => string.Join('\n', card.Faces.Select(face => face.OracleText));

        // "Partner with" pairs only with the named card. The keyword list of these cards also contains "Partner".
        if (Has(first, "Partner with") || Has(second, "Partner with"))
        {
            return Text(first).Contains($"Partner with {second.Name}", StringComparison.Ordinal)
                && Text(second).Contains($"Partner with {first.Name}", StringComparison.Ordinal);
        }

        foreach (var keyword in PairKeywords)
        {
            if (Has(first, keyword) && Has(second, keyword))
            {
                return true;
            }
        }

        bool IsBackground(CardFacts card) => card.TypeLine?.Contains("Background", StringComparison.Ordinal) == true;
        if ((Has(first, "Choose a background") && IsBackground(second)) || (Has(second, "Choose a background") && IsBackground(first)))
        {
            return true;
        }

        bool IsDoctor(CardFacts card) => card.TypeLine?.Contains("Time Lord Doctor", StringComparison.Ordinal) == true;
        return (Has(first, "Doctor's companion") && IsDoctor(second)) || (Has(second, "Doctor's companion") && IsDoctor(first));
    }

    private static readonly Dictionary<string, int> Numbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
        ["ten"] = 10,
    };

    [GeneratedRegex(@"A deck can have up to (?<count>\w+) cards named")]
    private static partial Regex UpToPattern();
}
