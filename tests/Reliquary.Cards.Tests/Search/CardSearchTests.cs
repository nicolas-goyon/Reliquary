using Reliquary.Cards.Search;

namespace Reliquary.Cards.Tests.Search;

/// <summary>
/// Runs the compiled SQL of the search on the demo dataset.
/// </summary>
[Collection(DemoCardsGroup.Name)]
public sealed class CardSearchTests(DemoCardsFixture fixture)
{
    [Theory]
    [InlineData("foudre", "Lightning Bolt", "fr")]
    [InlineData("bolt", "Lightning Bolt", "en")]
    [InlineData("!\"Lightning Bolt\"", "Lightning Bolt", "en")]
    public async Task LocalizedNameFindsTheCardAndShowsAPrintingInItsLanguage(string query, string name, string lang)
    {
        var items = await fixture.SearchAllAsync(query);

        var item = Assert.Single(items, item => item.Card.Name == name);
        Assert.Equal(lang, item.Printing.Lang);
    }

    [Fact]
    public async Task ExactNameMatchesAFaceName()
    {
        var items = await fixture.SearchAllAsync("!fire");

        Assert.Equal("Fire // Ice", Assert.Single(items).Card.Name);
    }

    [Fact]
    public async Task ExtraCardsAreHiddenByDefault()
    {
        var hidden = await fixture.SearchAllAsync("t:goblin");
        var shown = await fixture.SearchAllAsync("t:goblin include:extras");

        Assert.DoesNotContain(hidden, item => item.Card.Layout == "token");
        Assert.Contains(shown, item => item.Card.Layout == "token");
    }

    [Fact]
    public async Task PrintingConditionChoosesAMatchingPrinting()
    {
        var items = await fixture.SearchAllAsync("!\"Lightning Bolt\" lang:fr");

        Assert.Equal("fr", Assert.Single(items).Printing.Lang);
    }

    [Fact]
    public async Task PreferChoosesThePrinting()
    {
        var oldest = Assert.Single(await fixture.SearchAllAsync("!\"Counterspell\" prefer:oldest"));
        var newest = Assert.Single(await fixture.SearchAllAsync("!\"Counterspell\" prefer:newest"));

        Assert.True(oldest.Printing.ReleasedAt < newest.Printing.ReleasedAt);
    }

    [Fact]
    public async Task UniquePrintsGivesEachPrinting()
    {
        var cards = await fixture.SearchAllAsync("!\"Counterspell\"");
        var prints = await fixture.SearchAllAsync("!\"Counterspell\"", SearchUnique.Prints);

        Assert.Single(cards);
        Assert.True(prints.Count > 1);
        Assert.Equal(prints.Count, prints.Select(item => item.Printing.Id).Distinct().Count());
        Assert.All(prints, item => Assert.Equal("Counterspell", item.Card.Name));
    }

    [Fact]
    public async Task UniqueArtGivesEachIllustrationOnce()
    {
        var prints = await fixture.SearchAllAsync("!\"Sol Ring\" unique:prints");
        var art = await fixture.SearchAllAsync("!\"Sol Ring\" unique:art");

        Assert.NotEmpty(art);
        Assert.True(art.Count <= prints.Count);
    }

    [Fact]
    public async Task OrderAndDirectionSortTheResults()
    {
        var ascending = await fixture.SearchAllAsync("t:creature order:cmc dir:asc");
        var descending = await fixture.SearchAllAsync("t:creature order:cmc dir:desc");

        Assert.Equal(ascending.Select(item => item.Card.ManaValue).Order(), ascending.Select(item => item.Card.ManaValue));
        Assert.Equal(descending.Select(item => item.Card.ManaValue).OrderDescending(), descending.Select(item => item.Card.ManaValue));
    }

    [Fact]
    public async Task DefaultOrderIsTheName()
    {
        var items = await fixture.SearchAllAsync("t:creature");

        Assert.Equal(items.Select(item => item.Card.Name).Order(StringComparer.Ordinal), items.Select(item => item.Card.Name));
    }

    [Fact]
    public async Task CursorGivesTheNextPageAndTheTotal()
    {
        var all = await fixture.SearchAllAsync("t:creature");
        var first = Assert.IsType<CardSearchSucceeded>(await fixture.SearchAsync("t:creature", limit: 10)).Response;
        var second = Assert.IsType<CardSearchSucceeded>(await fixture.SearchAsync("t:creature", limit: 10, cursor: first.NextCursor)).Response;

        Assert.Equal(all.Count, first.Total);
        Assert.Equal(all.Count, second.Total);
        Assert.Equal(all.Skip(10).Take(10).Select(item => item.Card.Id), second.Items.Select(item => item.Card.Id));
    }

