using System.Text;

namespace Reliquary.Cards.Search;

/// <summary>
/// The kind of a token of a search query.
/// </summary>
internal enum SearchTokenKind
{
    LeftParenthesis,
    RightParenthesis,
    Minus,
    Or,
    And,
    Term,
}

/// <summary>
/// A token of a search query.
/// </summary>
/// <param name="Kind">The kind of the token.</param>
/// <param name="Position">The index of the first character.</param>
/// <param name="Length">The number of characters.</param>
/// <param name="Node">The term, for a token of the kind <see cref="SearchTokenKind.Term"/>.</param>
internal sealed record SearchToken(SearchTokenKind Kind, int Position, int Length, SearchNode? Node = null);

/// <summary>
/// Splits a search query into tokens: parentheses, the minus sign, <c>or</c>, <c>and</c> and the terms.
/// </summary>
/// <remarks>
/// The lexer reads each term completely, with its keyword, its operator and its value.
/// It does not know the keywords: the compiler checks them.
/// </remarks>
internal sealed class SearchLexer
{
    /// <summary>The keywords that accept a regular expression between slashes.</summary>
    private static readonly HashSet<string> RegexKeywords = new(StringComparer.Ordinal)
    {
        "t", "type", "o", "oracle", "fo", "fulloracle", "ft", "flavor", "name",
    };

    private readonly string query;
    private readonly List<SearchToken> tokens = [];
    private int index;

    private SearchLexer(string query) => this.query = query;

    /// <summary>
    /// Splits a query into tokens.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="tokens">The tokens, when the query has no syntax error.</param>
    /// <param name="error">The syntax error, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the query has no syntax error.</returns>
    public static bool TryTokenize(string query, out IReadOnlyList<SearchToken> tokens, out SearchSyntaxError? error)
    {
        var lexer = new SearchLexer(query);
        error = lexer.Run();
        tokens = lexer.tokens;
        return error is null;
    }

    private static bool IsSeparator(char character) => char.IsWhiteSpace(character) || character is '(' or ')';

    private static bool IsKeywordCharacter(char character) => char.IsAsciiLetter(character) || character == '_';

    private SearchSyntaxError? Run()
    {
        while (true)
        {
            while (index < query.Length && char.IsWhiteSpace(query[index]))
            {
                index++;
            }

            if (index >= query.Length)
            {
                return null;
            }

            var start = index;
            var character = query[index];
            SearchSyntaxError? error = null;
            switch (character)
            {
                case '(':
                    tokens.Add(new SearchToken(SearchTokenKind.LeftParenthesis, start, 1));
                    index++;
                    break;
                case ')':
                    tokens.Add(new SearchToken(SearchTokenKind.RightParenthesis, start, 1));
                    index++;
                    break;
                case '-':
                    if (index + 1 >= query.Length || char.IsWhiteSpace(query[index + 1]) || query[index + 1] == ')')
                    {
                        return new SearchSyntaxError(start, $"The minus sign at position {start} has no condition after it. Remove the minus sign, or write a condition immediately after it.");
                    }

                    tokens.Add(new SearchToken(SearchTokenKind.Minus, start, 1));
                    index++;
                    break;
                case '!':
                    error = ReadExactName(start);
                    break;
                case '"':
                    error = ReadQuoted(start, out var phrase);
                    if (error is null)
                    {
                        AddTerm(new NameNode(phrase, NameMatch.Contains, start, index - start));
                    }

                    break;
                case '/':
                    error = ReadRegex(start, out var pattern);
                    if (error is null)
                    {
                        AddTerm(new NameNode(pattern, NameMatch.Regex, start, index - start));
                    }

                    break;
                default:
                    error = ReadWordOrTerm(start);
                    break;
            }

            if (error is not null)
            {
                return error;
            }
        }
    }

    private void AddTerm(SearchNode node) => tokens.Add(new SearchToken(SearchTokenKind.Term, node.Position, node.Length, node));

    private SearchSyntaxError? ReadExactName(int start)
    {
        index++;
        if (index < query.Length && query[index] == '"')
        {
            var error = ReadQuoted(index, out var name);
            if (error is not null)
            {
                return error;
            }

            AddTerm(new NameNode(name, NameMatch.Exact, start, index - start));
            return null;
        }

        var wordStart = index;
        while (index < query.Length && !IsSeparator(query[index]))
        {
            index++;
        }

        if (index == wordStart)
        {
            return new SearchSyntaxError(start, $"The exclamation mark at position {start} has no card name after it. Write a card name immediately after the exclamation mark.");
        }

        AddTerm(new NameNode(query[wordStart..index], NameMatch.Exact, start, index - start));
        return null;
    }

