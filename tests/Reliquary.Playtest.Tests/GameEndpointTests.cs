using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Playtest.Api;
using Reliquary.Playtest.Sessions;

namespace Reliquary.Playtest.Tests;

[Collection(PlaytestGroup.Name)]
public sealed class GameEndpointTests(PlaytestInstance instance)
{
    private const string GuestDeck = """
        Commander
        1 Krenko, Mob Boss

        Main
        1 Sol Ring
        20 Forest

        Sideboard
        1 Lightning Bolt
        """;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HostCreatesAGameWithASeatATokenAndAnInvite()
    {
        using var host = await instance.NewUserAsync("Ana");
        var deck = await CreateDeckAsync(host, "private");

        var response = await host.PostAsJsonAsync("/api/v1/playtest/games", new { deckId = deck, seats = 3 }, Cancel);
        var created = await ReadAsync<GameSeatResponse>(response, HttpStatusCode.Created);

        Assert.Equal($"/api/v1/playtest/games/{created.GameId}", response.Headers.Location?.OriginalString);
        Assert.Equal("0", created.Seat);
        Assert.False(string.IsNullOrEmpty(created.GameToken));
        Assert.Equal(22, Base64UrlLength(created.InviteSecret!));
        Assert.NotNull(created.InviteExpiresAt);
        Assert.Equal("commander", created.Lobby.Format);
        Assert.Equal(3, created.Lobby.Size);
        Assert.False(created.Lobby.Started);
        Assert.Equal("0", created.Lobby.HostSeat);
        var seat = Assert.Single(created.Lobby.Seats);
        Assert.Equal(("0", "Ana", true, false, 22, "Game deck"), (seat.Seat, seat.Nickname, seat.Ready, seat.Guest, seat.CardCount, seat.DeckName));
    }