    [Fact]
    public async Task CursorOfAnotherQueryIsNotValid()
    {
        var first = Assert.IsType<CardSearchSucceeded>(await fixture.SearchAsync("t:creature", limit: 10)).Response;

        Assert.IsType<CardSearchInvalidCursor>(await fixture.SearchAsync("t:land", limit: 10, cursor: first.NextCursor));
        Assert.IsType<CardSearchInvalidCursor>(await fixture.SearchAsync("t:creature", limit: 10, cursor: "not-a-cursor"));
    }

    [Fact]
    public async Task RegexThatPostgresCannotCompileIsASyntaxError()
    {
        var failed = Assert.IsType<CardSearchFailed>(await fixture.SearchAsync("t:creature o:/(abc/"));

        Assert.Equal(CardSearchErrorCodes.SyntaxError, failed.Code);
        Assert.Equal(11, failed.Position);
    }

    [Fact]
    public async Task WarningsComeWithTheResults()
    {
        var page = Assert.IsType<CardSearchSucceeded>(await fixture.SearchAsync("t:goblin foo:bar")).Response;

        Assert.NotEmpty(page.Items);
        Assert.Equal("foo:bar", Assert.Single(page.Warnings).Term);
    }

    [Theory]
    [InlineData("pow>=8")]
    [InlineData("pow>tou")]
    [InlineData("pt>=10")]
    [InlineData("loy>=5")]
    [InlineData("m>3WU")]
    [InlineData("m<={4}{W}{W}{U}")]
    [InlineData("m!=W")]
    [InlineData("devotion>={g}{g}")]
    [InlineData("c<=wu")]
    [InlineData("c>u")]
    [InlineData("id>=3")]
    [InlineData("produces:c")]
    [InlineData("o:/~ deals/")]
    [InlineData("fo:/\\sm/")]
    [InlineData("ft:the")]
    [InlineData("wm:orzhov or has:watermark")]
    [InlineData("cn>=100 cn<200")]
    [InlineData("usd>1 eur<100 tix>=0")]
    [InlineData("t:creature cheapest:usd")]
    [InlineData("new:art or new:artist or new:flavor or new:frame or new:rarity or new:language")]
    [InlineData("border:black frame:2015 stamp:oval game:paper")]
    [InlineData("year>=2020 date<now date>=2015-08-18 date>ori")]
    [InlineData("is:foil is:nonfoil -is:etched is:hires is:booster")]
    [InlineData("prints>=2 sets>1 paperprints>=1 papersets<50 illustrations>=1")]
    [InlineData("artists>1 or a:/avon/")]
    [InlineData("edhrec<=100 penny>0")]
    [InlineData("is:spell or is:permanent or is:historic or is:party or is:outlaw or is:modal or is:vanilla or is:frenchvanilla or is:bear")]
    [InlineData("is:commander or is:brawler or is:companion or is:oathbreaker or is:partner or is:reserved or is:gamechanger")]
    [InlineData("is:fetchland or is:shockland or is:checkland or is:fastland or is:slowland or is:painland or is:karoo or is:gainland")]
    [InlineData("is:scryland or is:surveilland or is:filterland or is:battleland or is:canopyland or is:snarl or is:storageland or is:triland")]
    [InlineData("is:triome or is:bikeland or is:bondland or is:pathway or is:manland or is:dual")]
    [InlineData("in:paper in:en -in:sega in:rare in:core")]
    [InlineData("b:zen or g:fin or st:draftinnovation")]
    [InlineData("t:creature order:usd")]
    [InlineData("t:creature order:set dir:asc")]
    [InlineData("t:creature order:color")]
    [InlineData("t:creature order:rarity unique:prints")]
    [InlineData("t:creature order:released unique:art")]
    [InlineData("t:creature prefer:usd-low")]
    [InlineData("t:creature prefer:atypical lang:any")]
    [InlineData("oracleid:52558748-6893-4c72-a9e2-e87d31796b59")]
    public async Task EachKeywordRunsWithoutError(string query)
    {
        Assert.IsType<CardSearchSucceeded>(await fixture.SearchAsync(query));
    }

    [Theory]
    [InlineData("pow>=8", "Brisela, Voice of Nightmares")]
    [InlineData("o:\"~ enters tapped\"", "Command Tower", false)]
    [InlineData("o:\"discard this card\" !\"Ash Barrens\"", "Ash Barrens", false)]
    [InlineData("fo:\"discard this card\" !\"Ash Barrens\"", "Ash Barrens")]
    [InlineData("m:2WW", "Austere Command")]
    [InlineData("c:rg", "Rhythm of the Wild")]
    [InlineData("function:removal", "Swords to Plowshares")]
    [InlineData("is:dual", "Underground Sea")]
    [InlineData("t:creature o:/^{T}:/", "Llanowar Elves")]
    [InlineData("name:/\\bizzet\\b/", "Izzet Signet")]
    public async Task QueryFindsACard(string query, string name, bool found = true)
    {
        var items = await fixture.SearchAllAsync(query);

        Assert.Equal(found, items.Any(item => item.Card.Name == name));
    }
}
