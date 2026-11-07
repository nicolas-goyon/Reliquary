using System.Globalization;
using System.Text.RegularExpressions;

namespace Reliquary.Cards.Text;

/// <summary>
/// Reads mana costs such as <c>{2}{W}{U/P}</c>.
/// </summary>
public static partial class ManaCost
{
    /// <summary>The bit of each color in a color mask: W, U, B, R, G, then C for colorless mana.</summary>
    public static readonly IReadOnlyDictionary<char, short> ColorBits = new Dictionary<char, short>
    {
        ['W'] = 1,
        ['U'] = 2,
        ['B'] = 4,
        ['R'] = 8,
        ['G'] = 16,
        ['C'] = 32,
    };

    /// <summary>
    /// Gets the symbols of a mana cost, without the braces.
    /// </summary>
    /// <param name="cost">The mana cost, for example <c>{2}{W}</c>.</param>
    /// <returns>The symbols, for example <c>2</c> and <c>W</c>.</returns>
    public static IEnumerable<string> Symbols(string? cost) =>
        string.IsNullOrEmpty(cost) ? [] : SymbolPattern().Matches(cost).Select(match => match.Groups[1].Value);

    /// <summary>
    /// Gets the mana value of a mana cost, by the rules of the game.
    /// </summary>
    /// <remarks>
    /// A number counts as its value. X, Y and Z count as 0. A hybrid symbol with a number, for example <c>{2/W}</c>, counts as the number.
    /// A half symbol counts as one half. All other symbols count as 1.
    /// </remarks>
    /// <param name="cost">The mana cost.</param>
    /// <returns>The mana value.</returns>
    public static decimal Value(string? cost)
    {
        decimal total = 0;
        foreach (var symbol in Symbols(cost))
        {
            total += SymbolValue(symbol);
        }

        return total;
    }

    /// <summary>
    /// Gets the colors of a mana cost: each of W, U, B, R and G that a symbol contains, in the order WUBRG.
    /// </summary>
    /// <remarks>
    /// Scryfall gives no colors for the halves of a split card or an Adventure. Their colors come from their mana cost.
    /// </remarks>
    /// <param name="cost">The mana cost, for example <c>{1}{W/U}</c>.</param>
    /// <returns>The colors, for example W and U.</returns>
    public static IReadOnlyList<string> Colors(string? cost)
    {
        var symbols = string.Concat(Symbols(cost));
        return [.. "WUBRG".Where(color => symbols.Contains(color, StringComparison.Ordinal)).Select(color => color.ToString())];
    }

    /// <summary>
    /// Gets the color mask of a list of colors, for example W and U give 3.
    /// </summary>
    /// <param name="colors">The color letters.</param>
    /// <returns>The mask.</returns>
    public static short Mask(IEnumerable<string>? colors)
    {
        short mask = 0;
        foreach (var color in colors ?? [])
        {
            if (color.Length == 1 && ColorBits.TryGetValue(char.ToUpperInvariant(color[0]), out var bit))
            {
                mask |= bit;
            }
        }

        return mask;
    }

    private static decimal SymbolValue(string symbol)
    {
        if (decimal.TryParse(symbol, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        if (symbol is "X" or "Y" or "Z")
        {
            return 0;
        }

        if (symbol is "½" || symbol.StartsWith('H'))
        {
            return 0.5m;
        }

        var slash = symbol.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0 && decimal.TryParse(symbol[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hybrid))
        {
            return hybrid;
        }

        return 1;
    }

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex SymbolPattern();
}
