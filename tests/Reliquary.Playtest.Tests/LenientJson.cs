using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// A small JSON reader for the shared vectors. Unlike System.Text.Json, it accepts a lone surrogate in a string (for example "\udc00"),
/// as JavaScript does. The vectors of the canonical JSON have such a string.
/// </summary>
internal static class LenientJson
{
    public static JsonNode? Parse(string text)
    {
        var index = 0;
        var value = ReadValue(text, ref index);
        SkipSpace(text, ref index);
        return index == text.Length ? value : throw new FormatException($"The JSON text has more characters at the position {index}.");
    }

    private static JsonNode? ReadValue(string text, ref int index)
    {
        SkipSpace(text, ref index);
        switch (text[index])
        {
            case '{':
                var item = new JsonObject();
                index++;
                SkipSpace(text, ref index);
                if (text[index] == '}')
                {
                    index++;
                    return item;
                }

                while (true)
                {
                    SkipSpace(text, ref index);
                    var key = ReadString(text, ref index);
                    SkipSpace(text, ref index);
                    Expect(text, ref index, ':');
                    item[key] = ReadValue(text, ref index);
                    SkipSpace(text, ref index);
                    if (text[index++] == '}')
                    {
                        return item;
                    }
                }

            case '[':
                var items = new JsonArray();
                index++;
                SkipSpace(text, ref index);
                if (text[index] == ']')
                {
                    index++;
                    return items;
                }

                while (true)
                {
                    items.Add(ReadValue(text, ref index));
                    SkipSpace(text, ref index);
                    if (text[index++] == ']')
                    {
                        return items;
                    }
                }

            case '"':
                return JsonValue.Create(ReadString(text, ref index));
            case 't':
                index += 4;
                return JsonValue.Create(true);
            case 'f':
                index += 5;
                return JsonValue.Create(false);
            case 'n':
                index += 4;
                return null;
            default:
                var start = index;
                while (index < text.Length && "+-0123456789.eE".Contains(text[index], StringComparison.Ordinal))
                {
                    index++;
                }

                return JsonNode.Parse(text[start..index]);
        }
    }

    private static string ReadString(string text, ref int index)
    {
        Expect(text, ref index, '"');
        var builder = new StringBuilder();
        while (text[index] != '"')
        {
            var character = text[index++];
            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            var escape = text[index++];
            builder.Append(escape switch
            {
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'u' => (char)int.Parse(text.AsSpan(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                _ => escape,
            });
            if (escape == 'u')
            {
                index += 4;
            }
        }

        index++;
        return builder.ToString();
    }

    private static void SkipSpace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }

    private static void Expect(string text, ref int index, char expected)
    {
        if (text[index] != expected)
        {
            throw new FormatException($"The JSON text has '{text[index]}' at the position {index}, not '{expected}'.");
        }

        index++;
    }
}
