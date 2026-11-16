using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Decks;
using Reliquary.Decks.Api;
using Reliquary.Decks.Text;

namespace Reliquary.Decks.Tests;

[Collection(DecksGroup.Name)]
public sealed class GameDeckSourceTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnerGetsTheLibraryAndTheCommandZoneOfAPrivateDeck()
    {
        using var owner = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(owner, "private",
            new { name = "Sol Ring" },
            new { name = "Forest", quantity = 10 },
            new { name = "Krenko, Mob Boss", zone = "commander" },
            new { name = "Cultivate", zone = "considering" },
            new { name = "Lightning Bolt", zone = "sideboard" });

        var game = await (await SourceAsync()).GetDeckAsync(deck.Id, await AccountIdAsync(owner), Cancel);

        Assert.NotNull(game);
        Assert.Equal("Game deck", game.Name);
        Assert.Equal("commander", game.Format);
        Assert.Equal(["Forest", "Sol Ring"], game.Library.Select(card => card.Name).Order());
        Assert.Equal("Krenko, Mob Boss", Assert.Single(game.Command).Name);
        Assert.Equal(12, game.CardCount);
        var forest = game.Library.Single(card => card.Name == "Forest");
        Assert.True(forest.IsLand);
        Assert.Equal(10, forest.Quantity);
        var solRing = game.Library.Single(card => card.Name == "Sol Ring");
        Assert.False(solRing.IsLand);
        Assert.Equal(1m, solRing.ManaValue);
        Assert.Equal(deck.Cards[solRing.OracleId].Name, solRing.Name);
    }

    [Fact]
    public async Task OtherUsersAndGuestsGetOnlyUnlistedAndPublicDecks()
    {
        using var owner = await instance.NewUserAsync();
        using var other = await instance.NewUserAsync();
        var otherId = await AccountIdAsync(other);
        var source = await SourceAsync();
        var hidden = await CreateDeckAsync(owner, "private", new { name = "Sol Ring" });
        var shown = await CreateDeckAsync(owner, "public", new { name = "Sol Ring" });
        var unlisted = await CreateDeckAsync(owner, "unlisted", new { name = "Sol Ring" });

        Assert.Null(await source.GetDeckAsync(hidden.Id, otherId, Cancel));
        Assert.Null(await source.GetDeckAsync(hidden.Id, null, Cancel));
        Assert.NotNull(await source.GetDeckAsync(shown.Id, otherId, Cancel));
        Assert.NotNull(await source.GetDeckAsync(shown.Id, null, Cancel));
        Assert.NotNull(await source.GetDeckAsync(unlisted.Id, null, Cancel));
    }

    [Fact]
    public async Task UnknownDeckGivesNoDeck()
    {
        var source = await SourceAsync();

        Assert.Null(await source.GetDeckAsync(Guid.NewGuid(), null, Cancel));
    }

    [Fact]
    public async Task TextGivesTheLibraryAndTheCommandZone()
    {
        var source = await SourceAsync();
        const string text = """
            Commander
            1 Krenko, Mob Boss

            Main
            1 Sol Ring
            12 Forest

            Sideboard
            1 Lightning Bolt
            """;

        var game = await source.ParseDeckTextAsync(text, Cancel);

        Assert.Equal(string.Empty, game.Name);
        Assert.Null(game.Format);
        Assert.Equal(["Forest", "Sol Ring"], game.Library.Select(card => card.Name).Order());
        Assert.Equal("Krenko, Mob Boss", Assert.Single(game.Command).Name);
        Assert.Equal(14, game.CardCount);
        Assert.All(game.Library, card => Assert.NotNull(card.ImageUri));
    }

    [Fact]
    public async Task TextWithUnknownCardsGivesEachLine()
    {
        var source = await SourceAsync();

        var exception = await Assert.ThrowsAsync<GameDeckTextException>(() => source.ParseDeckTextAsync("1 Sol Ring\n1 No Such Card\n0 Forest", Cancel));

        Assert.Equal([2, 3], exception.LineErrors.Select(error => error.Line));
        Assert.Equal((DeckTextService.CardNotFound, "1 No Such Card"), (exception.LineErrors[0].Code, exception.LineErrors[0].Text));
        Assert.Equal(DeckText.QuantityOutOfRange, exception.LineErrors[1].Code);
    }

    private async Task<IGameDeckSource> SourceAsync() => (await instance.GetAsync()).Services.GetRequiredService<IGameDeckSource>();

    private static async Task<Guid> AccountIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(new Uri("/api/v1/me", UriKind.Relative), Cancel);
        return me.GetProperty("account").GetProperty("id").GetGuid();
    }

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string visibility, params object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name = "Game deck", format = "commander", visibility, lines }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}
