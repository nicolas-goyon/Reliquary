using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reliquary.Playtest.Api;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// Checks the table hub with two API hosts on one Redis: two replicas with the real backplane.
/// The clients use the JSON hub protocol over a raw WebSocket, as the web app.
/// </summary>
[Collection(PlaytestGroup.Name)]
public sealed class TableHubTests(PlaytestInstance instance)
{
    private const string Deck = """
        Commander
        1 Krenko, Mob Boss

        Main
        1 Sol Ring
        20 Forest
        """;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PatchReachesAPlayerOnTheOtherReplica()
    {
        var (created, guest) = await NewGameWithGuestAsync();
        await using var host = await HubTestClient.ConnectAsync(await instance.GetAsync(), created.GameToken, Cancel);
        await using var player = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), guest.GameToken, Cancel);
        await JoinAsync(host);
        await JoinAsync(player);

        var start = await host.InvokeAsync("Start");
        var startSeq = start["result"]!.GetValue<long>();
        var startPatch = (await player.NextAsync("Patch"))!.AsObject();
        var snapshot = (await player.InvokeAsync("Resync"))["result"]!.AsObject();
        var act = await host.InvokeAsync("Act", "a-1", new { type = "draw", playerId = "0", count = 2 });
        var patch = (await player.NextAsync("Patch"))!.AsObject();

        Assert.Equal(startSeq, startPatch["seq"]!.GetValue<long>());
        Assert.Equal(startSeq, snapshot["seq"]!.GetValue<long>());
        Assert.Equal(startSeq + 1, act["result"]!["accepted"]!.GetValue<long>());
        Assert.Equal(startSeq + 1, patch["seq"]!.GetValue<long>());
        Assert.Equal("0", patch["actorSeat"]!.GetValue<string>());
        Assert.Contains("drew 2 cards", patch["log"]!.GetValue<string>(), StringComparison.Ordinal);
        // The player sees the change of the hand count of the host, but no card of the hand.
        var changes = patch["changes"]!.AsArray();
        var hand = changes.Single(change => change!["zone"]?.GetValue<string>() == "hand")!;
        Assert.Equal("0", hand["seat"]!.GetValue<string>());
        Assert.Equal(9, hand["cards"]!.AsArray().Count);
        Assert.All(hand["cards"]!.AsArray(), card => Assert.Equal(string.Empty, card!["oracleId"]!.GetValue<string>()));
        Assert.Equal(16, patch["hash"]!.GetValue<string>().Length);
    }

    [Fact]
    public async Task JoinBeforeTheStartGivesNoViewAndTheLobbyOfTheRestEndpoint()
    {
        var (created, guest) = await NewGameWithGuestAsync();
        await using var player = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), guest.GameToken, Cancel);

        var lobby = (await player.NextAsync("Lobby"))!;
        var snapshot = (await player.InvokeAsync("Join", (long?)null))["result"]!.AsObject();
        var rest = await GetLobbyJsonAsync(created.GameId, guest.GameToken);

        Assert.Null(snapshot["view"]);
        Assert.Equal(guest.Lobby.Seats.Count, lobby["seats"]!.AsArray().Count);
        Assert.True(JsonNode.DeepEquals(rest, lobby), $"{rest}\n{lobby}");
        Assert.Equal(["seats", "hostSeat", "size", "started", "gameId", "yourSeat", "format", "createdAt", "endsAt", "inviteExpiresAt"], lobby.AsObject().Select(pair => pair.Key));
    }

    [Fact]
    public async Task SnapshotHasNoCardOfAHiddenZoneOfAnotherPlayerAndNoSeed()
    {
        var (created, guest) = await NewGameWithGuestAsync();
        await using var host = await HubTestClient.ConnectAsync(await instance.GetAsync(), created.GameToken, Cancel);
        await using var player = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), guest.GameToken, Cancel);
        await JoinAsync(host);
        await JoinAsync(player);
        await host.InvokeAsync("Start");

        var hostView = (await host.InvokeAsync("Resync"))["result"]!["view"]!.AsObject();
        var playerView = (await player.InvokeAsync("Resync"))["result"]!["view"]!.AsObject();

        var hostHand = hostView["players"]![0]!["zones"]!["hand"]!.AsArray();
        Assert.Equal(7, hostHand.Count);
        Assert.All(hostHand, card => Assert.NotEqual(string.Empty, card!["oracleId"]!.GetValue<string>()));
        var hiddenIds = hostHand.Select(card => card!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(0, playerView["seed"]!.GetValue<long>());
        foreach (var frame in player.Frames)
        {
            Assert.All(hiddenIds, id => Assert.DoesNotContain(id, frame, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ActOnTheObjectsOfAnotherPlayerIsRefused()
    {
        var (created, guest) = await NewGameWithGuestAsync();
        await using var host = await HubTestClient.ConnectAsync(await instance.GetAsync(), created.GameToken, Cancel);
        await using var player = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), guest.GameToken, Cancel);
        await JoinAsync(host);
        await JoinAsync(player);
        await host.InvokeAsync("Start");

        var steal = await player.InvokeAsync("Act", "b-1", new { type = "life", playerId = "0", change = -5 });
        var turn = await player.InvokeAsync("Act", "b-2", new { type = "nextTurn" });
        var unknown = await player.InvokeAsync("Act", "b-3", new { type = "tap", playerId = "1", cardId = "nope" });
        var bad = await player.InvokeAsync("Act", "b-4", new { type = "explode", playerId = "1" });
        var big = await player.InvokeAsync("Act", "b-5", new { type = "draw", playerId = "1", count = 21 });
        var own = await player.InvokeAsync("Act", "b-6", new { type = "life", playerId = "1", change = -1 });

        Assert.Equal("not_your_object", steal["result"]!["rejected"]!.GetValue<string>());
        Assert.Equal("not_your_turn", turn["result"]!["rejected"]!.GetValue<string>());
        Assert.Equal("unknown_card", unknown["result"]!["rejected"]!.GetValue<string>());
        Assert.Equal("invalid_action", bad["result"]!["rejected"]!.GetValue<string>());
        Assert.Equal("invalid_action", big["result"]!["rejected"]!.GetValue<string>());
        Assert.NotNull(own["result"]!["accepted"]);
    }

    [Fact]
    public async Task RetryOfAnActionAppliesItOneTime()
    {
        var (created, guest) = await NewGameWithGuestAsync();
        await using var host = await HubTestClient.ConnectAsync(await instance.GetAsync(), created.GameToken, Cancel);
        await JoinAsync(host);
        await host.InvokeAsync("Start");

        var first = await host.InvokeAsync("Act", "same", new { type = "life", playerId = "0", change = -3 });

        // The client sends the action again after a reconnect to the other replica.
        host.Drop();
        await using var again = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), created.GameToken, Cancel);
        await JoinAsync(again);
        var retry = await again.InvokeAsync("Act", "same", new { type = "life", playerId = "0", change = -3 });
        var snapshot = (await again.InvokeAsync("Resync"))["result"]!.AsObject();

        Assert.Equal(first["result"]!["accepted"]!.GetValue<long>(), retry["result"]!["accepted"]!.GetValue<long>());
        Assert.Equal(37, snapshot["view"]!["players"]![0]!["life"]!.GetValue<int>());
    }

    [Fact]
    public async Task OnlyTheHostStartsWithTwoPlayers()
    {
        using var hostClient = await instance.NewUserAsync("Ana");
        var alone = await CreateAsync(hostClient);
        await using var host = await HubTestClient.ConnectAsync(await instance.GetAsync(), alone.GameToken, Cancel);
        await JoinAsync(host);

        var tooFew = await host.InvokeAsync("Start");

        Assert.Contains("HubException: game_not_enough_players:", tooFew["error"]!.GetValue<string>(), StringComparison.Ordinal);

        var (created, guest) = await NewGameWithGuestAsync();
        await using var player = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), guest.GameToken, Cancel);
        await JoinAsync(player);
        var notHost = await player.InvokeAsync("Start");
        Assert.Contains("HubException: forbidden:", notHost["error"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.NotNull(created);
    }

    [Fact]
    public async Task KickedPlayerGetsClosedOnTheOtherReplica()
    {
        var (created, guest) = await NewGameWithGuestAsync();
        await using var host = await HubTestClient.ConnectAsync(await instance.GetAsync(), created.GameToken, Cancel);
        await using var player = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), guest.GameToken, Cancel);
        await JoinAsync(host);
        await JoinAsync(player);

        await host.InvokeAsync("Kick", guest.Seat);
        var closed = await player.NextAsync("Closed");
        var lobby = await host.NextAsync("Lobby");

        Assert.Equal("kicked", closed!["reason"]!.GetValue<string>());
        Assert.Single(lobby!["seats"]!.AsArray());
    }

    [Fact]
    public async Task NewInviteGivesASecretAndLeaveFreesTheSeat()
    {
        var (created, guest) = await NewGameWithGuestAsync();
        await using var host = await HubTestClient.ConnectAsync(await instance.GetAsync(), created.GameToken, Cancel);
        await using var player = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), guest.GameToken, Cancel);
        await JoinAsync(host);
        await JoinAsync(player);

        var invite = await host.InvokeAsync("NewInvite");
        await player.InvokeAsync("Leave");
        var lobby = await host.NextAsync("Lobby");
        while (lobby!["seats"]!.AsArray().Count != 1)
        {
            lobby = await host.NextAsync("Lobby");
        }

        Assert.Equal(22, invite["result"]!["inviteSecret"]!.GetValue<string>().Length);
        Assert.Equal("0", lobby["seats"]![0]!["seat"]!.GetValue<string>());
    }

    [Fact]
    public async Task ConnectionWithAnUnknownTokenGetsClosed()
    {
        await using var client = await HubTestClient.ConnectAsync(await instance.GetAsync(), "not.a.token", Cancel);

        var closed = await client.NextAsync("Closed");

        Assert.Equal("kicked", closed!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task HostClosesTheGameForAllPlayers()
    {
        var (created, guest) = await NewGameWithGuestAsync();
        await using var host = await HubTestClient.ConnectAsync(await instance.GetAsync(), created.GameToken, Cancel);
        await using var player = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), guest.GameToken, Cancel);
        await JoinAsync(host);
        await JoinAsync(player);

        await host.InvokeAsync("Close");

        Assert.Equal("ended", (await player.NextAsync("Closed"))!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task ConcurrentActionsOfTwoPlayersOnTwoReplicasGetOneOrder()
    {
        var (created, guest) = await NewGameWithGuestAsync();
        await using var host = await HubTestClient.ConnectAsync(await instance.GetAsync(), created.GameToken, Cancel);
        await using var player = await HubTestClient.ConnectAsync(await instance.GetSecondAsync(), guest.GameToken, Cancel);
        await JoinAsync(host);
        await JoinAsync(player);
        var startSeq = (await host.InvokeAsync("Start"))["result"]!.GetValue<long>();

        var calls = Enumerable.Range(0, 20).SelectMany(index => new[]
        {
            host.InvokeAsync("Act", $"h-{index}", new { type = "life", playerId = "0", change = -1 }),
            player.InvokeAsync("Act", $"p-{index}", new { type = "life", playerId = "1", change = -1 }),
        }).ToList();
        var results = await Task.WhenAll(calls);
        var hostView = (await host.InvokeAsync("Resync"))["result"]!.AsObject();
        var playerView = (await player.InvokeAsync("Resync"))["result"]!.AsObject();

        var sequences = results.Select(result => result["result"]!["accepted"]!.GetValue<long>()).Order().ToList();
        Assert.Equal(Enumerable.Range(1, 40).Select(value => startSeq + value), sequences);
        Assert.Equal(startSeq + 40, hostView["seq"]!.GetValue<long>());
        Assert.Equal(20, hostView["view"]!["players"]![0]!["life"]!.GetValue<int>());
        Assert.Equal(20, playerView["view"]!["players"]![0]!["life"]!.GetValue<int>());
        Assert.Equal(20, playerView["view"]!["players"]![1]!["life"]!.GetValue<int>());
    }

    private static async Task JoinAsync(HubTestClient client)
    {
        await client.NextAsync("Lobby");
        var join = await client.InvokeAsync("Join", (long?)null);
        Assert.False(join.ContainsKey("error"), join.ToJsonString());
    }

    private async Task<(GameSeatResponse Created, GameSeatResponse Guest)> NewGameWithGuestAsync()
    {
        using var host = await instance.NewUserAsync("Ana");
        var created = await CreateAsync(host);
        using var guest = await instance.GuestAsync();
        var response = await guest.PostAsJsonAsync($"/api/v1/playtest/games/{created.GameId}/join", new { inviteSecret = created.InviteSecret, nickname = "Cleo", deckText = Deck }, Cancel);
        response.EnsureSuccessStatusCode();
        return (created, (await response.Content.ReadFromJsonAsync<GameSeatResponse>(Cancel))!);
    }

    private static async Task<GameSeatResponse> CreateAsync(HttpClient host)
    {
        var response = await host.PostAsJsonAsync("/api/v1/playtest/games", new { deckText = Deck, seats = 2 }, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameSeatResponse>(Cancel))!;
    }

    private async Task<JsonNode> GetLobbyJsonAsync(Guid gameId, string token)
    {
        using var client = await instance.GuestAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/v1/playtest/games/{gameId}", UriKind.Relative));
        request.Headers.Add(GameEndpoints.TokenHeader, token);
        var response = await client.SendAsync(request, Cancel);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;
    }
}
