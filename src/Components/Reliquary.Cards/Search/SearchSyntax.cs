namespace Reliquary.Cards.Search;

/// <summary>
/// The comparison operator of a search term, for example <c>:</c> in <c>t:goblin</c> or <c>&gt;=</c> in <c>mv&gt;=3</c>.
/// </summary>
public enum SearchOperator
{
    /// <summary>The operator <c>:</c>. Its meaning depends on the keyword.</summary>
    Colon,

    /// <summary>The operator <c>=</c>.</summary>
    Equal,

    /// <summary>The operator <c>!=</c> or <c>&lt;&gt;</c>.</summary>
    NotEqual,

    /// <summary>The operator <c>&lt;</c>.</summary>
    Less,

    /// <summary>The operator <c>&lt;=</c>.</summary>
    LessOrEqual,

    /// <summary>The operator <c>&gt;</c>.</summary>
    Greater,

    /// <summary>The operator <c>&gt;=</c>.</summary>
    GreaterOrEqual,
}

/// <summary>
/// The form of the value of a search term.
/// </summary>
public enum SearchValueKind
{
    /// <summary>A plain word, for example <c>goblin</c>.</summary>
    Plain,

    /// <summary>A text between quotation marks, for example <c>"enters tapped"</c>.</summary>
    Quoted,

    /// <summary>A regular expression between slashes, for example <c>/^{T}:/</c>.</summary>
    Regex,
}

/// <summary>
/// How a name term compares the card names.
/// </summary>
public enum NameMatch
{
    /// <summary>A card name contains the text.</summary>
    Contains,

    /// <summary>A card name is equal to the text. The query uses <c>!</c>.</summary>
    Exact,

    /// <summary>A card name matches the regular expression.</summary>
    Regex,
}

/// <summary>
/// A node of the syntax tree of a search query.
/// </summary>
/// <param name="Position">The index of the first character of the node in the query.</param>
/// <param name="Length">The number of characters of the node in the query.</param>
public abstract record SearchNode(int Position, int Length);

/// <summary>
/// All the conditions must match. Terms without <c>or</c> between them give this node.
/// </summary>
/// <param name="Children">The conditions.</param>
/// <param name="Position">The index of the first character.</param>
/// <param name="Length">The number of characters.</param>
public sealed record AndNode(IReadOnlyList<SearchNode> Children, int Position, int Length) : SearchNode(Position, Length);

/// <summary>
/// One of the conditions must match. The query uses <c>or</c>.
/// </summary>
/// <param name="Children">The conditions.</param>
/// <param name="Position">The index of the first character.</param>
/// <param name="Length">The number of characters.</param>
public sealed record OrNode(IReadOnlyList<SearchNode> Children, int Position, int Length) : SearchNode(Position, Length);

/// <summary>
/// The condition must not match. The query uses <c>-</c> before the condition.
/// </summary>
/// <param name="Operand">The condition.</param>
/// <param name="Position">The index of the first character.</param>
/// <param name="Length">The number of characters.</param>
public sealed record NotNode(SearchNode Operand, int Position, int Length) : SearchNode(Position, Length);

/// <summary>
/// A term with a keyword, for example <c>t:goblin</c> or <c>mv&gt;=3</c>.
/// </summary>
/// <param name="Keyword">The keyword as the query writes it, in lowercase, for example <c>t</c>.</param>
/// <param name="Operator">The operator.</param>
/// <param name="Value">The value, without quotation marks or slashes.</param>
/// <param name="Kind">The form of the value.</param>
/// <param name="Position">The index of the first character.</param>
/// <param name="Length">The number of characters.</param>
public sealed record TermNode(string Keyword, SearchOperator Operator, string Value, SearchValueKind Kind, int Position, int Length)
    : SearchNode(Position, Length);

/// <summary>
/// A term without a keyword. It compares the card names, for example <c>bolt</c>, <c>!"Lightning Bolt"</c> or <c>/^bolt/</c>.
/// </summary>
/// <param name="Value">The text, without quotation marks or slashes.</param>
/// <param name="Match">How the term compares the names.</param>
/// <param name="Position">The index of the first character.</param>
/// <param name="Length">The number of characters.</param>
public sealed record NameNode(string Value, NameMatch Match, int Position, int Length) : SearchNode(Position, Length);

/// <summary>
/// A syntax error in a search query.
/// </summary>
/// <param name="Position">The index of the character where the error is.</param>
/// <param name="Message">The English message, in the strict mode of the writing rules.</param>
public sealed record SearchSyntaxError(int Position, string Message);
