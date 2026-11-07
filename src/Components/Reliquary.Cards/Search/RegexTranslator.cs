using System.Text;
using System.Text.RegularExpressions;

namespace Reliquary.Cards.Search;

/// <summary>
/// Changes a regular expression of the Scryfall flavor into a PostgreSQL advanced regular expression (ARE).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The result starts with the embedded option <c>(?n)</c>: the dot does not match a new line,
/// and <c>^</c> and <c>$</c> match at the start and the end of each line.</item>
/// <item><c>\b</c> and <c>\B</c> become <c>\y</c> and <c>\Y</c>, the word boundaries of PostgreSQL.</item>
/// <item>A brace that does not start a bound such as <c>{2,3}</c> becomes a literal brace, so <c>{T}</c> matches the tap symbol.</item>
/// <item>The Scryfall extensions <c>\sm</c>, <c>\sc</c>, <c>\ss</c>, <c>\smr</c>, <c>\smh</c>, <c>\smp</c>, <c>\spt</c>, <c>\spp</c> and <c>\smm</c> become standard expressions.</item>
/// <item><c>~</c> becomes <see cref="NamePlaceholder"/>. The SQL query puts the name of the card in its place.</item>
/// </list>
/// The search uses the operator <c>~*</c>, so the comparison ignores the case.
/// </remarks>
internal static partial class RegexTranslator
{
    /// <summary>The text that takes the place of <c>~</c>. The SQL query replaces it with the name of the card.</summary>
    public const char NamePlaceholder = '\u0001';

    private static readonly (string Escape, string Expression)[] Extensions =
    [
        ("smr", @"(\{(?:[wubrgcxyzs]|[0-9]+|[wubrgc2]/[wubrgp](?:/p)?)\})\"),
        ("smh", @"\{(?:[wubrgc2]/[wubrg])(?:/p)?\}"),
        ("smp", @"\{(?:[^}/]+/)*p\}"),
        ("smm", @"-[0-9x*]+/-[0-9x*]+"),
        ("spt", @"[-+]?[0-9x*]+/[-+]?[0-9x*]+"),
        ("spp", @"\+[0-9x*]+/\+[0-9x*]+"),
        ("sm", @"\{(?:[wubrgcxyzs]|[0-9]+|[wubrgc2]/[wubrgp](?:/p)?)\}"),
        ("sc", @"\{[^}]*[wubrg][^}]*\}"),
        ("ss", @"\{[^}]+\}"),
    ];

    /// <summary>
    /// Changes a Scryfall regular expression into a PostgreSQL regular expression.
    /// </summary>
    /// <param name="pattern">The regular expression, without the slashes.</param>
    /// <param name="usesName">Whether the expression contains <c>~</c>, the name of the card.</param>
    /// <returns>The PostgreSQL regular expression.</returns>
    public static string Translate(string pattern, out bool usesName)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        usesName = false;
        var builder = new StringBuilder("(?n)", pattern.Length + 16);
        var groups = 0;
        var index = 0;
        while (index < pattern.Length)
        {
            var character = pattern[index];
            switch (character)
            {
                case '\\' when index + 1 < pattern.Length:
                    index = TranslateEscape(pattern, index, builder, ref groups);
                    continue;
                case '[':
                    index = CopyBracket(pattern, index, builder);
                    continue;
                case '{':
                    var bound = BoundPattern().Match(pattern, index);
                    if (bound.Success && bound.Index == index)
                    {
                        builder.Append(bound.Value);
                        index += bound.Length;
                    }
                    else
                    {
                        builder.Append(@"\{");
                        index++;
                    }

                    continue;
                case '}':
                    builder.Append(@"\}");
                    break;
                case '(':
                    if (index + 1 >= pattern.Length || pattern[index + 1] != '?')
                    {
                        groups++;
                    }

                    builder.Append(character);
                    break;
                case '~':
                    usesName = true;
                    builder.Append(NamePlaceholder);
                    break;
                default:
                    builder.Append(character);
                    break;
            }

            index++;
        }

        return builder.ToString();
    }

    private static int TranslateEscape(string pattern, int index, StringBuilder builder, ref int groups)
    {
        var next = pattern[index + 1];
        if (next == 's')
        {
            foreach (var (escape, expression) in Extensions)
            {
                if (string.CompareOrdinal(pattern, index + 1, escape, 0, escape.Length) == 0)
                {
                    if (escape == "smr")
                    {
                        // A repeated symbol: a group, then a back reference to the group.
                        groups++;
                        builder.Append(expression).Append(groups.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(expression);
                    }

                    return index + 1 + escape.Length;
                }
            }
        }

        switch (next)
        {
            case '/':
                builder.Append('/');
                break;
            case 'b':
                builder.Append(@"\y");
                break;
            case 'B':
                builder.Append(@"\Y");
                break;
            default:
                builder.Append('\\').Append(next);
                break;
        }

        return index + 2;
    }

    private static int CopyBracket(string pattern, int index, StringBuilder builder)
    {
        var start = index;
        index++;
        if (index < pattern.Length && pattern[index] == '^')
        {
            index++;
        }

        // A closing bracket at the first place is a literal character.
        if (index < pattern.Length && pattern[index] == ']')
        {
            index++;
        }

        while (index < pattern.Length && pattern[index] != ']')
        {
            index += pattern[index] == '\\' && index + 1 < pattern.Length ? 2 : 1;
        }

        var end = Math.Min(index + 1, pattern.Length);
        builder.Append(pattern, start, end - start);
        return end;
    }

    [GeneratedRegex(@"\{[0-9]+(,[0-9]*)?\}")]
    private static partial Regex BoundPattern();
}