    private SearchSyntaxError? ReadWordOrTerm(int start)
    {
        var keywordEnd = start;
        while (keywordEnd < query.Length && IsKeywordCharacter(query[keywordEnd]))
        {
            keywordEnd++;
        }

        if (keywordEnd > start && TryReadOperator(keywordEnd, out var op, out var valueStart))
        {
            return ReadTerm(start, query[start..keywordEnd].ToLowerInvariant(), op, valueStart);
        }

        while (index < query.Length && !IsSeparator(query[index]))
        {
            index++;
        }

        var word = query[start..index];
        if (word.Equals("or", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add(new SearchToken(SearchTokenKind.Or, start, word.Length));
        }
        else if (word.Equals("and", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add(new SearchToken(SearchTokenKind.And, start, word.Length));
        }
        else if (word == "++")
        {
            AddTerm(new TermNode("unique", SearchOperator.Colon, "prints", SearchValueKind.Plain, start, word.Length));
        }
        else if (word == "@@")
        {
            AddTerm(new TermNode("unique", SearchOperator.Colon, "art", SearchValueKind.Plain, start, word.Length));
        }
        else
        {
            AddTerm(new NameNode(word, NameMatch.Contains, start, word.Length));
        }

        return null;
    }

    private bool TryReadOperator(int position, out SearchOperator op, out int valueStart)
    {
        op = SearchOperator.Colon;
        valueStart = position;
        if (position >= query.Length)
        {
            return false;
        }

        var next = position + 1 < query.Length ? query[position + 1] : '\0';
        (op, var length) = query[position] switch
        {
            ':' => (SearchOperator.Colon, 1),
            '=' => (SearchOperator.Equal, 1),
            '!' when next == '=' => (SearchOperator.NotEqual, 2),
            '<' when next == '>' => (SearchOperator.NotEqual, 2),
            '<' when next == '=' => (SearchOperator.LessOrEqual, 2),
            '<' => (SearchOperator.Less, 1),
            '>' when next == '=' => (SearchOperator.GreaterOrEqual, 2),
            '>' => (SearchOperator.Greater, 1),
            _ => (SearchOperator.Colon, 0),
        };

        valueStart = position + length;
        return length > 0;
    }

    private SearchSyntaxError? ReadTerm(int start, string keyword, SearchOperator op, int valueStart)
    {
        index = valueStart;
        if (index >= query.Length || IsSeparator(query[index]))
        {
            var written = query[start..valueStart];
            return new SearchSyntaxError(start, $"The term \"{written}\" at position {start} has no value. Write a value immediately after the operator.");
        }

        string value;
        SearchValueKind kind;
        if (query[index] == '"')
        {
            var error = ReadQuoted(index, out value);
            if (error is not null)
            {
                return error;
            }

            kind = SearchValueKind.Quoted;
        }
        else if (query[index] == '/' && RegexKeywords.Contains(keyword))
        {
            var error = ReadRegex(index, out value);
            if (error is not null)
            {
                return error;
            }

            kind = SearchValueKind.Regex;
        }
        else
        {
            var plainStart = index;
            while (index < query.Length && !char.IsWhiteSpace(query[index]) && query[index] != ')')
            {
                index++;
            }

            value = query[plainStart..index];
            kind = SearchValueKind.Plain;
        }

        AddTerm(new TermNode(keyword, op, value, kind, start, index - start));
        return null;
    }

    private SearchSyntaxError? ReadQuoted(int start, out string text)
    {
        var builder = new StringBuilder();
        index = start + 1;
        while (index < query.Length)
        {
            var character = query[index];
            if (character == '\\' && index + 1 < query.Length && query[index + 1] is '"' or '\\')
            {
                builder.Append(query[index + 1]);
                index += 2;
                continue;
            }

            if (character == '"')
            {
                index++;
                text = builder.ToString();
                return null;
            }

            builder.Append(character);
            index++;
        }

        text = string.Empty;
        return new SearchSyntaxError(start, $"The quotation mark at position {start} has no closing quotation mark. Write a quotation mark at the end of the text.");
    }

    private SearchSyntaxError? ReadRegex(int start, out string pattern)
    {
        index = start + 1;
        while (index < query.Length)
        {
            var character = query[index];
            if (character == '\\' && index + 1 < query.Length)
            {
                index += 2;
                continue;
            }

            if (character == '/')
            {
                pattern = query[(start + 1)..index];
                index++;
                if (pattern.Length == 0)
                {
                    return new SearchSyntaxError(start, $"The regular expression at position {start} is empty. Write a regular expression between the two slashes.");
                }

                return null;
            }

            index++;
        }

        pattern = string.Empty;
        return new SearchSyntaxError(start, $"The regular expression at position {start} has no closing slash. Write a slash at the end of the regular expression.");
    }
}