    [Fact]
    public async Task GuestCannotCreateAGame()
    {
        using var guest = await instance.GuestAsync();

        var response = await guest.PostAsJsonAsync("/api/v1/playtest/games", new { deckText = GuestDeck }, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GuestJoinsWithADeckTextAndAccountJoinsWithAPublicDeck()
    {
        using var host = await instance.NewUserAsync("Ana");
        using var player = await instance.NewUserAsync("Bob");
        using var guest = await instance.GuestAsync();
        var created = await CreateGameAsync(host);
        var publicDeck = await CreateDeckAsync(host, "public");

        var guestSeat = await ReadAsync<GameSeatResponse>(
            await guest.PostAsJsonAsync($"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = created.InviteSecret, nickname = " Cleo ", deckText = GuestDeck }, Cancel));
        var playerSeat = await ReadAsync<GameSeatResponse>(
            await player.PostAsJsonAsync($"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = created.InviteSecret, deckId = publicDeck }, Cancel));

        Assert.Equal(("1", "2"), (guestSeat.Seat, playerSeat.Seat));
        Assert.Null(guestSeat.InviteSecret);
        Assert.Equal(
            [("0", "Ana", false, 22), ("1", "Cleo", true, 22), ("2", "Bob", false, 22)],
            playerSeat.Lobby.Seats.Select(seat => (seat.Seat, seat.Nickname, seat.Guest, seat.CardCount)));
        // A player sees the name of their own deck only.
        Assert.Equal([null, null, "Game deck"], playerSeat.Lobby.Seats.Select(seat => seat.DeckName));
    }

    [Fact]
    public async Task SeatCardsHaveRandomIdsAndNoSideboard()
    {
        using var host = await instance.NewUserAsync();
        var created = await CreateGameAsync(host);
        using var guest = await instance.GuestAsync();
        await ReadAsync<GameSeatResponse>(
            await guest.PostAsJsonAsync($"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = created.InviteSecret, nickname = "Cleo", deckText = GuestDeck }, Cancel));

        var store = (await instance.GetAsync()).Services.GetRequiredService<GameStore<OnlineGame>>();
        var game = (await store.GetAsync(GameKeys.Id(created.GameId)))!.State;

        var deck = game.FindSeat(1)!.Deck;
        Assert.Equal(21, deck.Library.Count);
        Assert.Equal("Krenko, Mob Boss", Assert.Single(deck.Command).Name);
        Assert.DoesNotContain(deck.Library, card => card.Name == "Lightning Bolt");
        var ids = game.Seats.SelectMany(seat => seat.Deck.Library.Concat(seat.Deck.Command)).Select(card => card.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Equal(16, id.Length));
        Assert.All(deck.Library, card => Assert.DoesNotContain(card.OracleId, card.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WrongSecretAndUnknownGameGiveTheSameError()
    {
        using var host = await instance.NewUserAsync();
        using var guest = await instance.GuestAsync();
        var created = await CreateGameAsync(host);

        var wrong = await guest.PostAsJsonAsync($"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = "AAAAAAAAAAAAAAAAAAAAAA", nickname = "Cleo", deckText = GuestDeck }, Cancel);
        var unknown = await guest.PostAsJsonAsync($"/api/v1/playtest/games/{Guid.NewGuid()}/join", new { inviteSecret = created.InviteSecret, nickname = "Cleo", deckText = GuestDeck }, Cancel);

        await AssertProblemAsync(wrong, HttpStatusCode.NotFound, PlaytestErrorCodes.InviteInvalid);
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, PlaytestErrorCodes.InviteInvalid);
    }

    [Fact]
    public async Task GuestCannotUseAPrivateDeck()
    {
        using var host = await instance.NewUserAsync();
        using var guest = await instance.GuestAsync();
        var created = await CreateGameAsync(host);
        var privateDeck = await CreateDeckAsync(host, "private");

        var response = await guest.PostAsJsonAsync($"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = created.InviteSecret, nickname = "Cleo", deckId = privateDeck }, Cancel);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task JoinRequestChecksTheFields()
    {
        using var host = await instance.NewUserAsync();
        using var guest = await instance.GuestAsync();
        var created = await CreateGameAsync(host);
        var url = $"/api/v1/playtest/games/{created.GameId}/join";

        var noNickname = await guest.PostAsJsonAsync(url, new { inviteSecret = created.InviteSecret, deckText = GuestDeck }, Cancel);
        var longNickname = await guest.PostAsJsonAsync(url, new { inviteSecret = created.InviteSecret, nickname = new string('a', 25), deckText = GuestDeck }, Cancel);
        var controlNickname = await guest.PostAsJsonAsync(url, new { inviteSecret = created.InviteSecret, nickname = "a\u0007b", deckText = GuestDeck }, Cancel);
        var twoDecks = await guest.PostAsJsonAsync(url, new { inviteSecret = created.InviteSecret, nickname = "Cleo", deckText = GuestDeck, deckId = Guid.NewGuid() }, Cancel);
        var bigText = await guest.PostAsJsonAsync(url, new { inviteSecret = created.InviteSecret, nickname = "Cleo", deckText = string.Concat(Enumerable.Repeat("1 Forest\n", 1000)) }, Cancel);
        var badText = await guest.PostAsJsonAsync(url, new { inviteSecret = created.InviteSecret, nickname = "Cleo", deckText = "1 Sol Ring\n1 No Such Card" }, Cancel);

        Assert.Equal("nickname", (await AssertProblemAsync(noNickname, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("too_long", (await AssertProblemAsync(longNickname, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("invalid_format", (await AssertProblemAsync(controlNickname, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("deckId", (await AssertProblemAsync(twoDecks, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("deckText", (await AssertProblemAsync(bigText, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors")[0].GetProperty("field").GetString());
        var lines = (await AssertProblemAsync(badText, HttpStatusCode.BadRequest, PlaytestErrorCodes.DeckTextInvalid)).GetProperty("lineErrors");
        Assert.Equal((2, "card_not_found"), (lines[0].GetProperty("line").GetInt32(), lines[0].GetProperty("code").GetString()));
    }

    [Fact]
    public async Task FullGameRefusesANewPlayer()
    {
        using var host = await instance.NewUserAsync();
        var created = await CreateGameAsync(host, seats: 2);
        using var first = await instance.GuestAsync();
        using var second = await instance.GuestAsync();
        await JoinAsGuestAsync(first, created, "First");

        var response = await second.PostAsJsonAsync($"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = created.InviteSecret, nickname = "Second", deckText = GuestDeck }, Cancel);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, PlaytestErrorCodes.GameFull);
    }

    [Fact]
    public async Task AccountThatJoinsAgainGetsItsSeatAndTheOldTokenStops()
    {
        using var host = await instance.NewUserAsync();
        using var player = await instance.NewUserAsync("Bob");
        var created = await CreateGameAsync(host);
        var deck = await CreateDeckAsync(player, "private");
        var url = $"/api/v1/playtest/games/{created.GameId}/join";

        var first = await ReadAsync<GameSeatResponse>(await player.PostAsJsonAsync(url, new { inviteSecret = created.InviteSecret, deckId = deck }, Cancel));
        var second = await ReadAsync<GameSeatResponse>(await player.PostAsJsonAsync(url, new { inviteSecret = created.InviteSecret, deckId = deck }, Cancel));

        Assert.Equal(first.Seat, second.Seat);
        Assert.Equal(2, second.Lobby.Seats.Count);
        await AssertProblemAsync(await GetLobbyAsync(first.GameId, first.GameToken), HttpStatusCode.Unauthorized, PlaytestErrorCodes.TokenInvalid);
        Assert.Equal(HttpStatusCode.OK, (await GetLobbyAsync(second.GameId, second.GameToken)).StatusCode);
    }

    [Fact]
    public async Task LobbyNeedsAGameTokenOfTheGame()
    {
        using var host = await instance.NewUserAsync();
        var first = await CreateGameAsync(host);
        var second = await CreateGameAsync(host);

        var lobby = await ReadAsync<GameLobbyResponse>(await GetLobbyAsync(first.GameId, first.GameToken));

        Assert.Equal(("0", first.GameId), (lobby.YourSeat, lobby.GameId));
        await AssertProblemAsync(await GetLobbyAsync(first.GameId, null), HttpStatusCode.Unauthorized, PlaytestErrorCodes.TokenInvalid);
        await AssertProblemAsync(await GetLobbyAsync(first.GameId, second.GameToken), HttpStatusCode.Unauthorized, PlaytestErrorCodes.TokenInvalid);
        await AssertProblemAsync(await GetLobbyAsync(first.GameId, "not.a.token"), HttpStatusCode.Unauthorized, PlaytestErrorCodes.TokenInvalid);
    }

    [Fact]
    public async Task AccessTokenIsNotAGameToken()
    {
        using var host = await instance.NewUserAsync();
        var created = await CreateGameAsync(host);
        var accessToken = host.DefaultRequestHeaders.Authorization!.Parameter;

        await AssertProblemAsync(await GetLobbyAsync(created.GameId, accessToken), HttpStatusCode.Unauthorized, PlaytestErrorCodes.TokenInvalid);
    }

    [Fact]
    public async Task GameTokenIsNotAnAccessToken()
    {
        using var host = await instance.NewUserAsync();
        var created = await CreateGameAsync(host);
        using var client = await instance.GuestAsync();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", created.GameToken);

        var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HostKicksAPlayerAndTheTokenOfThePlayerStops()
    {
        using var host = await instance.NewUserAsync();
        using var guest = await instance.GuestAsync();
        var created = await CreateGameAsync(host);
        var seat = await JoinAsGuestAsync(guest, created, "Cleo");

        var byGuest = await SendAsync(HttpMethod.Delete, $"/api/v1/playtest/games/{created.GameId}/seats/0", seat.GameToken);
        var kick = await SendAsync(HttpMethod.Delete, $"/api/v1/playtest/games/{created.GameId}/seats/{seat.Seat}", created.GameToken);
        var again = await SendAsync(HttpMethod.Delete, $"/api/v1/playtest/games/{created.GameId}/seats/{seat.Seat}", created.GameToken);
        var host0 = await SendAsync(HttpMethod.Delete, $"/api/v1/playtest/games/{created.GameId}/seats/0", created.GameToken);

        await AssertProblemAsync(byGuest, HttpStatusCode.Forbidden, "forbidden");
        Assert.Single((await ReadAsync<GameLobbyResponse>(kick)).Seats);
        await AssertProblemAsync(again, HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(host0, HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(await GetLobbyAsync(created.GameId, seat.GameToken), HttpStatusCode.Unauthorized, PlaytestErrorCodes.TokenInvalid);
    }

    [Fact]
    public async Task NewInviteStopsTheOldLink()
    {
        using var host = await instance.NewUserAsync();
        using var guest = await instance.GuestAsync();
        var created = await CreateGameAsync(host);

        var invite = await ReadAsync<GameInviteResponse>(await SendAsync(HttpMethod.Post, $"/api/v1/playtest/games/{created.GameId}/invite", created.GameToken));
        var old = await guest.PostAsJsonAsync($"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = created.InviteSecret, nickname = "Cleo", deckText = GuestDeck }, Cancel);
        var seat = await JoinAsGuestAsync(guest, created with { InviteSecret = invite.InviteSecret }, "Cleo");

        Assert.NotEqual(created.InviteSecret, invite.InviteSecret);
        await AssertProblemAsync(old, HttpStatusCode.NotFound, PlaytestErrorCodes.InviteInvalid);
        Assert.Equal("1", seat.Seat);
        await AssertProblemAsync(await SendAsync(HttpMethod.Post, $"/api/v1/playtest/games/{created.GameId}/invite", seat.GameToken), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task HostClosesTheGameAndAllTokensStop()
    {
        using var host = await instance.NewUserAsync();
        using var guest = await instance.GuestAsync();
        var created = await CreateGameAsync(host);
        var seat = await JoinAsGuestAsync(guest, created, "Cleo");

        await AssertProblemAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/playtest/games/{created.GameId}", seat.GameToken), HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/v1/playtest/games/{created.GameId}", created.GameToken)).StatusCode);

        await AssertProblemAsync(await GetLobbyAsync(created.GameId, created.GameToken), HttpStatusCode.NotFound, "not_found");
        var join = await guest.PostAsJsonAsync($"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = created.InviteSecret, nickname = "Cleo", deckText = GuestDeck }, Cancel);
        await AssertProblemAsync(join, HttpStatusCode.NotFound, PlaytestErrorCodes.InviteInvalid);
    }

    [Fact]
    public async Task HostHasThreeOpenGamesAtMost()
    {
        using var host = await instance.NewUserAsync();
        var games = new List<GameSeatResponse>();
        for (var index = 0; index < 3; index++)
        {
            games.Add(await CreateGameAsync(host));
        }

        var fourth = await host.PostAsJsonAsync("/api/v1/playtest/games", new { deckText = GuestDeck }, Cancel);
        await SendAsync(HttpMethod.Delete, $"/api/v1/playtest/games/{games[0].GameId}", games[0].GameToken);
        var afterClose = await host.PostAsJsonAsync("/api/v1/playtest/games", new { deckText = GuestDeck }, Cancel);

        await AssertProblemAsync(fourth, HttpStatusCode.Conflict, PlaytestErrorCodes.OpenGameLimit);
        Assert.Equal(HttpStatusCode.Created, afterClose.StatusCode);
    }

    [Fact]
    public async Task CreateRequestChecksTheFields()
    {
        using var host = await instance.NewUserAsync();

        var seats = await host.PostAsJsonAsync("/api/v1/playtest/games", new { deckText = GuestDeck, seats = 5 }, Cancel);
        var format = await host.PostAsJsonAsync("/api/v1/playtest/games", new { deckText = GuestDeck, format = "Not A Format" }, Cancel);
        var noDeck = await host.PostAsJsonAsync("/api/v1/playtest/games", new { seats = 2 }, Cancel);
        var unknownDeck = await host.PostAsJsonAsync("/api/v1/playtest/games", new { deckId = Guid.NewGuid() }, Cancel);

        Assert.Equal("seats", (await AssertProblemAsync(seats, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("format", (await AssertProblemAsync(format, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("deckId", (await AssertProblemAsync(noDeck, HttpStatusCode.BadRequest, "validation_failed")).GetProperty("errors")[0].GetProperty("field").GetString());
        await AssertProblemAsync(unknownDeck, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task OpenApiDocumentDescribesTheGameEndpoints()
    {
        using var client = await instance.GuestAsync();

        var document = await client.GetFromJsonAsync<JsonElement>(new Uri("/openapi/v1.json", UriKind.Relative), Cancel);
        var paths = document.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/v1/playtest/games", out _));
        Assert.True(paths.TryGetProperty("/api/v1/playtest/games/{gameId}/join", out _));
        Assert.True(paths.TryGetProperty("/api/v1/playtest/games/{gameId}/seats/{seat}", out _));
    }

    private static async Task<GameSeatResponse> CreateGameAsync(HttpClient host, int seats = 4) =>
        await ReadAsync<GameSeatResponse>(await host.PostAsJsonAsync("/api/v1/playtest/games", new { deckText = GuestDeck, seats }, Cancel), HttpStatusCode.Created);

    private static async Task<GameSeatResponse> JoinAsGuestAsync(HttpClient guest, GameSeatResponse created, string nickname) =>
        await ReadAsync<GameSeatResponse>(await guest.PostAsJsonAsync(
            $"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = created.InviteSecret, nickname, deckText = GuestDeck }, Cancel));

    private Task<HttpResponseMessage> GetLobbyAsync(Guid gameId, string? token) => SendAsync(HttpMethod.Get, $"/api/v1/playtest/games/{gameId}", token);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token)
    {
        using var client = await instance.GuestAsync();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Add(GameEndpoints.TokenHeader, token);
        }

        return await client.SendAsync(request, Cancel);
    }

    private static async Task<Guid> CreateDeckAsync(HttpClient client, string visibility)
    {
        var lines = new object[] { new { name = "Krenko, Mob Boss", zone = "commander" }, new { name = "Sol Ring" }, new { name = "Forest", quantity = 20 }, new { name = "Lightning Bolt", zone = "sideboard" } };
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name = "Game deck", format = "commander", visibility, lines }, Cancel);
        return (await ReadAsync<JsonElement>(response, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return (await response.Content.ReadFromJsonAsync<T>(Cancel))!;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {body}");
        var problem = JsonDocument.Parse(body).RootElement.Clone();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    private static int Base64UrlLength(string value)
    {
        Assert.All(value, character => Assert.True(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        return value.Length;
    }
}
