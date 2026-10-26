using Reliquary.Cards.Search;

namespace Reliquary.Cards.Tests.Search;

public sealed class SearchCompilerTests
{
    [Fact]
    public void ValuesGoIntoParametersAndNeverIntoTheSqlText()
    {
        var search = Compile("o:\"'; drop table x; --\" t:goblin a:robert name:/x'y/");

        var sql = SearchSql.Page(search);

        Assert.DoesNotContain("drop table", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("goblin", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("robert", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("x'y", sql, StringComparison.Ordinal);
        Assert.Contains(search.Parameters.Values, value => value is string text && text.Contains("drop table", StringComparison.Ordinal));
    }

    [Fact]
    public void CardTermsDoNotUseThePrintings()
    {
        var search = Compile("t:creature c:g mv<=2 o:draw");

        Assert.NotNull(search.CardCondition);
        Assert.Null(search.PrintingCondition);
        Assert.False(search.UsesPrintings);
    }

    [Fact]
    public void PrintingTermsUseThePrintings()
    {
        var search = Compile("t:creature e:war r:rare");

        Assert.NotNull(search.CardCondition);
        Assert.NotNull(search.PrintingCondition);
    }

    [Fact]
    public void OrWithAPrintingTermIsAPrintingCondition()
    {
        var search = Compile("t:goblin or e:war");

        Assert.Null(search.CardCondition);
        Assert.NotNull(search.PrintingCondition);
    }

    [Fact]
    public void UnknownKeywordGivesAWarningAndTheSearchIgnoresTheTerm()
    {
        var search = Compile("foo:bar t:goblin");

        var warning = Assert.Single(search.Warnings);
        Assert.Equal(SearchWarningCodes.UnknownKeyword, warning.Code);
        Assert.Equal(0, warning.Position);
        Assert.Equal("foo:bar", warning.Term);
        Assert.NotNull(search.CardCondition);
        Assert.False(search.IsEmpty);
    }

    [Theory]
    [InlineData("c:xyz", SearchWarningCodes.InvalidValue)]
    [InlineData("r:xyz", SearchWarningCodes.InvalidValue)]
    [InlineData("f:xyz", SearchWarningCodes.InvalidValue)]
    [InlineData("mv>abc", SearchWarningCodes.InvalidValue)]
    [InlineData("is:xyz", SearchWarningCodes.InvalidValue)]
    [InlineData("lang:xyz", SearchWarningCodes.InvalidValue)]
    [InlineData("date>2015-99-99", SearchWarningCodes.InvalidValue)]
    [InlineData("m:{", SearchWarningCodes.InvalidValue)]
    [InlineData("t>goblin", SearchWarningCodes.InvalidOperator)]
    [InlineData("cube:vintage", SearchWarningCodes.UnsupportedKeyword)]
    [InlineData("is:scryfallpreview", SearchWarningCodes.UnsupportedKeyword)]
    public void TermThatIsNotValidGivesAWarning(string term, string code)
    {
        var search = Compile(term + " t:goblin");

        Assert.Equal(code, Assert.Single(search.Warnings).Code);
    }

    [Fact]
    public void QueryWithOnlyIgnoredTermsIsEmpty()
    {
        Assert.True(Compile("foo:bar").IsEmpty);
        Assert.True(Compile("unique:prints order:name").IsEmpty);
    }

    [Fact]
    public void IgnoredTermInsideOrLeavesTheOtherTerm()
    {
        var search = Compile("t:goblin or foo:bar");

        Assert.NotNull(search.CardCondition);
        Assert.DoesNotContain(" or ", search.CardCondition, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayKeywordsWinOverTheParameters()
    {
        var search = SearchCompiler.Compile(
            "t:goblin unique:art order:cmc dir:desc prefer:oldest display:grid",
            SearchParser.Parse("t:goblin unique:art order:cmc dir:desc prefer:oldest display:grid").Root,
            new SearchDefaults(SearchUnique.Prints, SearchOrder.Usd, SearchDirection.Ascending));

        Assert.Equal(SearchUnique.Art, search.Unique);
        Assert.Equal(SearchOrder.ManaValue, search.Order);
        Assert.Equal(SearchDirection.Descending, search.Direction);
        Assert.Equal(SearchPreference.Oldest, search.Prefer);
        Assert.Empty(search.Warnings);
    }

    [Fact]
    public void ParametersGiveTheDefaultDisplayOptions()
    {
        var search = SearchCompiler.Compile("t:goblin", SearchParser.Parse("t:goblin").Root, new SearchDefaults(SearchUnique.Prints, SearchOrder.Released, SearchDirection.Ascending));

        Assert.Equal(SearchUnique.Prints, search.Unique);
        Assert.Equal(SearchOrder.Released, search.Order);
    }

    [Theory]
    [InlineData("t:scheme")]
    [InlineData("t:vanguard")]
    [InlineData("e:tdsk")]
    [InlineData("include:extras")]
    [InlineData("is:token")]
    [InlineData("is:funny")]
    [InlineData("a:avon")]
    [InlineData("banned:legacy")]
    [InlineData("is:reserved")]
    [InlineData("name:izzet")]
    public void TermThatTargetsExtraCardsIncludesThem(string query)
    {
        Assert.True(Compile(query).IncludeExtras);
    }

    [Theory]
    [InlineData("t:creature")]
    [InlineData("-t:scheme")]
    [InlineData("-e:tdsk t:goblin")]
    [InlineData("t:planeswalker")]
    [InlineData("st:memorabilia")]
    [InlineData("izzet")]
    [InlineData("-a:avon")]
    public void OtherQueriesExcludeExtraCards(string query)
    {
        Assert.False(Compile(query).IncludeExtras);
    }

    [Fact]
    public void LanguageKeywordRemovesTheDefaultLanguage()
    {
        Assert.True(Compile("lang:fr t:goblin").LanguageGiven);
        Assert.True(Compile("lang:any t:goblin").LanguageGiven);
        Assert.False(Compile("t:goblin").LanguageGiven);
    }

    [Fact]
    public void NameTermsGiveTheLanguageOfALocalizedName()
    {
        Assert.NotNull(Compile("foudre").NameLanguage);
        Assert.Null(Compile("lang:fr foudre").NameLanguage);
        Assert.Null(Compile("-foudre t:instant").NameLanguage);
        Assert.Null(Compile("t:instant").NameLanguage);
    }

    [Theory]
    [InlineData("2WW", 2, new[] { "W", "W" })]
    [InlineData("{2}{W}{W}", 2, new[] { "W", "W" })]
    [InlineData("{G}{U}", 0, new[] { "G", "U" })]
    [InlineData("{R/P}", 0, new[] { "R/P" })]
    [InlineData("{P/R}", 0, new[] { "R/P" })]
    [InlineData("{b/u}{2/g}", 0, new[] { "U/B", "2/G" })]
    [InlineData("X10R", 10, new[] { "X", "R" })]
    public void ManaCostGivesGenericManaAndSymbols(string value, int generic, string[] symbols)
    {
        Assert.True(SearchCompiler.TryParseMana(value, out var parsedGeneric, out var parsedSymbols));

        Assert.Equal(generic, parsedGeneric);
        Assert.Equal(symbols, parsedSymbols);
    }

    [Theory]
    [InlineData("^{T}:", "(?n)^\\{T\\}:")]
    [InlineData("\\bizzet\\b", "(?n)\\yizzet\\y")]
    [InlineData("a{2,3}", "(?n)a{2,3}")]
    [InlineData("\\/", "(?n)/")]
    [InlineData("[{}]", "(?n)[{}]")]
    [InlineData("\\spp", "(?n)\\+[0-9x*]+/\\+[0-9x*]+")]
    [InlineData("\\sm", "(?n)\\{(?:[wubrgcxyzs]|[0-9]+|[wubrgc2]/[wubrgp](?:/p)?)\\}")]
    [InlineData("(a)\\smr", "(?n)(a)(\\{(?:[wubrgcxyzs]|[0-9]+|[wubrgc2]/[wubrgp](?:/p)?)\\})\\2")]
    public void RegexBecomesAPostgresRegex(string pattern, string expected)
    {
        Assert.Equal(expected, RegexTranslator.Translate(pattern, out var usesName));
        Assert.False(usesName);
    }

    [Fact]
    public void TildeInARegexIsTheCardName()
    {
        var translated = RegexTranslator.Translate("~ deals", out var usesName);

        Assert.True(usesName);
        Assert.Equal("(?n)\u0001 deals", translated);
    }

    private static CompiledSearch Compile(string query)
    {
        var parsed = SearchParser.Parse(query);
        Assert.Null(parsed.Error);
        return SearchCompiler.Compile(query, parsed.Root, new SearchDefaults());
    }
}
