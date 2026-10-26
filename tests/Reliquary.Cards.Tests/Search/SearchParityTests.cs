using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Cards.Search;

namespace Reliquary.Cards.Tests.Search;

/// <summary>
/// One database with the imported demo dataset, shared by the search tests.
/// </summary>
public sealed class DemoCardsFixture : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CardsDatabase? database;

    internal async Task<CardsDatabase> GetAsync()
    {
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (database is null)
            {
                var created = await CardsDatabase.CreateAsync(SearchParityData.DemoDirectory);
                await created.ImportAsync();
                database = created;
            }

            return database;
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task<CardSearchOutcome> SearchAsync(string query, SearchUnique unique = SearchUnique.Cards, int limit = 100, string? cursor = null)
    {
        var cards = await GetAsync();
        var search = cards.Services.GetRequiredService<CardSearch>();
        return await search.SearchAsync(new CardSearchRequest(query, new SearchDefaults(unique), new PageRequest(limit, cursor)), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Runs a query and reads all the pages.
    /// </summary>
    internal async Task<List<Api.CardSearchItem>> SearchAllAsync(string query, SearchUnique unique = SearchUnique.Cards)
    {
        var items = new List<Api.CardSearchItem>();
        string? cursor = null;
        do
        {
            var outcome = await SearchAsync(query, unique, 100, cursor);
            if (outcome is CardSearchFailed { Code: CardSearchErrorCodes.EmptyQuery })
            {
                return items;
            }

            var page = Assert.IsType<CardSearchSucceeded>(outcome, exactMatch: false).Response;
            items.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return items;
    }

    public async ValueTask DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }

        gate.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class DemoCardsGroup : ICollectionFixture<DemoCardsFixture>
{
    public const string Name = "Demo cards";
}

/// <summary>
/// Runs the examples of the Scryfall syntax guide and other queries on the demo dataset,
/// and compares the Oracle cards with the cards that the Scryfall search finds.
/// </summary>
[Collection(DemoCardsGroup.Name)]
public sealed class SearchParityTests(DemoCardsFixture fixture)
{
    [Theory]
    [MemberData(nameof(Queries))]
    public async Task QueryFindsTheSameCardsAsScryfall(string query)
    {
        var entry = SearchParityData.Load().First(item => item.Query == query);

        var items = await fixture.SearchAllAsync(query);
        var found = items.Select(item => item.Card.Id).ToHashSet();
        var expected = entry.Expected.ToHashSet();

        if (entry.KnownDifference is not null)
        {
            Assert.False(found.SetEquals(expected), $"The query \"{query}\" now finds the same cards as Scryfall. Remove its known difference.");
            return;
        }

        var missing = expected.Except(found).ToList();
        var extra = items.Where(item => !expected.Contains(item.Card.Id)).Select(item => item.Card.Name).ToList();
        Assert.True(
            missing.Count == 0 && extra.Count == 0,
            $"The query \"{query}\" does not find the same cards as Scryfall. Missing: {string.Join(", ", missing)}. Not expected: {string.Join(", ", extra)}.");
    }

    [Fact]
    public void AtLeastNinetyPercentOfTheGuideExamplesHaveNoKnownDifference()
    {
        var examples = SearchParityData.Load().Where(entry => entry.Origin.StartsWith("syntax-guide", StringComparison.Ordinal)).ToList();
        var exact = examples.Count(entry => entry.KnownDifference is null);

        Assert.True(exact >= examples.Count * 0.9, $"{exact} of {examples.Count} examples of the syntax guide find the same cards as Scryfall.");
    }

    public static TheoryData<string> Queries() => SearchParityData.Queries();
}
