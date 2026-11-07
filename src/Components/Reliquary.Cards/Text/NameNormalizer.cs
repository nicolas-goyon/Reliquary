using System.Globalization;
using System.Text;

namespace Reliquary.Cards.Text;

/// <summary>
/// Normalizes card names for the comparison: the import and the search use the same rules.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The text becomes lowercase.</item>
/// <item>Latin, Greek and Cyrillic letters lose their accents: "Éclair" becomes "eclair".
/// Other scripts keep their marks, because a Japanese mark changes the letter.</item>
/// <item>Typographic apostrophes and quotation marks become simple ones, and dashes become a hyphen.</item>
/// <item>Ligatures become two letters: "Æther" becomes "aether".</item>
/// <item>Spaces become one space, and the text has no space at the start or the end.</item>
/// </list>
/// </remarks>
public static class NameNormalizer
{
    /// <summary>
    /// Normalizes a card name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The normalized name.</returns>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var decomposed = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastBase = '\0';
        var lastWasSpace = true;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                // Remove the accents of Latin, Greek and Cyrillic letters only.
                if (lastBase < '԰')
                {
                    continue;
                }

                builder.Append(character);
                continue;
            }

            lastBase = character;

            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            lastWasSpace = false;
            switch (character)
            {
                case '‘' or '’' or 'ʼ' or '`' or '´':
                    builder.Append('\'');
                    break;
                case '“' or '”' or '«' or '»':
                    builder.Append('"');
                    break;
                case '‐' or '‑' or '‒' or '–' or '—' or '−':
                    builder.Append('-');
                    break;
                case 'Æ' or 'æ':
                    builder.Append("ae");
                    break;
                case 'Œ' or 'œ':
                    builder.Append("oe");
                    break;
                case 'ß':
                    builder.Append("ss");
                    break;
                default:
                    builder.Append(char.ToLowerInvariant(character));
                    break;
            }
        }

        return builder.ToString().TrimEnd().Normalize(NormalizationForm.FormC);
    }
}
