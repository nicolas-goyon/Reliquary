namespace Reliquary.Cards.Search;

/// <summary>
/// The result of the parse of a search query.
/// </summary>
/// <param name="Root">The syntax tree, or <see langword="null"/> when the query is empty or has a syntax error.</param>
/// <param name="Error">The syntax error, or <see langword="null"/>.</param>
public sealed record SearchParseResult(SearchNode? Root, SearchSyntaxError? Error);

/// <summary>
/// Reads a search query in the Scryfall syntax and gives its syntax tree.
/// </summary>
/// <remarks>
/// The parser is a recursive-descent parser. It uses no database. The grammar is:
/// <code>
/// query   = or
/// or      = and { "or" and }
/// and     = unary { [ "and" ] unary }
/// unary   = "-" unary | primary
/// primary = "(" or ")" | term
/// </code>
/// Terms without <c>or</c> between them must all match, so <c>and</c> binds more strongly than <c>or</c>.
/// </remarks>
public static class SearchParser
{
    /// <summary>
    /// Parses a query.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>The syntax tree or the syntax error.</returns>
    public static SearchParseResult Parse(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!SearchLexer.TryTokenize(query, out var tokens, out var error))
        {
            return new SearchParseResult(null, error);
        }

        if (tokens.Count == 0)
        {
            return new SearchParseResult(null, null);
        }

        var state = new State(tokens);
        try
        {
            var root = state.ParseOr();
            if (state.Current is { } extra)
            {
                // Only a closing parenthesis can stop the parse before the end.
                throw new ParseFailure(new SearchSyntaxError(extra.Position, $"The closing parenthesis at position {extra.Position} has no opening parenthesis. Remove it, or write an opening parenthesis before it."));
            }

            return new SearchParseResult(root, null);
        }
        catch (ParseFailure failure)
        {
            return new SearchParseResult(null, failure.Error);
        }
    }

    private static SearchNode Combine(List<SearchNode> children, bool or)
    {
        if (children.Count == 1)
        {
            return children[0];
        }

        var position = children[0].Position;
        var last = children[^1];
        var length = last.Position + last.Length - position;
        return or ? new OrNode(children, position, length) : new AndNode(children, position, length);
    }

    private sealed class State(IReadOnlyList<SearchToken> tokens)
    {
        private int index;

        public SearchToken? Current => index < tokens.Count ? tokens[index] : null;

        public SearchNode ParseOr()
        {
            if (Current is { Kind: SearchTokenKind.Or } leading)
            {
                throw Failure(leading.Position, $"The word \"or\" at position {leading.Position} has no condition before it. Write a condition before \"or\", or remove \"or\".");
            }

            var children = new List<SearchNode> { ParseAnd() };
            while (Current is { Kind: SearchTokenKind.Or } or)
            {
                index++;
                if (Current is null or { Kind: SearchTokenKind.RightParenthesis or SearchTokenKind.Or or SearchTokenKind.And })
                {
                    throw Failure(or.Position, $"The word \"or\" at position {or.Position} has no condition after it. Write a condition after \"or\", or remove \"or\".");
                }

                children.Add(ParseAnd());
            }

            return Combine(children, or: true);
        }

        private SearchNode ParseAnd()
        {
            if (Current is { Kind: SearchTokenKind.And } leading)
            {
                throw Failure(leading.Position, $"The word \"and\" at position {leading.Position} has no condition before it. Write a condition before \"and\", or remove \"and\".");
            }

            var children = new List<SearchNode>();
            while (Current is { } token && token.Kind is not (SearchTokenKind.Or or SearchTokenKind.RightParenthesis))
            {
                if (token.Kind == SearchTokenKind.And)
                {
                    index++;
                    if (Current is null or { Kind: SearchTokenKind.RightParenthesis or SearchTokenKind.Or or SearchTokenKind.And })
                    {
                        throw Failure(token.Position, $"The word \"and\" at position {token.Position} has no condition after it. Write a condition after \"and\", or remove \"and\".");
                    }

                    continue;
                }

                children.Add(ParseUnary());
            }

            if (children.Count == 0)
            {
                // The parse stops at a closing parenthesis that has no opening parenthesis.
                var closing = Current!;
                throw Failure(closing.Position, $"The closing parenthesis at position {closing.Position} has no opening parenthesis. Remove it, or write an opening parenthesis before it.");
            }

            return Combine(children, or: false);
        }

        private SearchNode ParseUnary()
        {
            var token = Current!;
            if (token.Kind != SearchTokenKind.Minus)
            {
                return ParsePrimary();
            }

            index++;
            if (Current is null or { Kind: SearchTokenKind.RightParenthesis or SearchTokenKind.Or or SearchTokenKind.And })
            {
                throw Failure(token.Position, $"The minus sign at position {token.Position} has no condition after it. Remove the minus sign, or write a condition immediately after it.");
            }

            var operand = ParseUnary();
            return new NotNode(operand, token.Position, operand.Position + operand.Length - token.Position);
        }

        private SearchNode ParsePrimary()
        {
            var token = Current!;
            switch (token.Kind)
            {
                case SearchTokenKind.Term:
                    index++;
                    return token.Node!;
                case SearchTokenKind.LeftParenthesis:
                    index++;
                    if (Current is { Kind: SearchTokenKind.RightParenthesis })
                    {
                        throw Failure(token.Position, $"The parentheses at position {token.Position} contain no condition. Write a condition between the parentheses, or remove them.");
                    }

                    if (Current is null)
                    {
                        throw Unclosed(token);
                    }

                    var inner = ParseOr();
                    if (Current is not { Kind: SearchTokenKind.RightParenthesis } closing)
                    {
                        throw Unclosed(token);
                    }

                    index++;
                    return inner switch
                    {
                        AndNode and => and with { Position = token.Position, Length = closing.Position + 1 - token.Position },
                        OrNode or => or with { Position = token.Position, Length = closing.Position + 1 - token.Position },
                        _ => inner,
                    };
                default:
                    throw Failure(token.Position, $"The closing parenthesis at position {token.Position} has no opening parenthesis. Remove it, or write an opening parenthesis before it.");
            }
        }

        private static ParseFailure Unclosed(SearchToken opening) =>
            Failure(opening.Position, $"The opening parenthesis at position {opening.Position} has no closing parenthesis. Write a closing parenthesis after the conditions.");

        private static ParseFailure Failure(int position, string message) => new(new SearchSyntaxError(position, message));
    }

#pragma warning disable CA1032, CA1064 // A private exception that stops the recursive parse. It never leaves this class.
    private sealed class ParseFailure(SearchSyntaxError error) : Exception(error.Message)
#pragma warning restore CA1032, CA1064
    {
        public SearchSyntaxError Error { get; } = error;
    }
}
