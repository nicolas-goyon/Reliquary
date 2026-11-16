using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Reliquary.Playtest.Views;

/// <summary>
/// The canonical JSON and the 64-bit FNV-1a hash of web/src/playtest/views.ts. The two sides must give the same text, byte for byte.
/// The shared vectors in tests/vectors/views keep the two sides equal.
/// </summary>
/// <remarks>
/// The rules (see <c>canonicalJson</c> in views.ts):
/// no whitespace; object keys in ordinal order of their UTF-16 code units; only safe integers, with no exponent and no decimal point
/// (minus zero is "0"); the strings escaped as JavaScript <c>JSON.stringify</c> escapes them; the bytes are the UTF-8 encoding of the text.
/// </remarks>
public static class CanonicalJson
{
    /// <summary>The largest safe integer of JavaScript: 2^53 - 1.</summary>
    private const double MaxSafeInteger = 9007199254740991;

    private const ulong FnvOffset = 0xcbf29ce484222325;
    private const ulong FnvPrime = 0x100000001b3;

    /// <summary>
    /// Writes a JSON value as canonical JSON.
    /// </summary>
    /// <param name="value">The value. <see langword="null"/> is the JSON null.</param>
    /// <returns>The canonical JSON text.</returns>
    /// <exception cref="ArgumentException">The value contains a number that is not a safe integer.</exception>
    public static string Write(JsonNode? value)
    {
        var builder = new StringBuilder();
        Write(builder, value);
        return builder.ToString();
    }

    /// <summary>
    /// Gives the 64-bit FNV-1a hash of the UTF-8 bytes of a text, as 16 lowercase hexadecimal digits.
    /// The hash finds bugs. It is not a security function.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The hash.</returns>
    public static string Fnv1a64(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var hash = FnvOffset;
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            hash = unchecked((hash ^ value) * FnvPrime);
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static void Write(StringBuilder builder, JsonNode? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject item:
                builder.Append('{');
                var first = true;
                foreach (var property in item.OrderBy(property => property.Key, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteString(builder, property.Key);
                    builder.Append(':');
                    Write(builder, property.Value);
                }

                builder.Append('}');
                break;
            case JsonArray items:
                builder.Append('[');
                for (var index = 0; index < items.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    Write(builder, items[index]);
                }

                builder.Append(']');
                break;
            case JsonValue primitive:
                WriteValue(builder, primitive);
                break;
            default:
                throw new ArgumentException($"Canonical JSON does not accept the node {value.GetType().Name}.", nameof(value));
        }
    }

    private static void WriteValue(StringBuilder builder, JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            case JsonValueKind.String:
                WriteString(builder, value.GetValue<string>());
                break;
            case JsonValueKind.Number:
                // The JSON text of the number works for all the .NET number types of a JsonValue.
                var number = double.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                if (Math.Floor(number) != number || Math.Abs(number) > MaxSafeInteger)
                {
                    throw new ArgumentException($"Canonical JSON accepts only safe integers. The value is {value.ToJsonString()}.", nameof(value));
                }

                // (long)-0.0 is 0, as String(-0) in JavaScript.
                builder.Append(((long)number).ToString(CultureInfo.InvariantCulture));
                break;
            default:
                throw new ArgumentException($"Canonical JSON does not accept the value {value.ToJsonString()}.", nameof(value));
        }
    }

    private static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case < ' ':
                    AppendUnicodeEscape(builder, character);
                    break;
                default:
                    if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                    {
                        // A surrogate pair is one character: it goes to the output as it is.
                        builder.Append(character).Append(text[index + 1]);
                        index++;
                    }
                    else if (char.IsSurrogate(character))
                    {
                        // A lone surrogate cannot go to UTF-8.
                        AppendUnicodeEscape(builder, character);
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static void AppendUnicodeEscape(StringBuilder builder, char character) =>
        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
}
