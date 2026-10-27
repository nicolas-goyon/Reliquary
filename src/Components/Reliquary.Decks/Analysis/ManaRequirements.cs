using System.Text.RegularExpressions;

namespace Reliquary.Decks.Analysis;

/// <summary>
/// The colored mana requirements of the cards, with the method of Frank Karsten.
/// </summary>
/// <remarks>
/// Source: Frank Karsten, "How Many Sources Do You Need to Consistently Cast Your Spells? A 2022 Update", TCGplayer, 2 August 2022.
/// A deck can cast a card with mana value M on curve when it has a probability of (89 + M) percent or more to draw
/// the colored sources of the card by turn M on the play, if it draws M lands by that turn.
/// See docs/architecture/deck-analysis.md.
/// </remarks>
public static partial class ManaRequirements
{
    /// <summary>The colors of the requirements and of the sources, in the order of the result.</summary>
    public static readonly IReadOnlyList<string> Colors = ["W", "U", "B", "R", "G", "C"];

    // The tables give the sources for each number of symbols of one color (the row) and each mana value (the column).
    // The first column of a row is the mana value that is equal to the number of symbols, for example CC at mana value 2.
    // A higher mana value than the last column uses the last column.
    private static readonly int[][] Table99 =
    [
        [19, 19, 18, 16, 15, 14], // C, 1C, 2C, 3C, 4C, 5C
        [30, 28, 26, 23, 22, 20], // CC, 1CC, 2CC, 3CC, 4CC, 5CC
        [36, 33, 30, 28, 26], // CCC, 1CCC, 2CCC, 3CCC, 4CCC
        [39, 36], // CCCC, 1CCCC
    ];

    private static readonly int[][] Table60 =
    [
        [14, 13, 12, 10, 9, 9],
        [21, 18, 16, 15, 13, 12],
        [23, 21, 19, 17, 16],
        [24, 22],
    ];

    /// <summary>
    /// Counts the symbols of each color in a mana cost.
    /// </summary>
    /// <remarks>
    /// Only the symbols that need one color count: <c>{W}</c>, <c>{U}</c>, <c>{B}</c>, <c>{R}</c>, <c>{G}</c> and <c>{C}</c>.
    /// A hybrid symbol such as <c>{W/U}</c> accepts two colors, a Phyrexian symbol such as <c>{W/P}</c> accepts life,
    /// and a symbol such as <c>{2/W}</c> accepts generic mana. These symbols do not count.
    /// </remarks>
    /// <param name="manaCost">The mana cost, for example <c>{1}{W}{W}</c>, or <see langword="null"/>.</param>
    /// <returns>The number of symbols of each color. A color with no symbol is not in the result.</returns>
    public static IReadOnlyDictionary<string, int> CountSymbols(string? manaCost)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(manaCost))
        {
            return counts;
        }

        foreach (Match match in SymbolPattern().Matches(manaCost))
        {
            var symbol = match.Groups["symbol"].Value.ToUpperInvariant();
            if (Colors.Contains(symbol))
            {
                counts[symbol] = counts.GetValueOrDefault(symbol) + 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// Gets the number of sources of one color that a deck needs to cast a card on curve, from the tables of Frank Karsten.
    /// </summary>
    /// <param name="tableDeckSize">The deck size of the table: 99 for the Commander decks, 60 for the other decks.</param>
    /// <param name="pips">The number of symbols of the color in the mana cost. More than 4 symbols use the row of 4 symbols.</param>
    /// <param name="manaValue">The mana value of the card. A mana value lower than the number of symbols uses the number of symbols.</param>
    /// <returns>The number of sources, or 0 when <paramref name="pips"/> is 0.</returns>
    public static int Required(int tableDeckSize, int pips, int manaValue)
    {
        if (pips <= 0)
        {
            return 0;
        }

        var row = (tableDeckSize >= 99 ? Table99 : Table60)[Math.Min(pips, 4) - 1];
        var column = Math.Clamp(manaValue - pips, 0, row.Length - 1);
        return row[column];
    }

    [GeneratedRegex(@"\{(?<symbol>[^}]+)\}")]
    private static partial Regex SymbolPattern();
}
