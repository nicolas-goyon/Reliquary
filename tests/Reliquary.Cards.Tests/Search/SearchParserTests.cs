using Reliquary.Cards.Search;

namespace Reliquary.Cards.Tests.Search;

public sealed class SearchParserTests
{
    [Fact]
    public void KeywordTermHasItsKeywordOperatorAndValue()
    {
        var term = Assert.IsType<TermNode>(Parse("t:goblin"));

        Assert.Equal("t", term.Keyword);
        Assert.Equal(SearchOperator.Colon, term.Operator);
        Assert.Equal("goblin", term.Value);
        Assert.Equal(SearchValueKind.Plain, term.Kind);
        Assert.Equal(0, term.Position);
        Assert.Equal(8, term.Length);
    }

    [Theory]
    [InlineData("mv=3", SearchOperator.Equal)]
    [InlineData("mv!=3", SearchOperator.NotEqual)]
    [InlineData("mv<>3", SearchOperator.NotEqual)]
    [InlineData("mv<3", SearchOperator.Less)]
    [InlineData("mv<=3", SearchOperator.LessOrEqual)]
    [InlineData("mv>3", SearchOperator.Greater)]
    [InlineData("mv>=3", SearchOperator.GreaterOrEqual)]
    [InlineData("mv:3", SearchOperator.Colon)]
    public void EachOperatorHasItsKind(string query, SearchOperator expected)
    {
        var term = Assert.IsType<TermNode>(Parse(query));

        Assert.Equal(expected, term.Operator);
        Assert.Equal("3", term.Value);
    }

    [Fact]
    public void KeywordIsLowercase()
    {
        var term = Assert.IsType<TermNode>(Parse("T:Goblin"));

        Assert.Equal("t", term.Keyword);
        Assert.Equal("Goblin", term.Value);
    }

    [Fact]
    public void QuotedValueKeepsItsSpaces()
    {
        var term = Assert.IsType<TermNode>(Parse("o:\"~ enters tapped\""));

        Assert.Equal("~ enters tapped", term.Value);
        Assert.Equal(SearchValueKind.Quoted, term.Kind);
    }

    [Fact]
    public void QuotedValueAcceptsAnEscapedQuotationMark()
    {
        var term = Assert.IsType<TermNode>(Parse("o:\"say \\\"hi\\\"\""));

        Assert.Equal("say \"hi\"", term.Value);
    }

    [Fact]
    public void RegexValueKeepsItsEscapes()
    {
        var term = Assert.IsType<TermNode>(Parse(@"o:/^{T}:\/x/"));

        Assert.Equal(@"^{T}:\/x", term.Value);
        Assert.Equal(SearchValueKind.Regex, term.Kind);
    }

    [Fact]
    public void SlashIsPlainForAKeywordWithoutRegex()
    {
        var term = Assert.IsType<TermNode>(Parse("m:{2/G}"));

        Assert.Equal("{2/G}", term.Value);
        Assert.Equal(SearchValueKind.Plain, term.Kind);
    }

    [Fact]
    public void BareWordIsAName()
    {
        var name = Assert.IsType<NameNode>(Parse("bolt"));

        Assert.Equal("bolt", name.Value);
        Assert.Equal(NameMatch.Contains, name.Match);
    }

    [Fact]
    public void QuotedBareTextIsAName()
    {
        var name = Assert.IsType<NameNode>(Parse("\"lightning bolt\""));

        Assert.Equal("lightning bolt", name.Value);
        Assert.Equal(NameMatch.Contains, name.Match);
    }

    [Theory]
    [InlineData("!fire", "fire")]
    [InlineData("!\"sift through sands\"", "sift through sands")]
    public void ExclamationMarkGivesAnExactName(string query, string expected)
    {
        var name = Assert.IsType<NameNode>(Parse(query));

        Assert.Equal(expected, name.Value);
        Assert.Equal(NameMatch.Exact, name.Match);
    }

    [Fact]
    public void BareRegexIsANameRegex()
    {
        var name = Assert.IsType<NameNode>(Parse(@"/\bizzet\b/"));

        Assert.Equal(@"\bizzet\b", name.Value);
        Assert.Equal(NameMatch.Regex, name.Match);
    }

    [Fact]
    public void WordWithAnInvalidKeywordIsAName()
    {
        var name = Assert.IsType<NameNode>(Parse("ach!"));

        Assert.Equal("ach!", name.Value);
    }

    [Fact]
    public void TermsWithoutOrMustAllMatch()
    {
        var and = Assert.IsType<AndNode>(Parse("t:goblin c:r"));

        Assert.Equal(2, and.Children.Count);
    }

    [Fact]
    public void AndWordIsAnOperator()
    {
        var and = Assert.IsType<AndNode>(Parse("t:goblin AND t:legendary"));

        Assert.Equal(2, and.Children.Count);
    }

    [Fact]
    public void AndBindsMoreStronglyThanOr()
    {
        var or = Assert.IsType<OrNode>(Parse("t:fish or t:bird c:u"));

        Assert.IsType<TermNode>(or.Children[0]);
        Assert.IsType<AndNode>(or.Children[1]);
    }

    [Fact]
    public void ParenthesesGroupConditions()
    {
        var and = Assert.IsType<AndNode>(Parse("t:legendary (t:goblin or t:elf)"));

        Assert.IsType<OrNode>(and.Children[1]);
    }

    [Fact]
    public void UppercaseOrIsAnOperator()
    {
        Assert.IsType<OrNode>(Parse("t:fish OR t:bird"));
    }

    [Fact]
    public void MinusNegatesATerm()
    {
        var not = Assert.IsType<NotNode>(Parse("-c:red"));

        Assert.Equal("c", Assert.IsType<TermNode>(not.Operand).Keyword);
    }

    [Fact]
    public void MinusNegatesAName()
    {
        var not = Assert.IsType<NotNode>(Parse("-fire"));

        Assert.Equal("fire", Assert.IsType<NameNode>(not.Operand).Value);
    }

    [Fact]
    public void MinusNegatesAGroup()
    {
        var not = Assert.IsType<NotNode>(Parse("-(t:goblin or t:elf)"));

        Assert.IsType<OrNode>(not.Operand);
    }

    [Theory]
    [InlineData("++", "prints")]
    [InlineData("@@", "art")]
    public void ShortFormsGiveTheUniqueKeyword(string query, string value)
    {
        var term = Assert.IsType<TermNode>(Parse(query));

        Assert.Equal("unique", term.Keyword);
        Assert.Equal(value, term.Value);
    }

    [Fact]
    public void EmptyQueryHasNoTree()
    {
        var result = SearchParser.Parse("   ");

        Assert.Null(result.Root);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("t:goblin (", 9, "opening parenthesis")]
    [InlineData("(t:goblin or t:elf", 0, "opening parenthesis")]
    [InlineData("t:goblin )", 9, "closing parenthesis")]
    [InlineData(") t:goblin", 0, "closing parenthesis")]
    [InlineData("t:goblin ()", 9, "contain no condition")]
    [InlineData("o:\"draw", 2, "quotation mark")]
    [InlineData("\"draw", 0, "quotation mark")]
    [InlineData("o:/draw", 2, "closing slash")]
    [InlineData("o://", 2, "empty")]
    [InlineData("t: goblin", 0, "no value")]
    [InlineData("t:", 0, "no value")]
    [InlineData("mv>=", 0, "no value")]
    [InlineData("or t:goblin", 0, "no condition before")]
    [InlineData("t:goblin or", 9, "no condition after")]
    [InlineData("t:goblin or or t:elf", 9, "no condition after")]
    [InlineData("t:goblin and", 9, "no condition after")]
    [InlineData("and t:goblin", 0, "no condition before")]
    [InlineData("t:goblin -", 9, "minus sign")]
    [InlineData("t:goblin - c:r", 9, "minus sign")]
    [InlineData("(-)", 1, "minus sign")]
    [InlineData("! t:goblin", 0, "exclamation mark")]
    public void SyntaxErrorGivesItsPosition(string query, int position, string message)
    {
        var result = SearchParser.Parse(query);

        Assert.Null(result.Root);
        Assert.NotNull(result.Error);
        Assert.Equal(position, result.Error.Position);
        Assert.Contains(message, result.Error.Message, StringComparison.Ordinal);
        Assert.Contains($"position {position}", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllExamplesOfTheSyntaxGuideHaveNoSyntaxError()
    {
        foreach (var entry in SearchParityData.Load())
        {
            var result = SearchParser.Parse(entry.Query);
            Assert.True(result.Error is null, $"The query {entry.Query} has a syntax error: {result.Error?.Message}");
            Assert.NotNull(result.Root);
        }
    }

    private static SearchNode Parse(string query)
    {
        var result = SearchParser.Parse(query);
        Assert.Null(result.Error);
        return result.Root!;
    }
}
