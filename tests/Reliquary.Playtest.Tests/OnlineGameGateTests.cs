using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Reliquary.BuildingBlocks;
using Reliquary.Playtest.Api;
using Reliquary.Playtest.Engine;
using Reliquary.Playtest.Tests.Gate;
using Reliquary.Testing;
using StackExchange.Redis;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// The gate test of the milestone M3: an online game with 4 players runs one hour with no desynchronization.
/// See section "Gate test" of docs/architecture/online-games.md.
/// </summary>
/// <remarks>
/// <para>Two API hosts share one Redis: two replicas with the real backplane. Two players connect to each host.
/// One hour of a 4-player game has about 2,400 actions. The test sends 4,000 random actions, legal for the player that sends them,
/// with all the action types.</para>
/// <para>Faults: every 300 actions, one player sends an action, loses the connection, connects to the other host and sends the action again.
/// Some actions of two or four players go at the same time (conflicts of the compare-and-set). Some actions go two times with the same ID.</para>
/// <para>Each player rebuilds the view from the patches with <see cref="Views.PlayerViews.Apply"/> and checks the hash of each patch.
/// After each change, the view of each player must be equal to the view of the server: the state in Redis at the start,
/// with the actions of the action stream. No frame to a player has a card ID or an Oracle ID of a hidden card of another player.</para>
/// </remarks>
[Collection(PlaytestGroup.Name)]
public sealed class OnlineGameGateTests(PlaytestInstance instance)
{
    /// <summary>The number of actions: one hour of a 4-player game, with a margin.</summary>
    private const int Actions = 4000;

    /// <summary>The number of actions of each player in the test with no pause between the actions.</summary>
    private const int ContinuousActionsPerPlayer = 100;

    /// <summary>The number of connections of each player in the test with no pause.</summary>
    private const int TabsPerPlayer = 3;

    /// <summary>The number of actions between two reconnects to the other host.</summary>
    private const int ReconnectEvery = 300;

    /// <summary>The part of the actions that the player sends again with the same ID.</summary>
    private const double DuplicateRate = 0.03;

    /// <summary>The seed of the random actions. A failure message gives it.</summary>
    private const int Seed = 236;

    /// <summary>The maximum 95th percentile of the time from the send of an action to the receipt of the patch of the player, in milliseconds.</summary>
    private const double MaxP95Milliseconds = 200;

    private static readonly string[] Commanders = ["Krenko, Mob Boss", "Atraxa, Praetors' Voice", "Gisela, the Broken Blade", "Zilortha, Strength Incarnate"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlineGameWithFourPlayersHasNoDesync()
    {
        var clock = Stopwatch.StartNew();
        using var metrics = new GateMetrics();
        WebApplicationFactory<Program>[] hosts = [await instance.GetAsync(), await instance.GetSecondAsync()];
        var (gameId, tokens) = await NewGameAsync();
        var players = tokens.Select((token, seat) => new GatePlayer(SeatId(seat), token, hosts, seat % 2)).ToList();
        await using var redis = await ConnectionMultiplexer.ConnectAsync(TestServers.Redis);
        GateTruth? truth = null;
        try
        {
            foreach (var player in players)
            {
                await player.ConnectAsync(Cancel);
                await player.JoinAsync();
            }

            var start = await players[0].InvokeAsync("Start");
            Assert.False(start.ContainsKey("error"), start.ToJsonString());
            truth = await GateTruth.ReadStartAsync(redis.GetDatabase(), gameId);
            foreach (var player in players)
            {
                player.Truth = truth;
                await player.JoinAsync();
                await player.WaitForAsync(truth.StartSeq);
            }

            var run = await RunActionsAsync(players);

            // The end: each player has the last change, and a new snapshot is equal to the view of the server.
            var (storedSeq, storedTable) = await truth.ReadStoredAsync();
            foreach (var player in players)
            {
                await player.WaitForAsync(storedSeq);
                await player.JoinAsync();
            }

            var replayed = await truth.StateAtAsync(storedSeq);
            var leaks = new List<string>();
            var messages = 0;
            foreach (var player in players)
            {
                var scan = await HiddenCardScan.ScanAsync(player.Seat, player.Connections.SelectMany(connection => connection.CopyFrames()), truth);
                leaks.AddRange(scan.Leaks);
                messages += scan.Messages;
            }

            var latencies = players.SelectMany(player => player.Latencies).Order().ToList();
            var p95 = Percentile(latencies, 0.95);
            var desyncs = players.SelectMany(player => player.Desyncs).ToList();
            var server = metrics.ServerMilliseconds.Order().ToList();
            var errors = players.SelectMany(player => player.Errors).ToList();
            var output = TestContext.Current.TestOutputHelper;
            output?.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Gate: {Actions} actions, {run.Duplicates} duplicates, {run.Reconnects} reconnects, {run.Simultaneous} simultaneous rounds, "
                + $"{players.Sum(player => player.AppliedPatches)} patches applied, {players.Sum(player => player.CheckedViews)} views checked, {messages} messages scanned. "
                + $"Latency (ms): p50 {Percentile(latencies, 0.5):F1}, p95 {p95:F1}, max {latencies.LastOrDefault():F1}, {latencies.Count} samples. "
                + $"Server time (ms): p50 {Percentile(server, 0.5):F1}, p95 {Percentile(server, 0.95):F1}. "
                + $"Desyncs: {desyncs.Count}. Duration: {clock.Elapsed.TotalSeconds:F1} s. Seed: {Seed}."));

            foreach (var line in desyncs.Concat(errors).Take(20))
            {
                output?.WriteLine(line);
            }

            Assert.Empty(errors);
            Assert.Empty(desyncs);
            Assert.Empty(leaks);
            Assert.Equal(storedSeq, truth.LastSeq);
            // Each action is in the action stream one time: a retry with the same ID applied nothing.
            Assert.Equal(Actions, storedSeq - truth.StartSeq);
            Assert.Null(ViewComparer.Difference(storedTable, replayed));
            Assert.All(players, player => Assert.Equal(storedSeq, player.Seq));
            Assert.Equal(GateActions.AllTypes.Order(), run.Types.Order());
            Assert.True(latencies.Count >= Actions * 9 / 10, $"{latencies.Count} latency samples.");
            Assert.True(p95 < MaxP95Milliseconds, $"The p95 of the action latency is {p95:F1} ms. The limit is {MaxP95Milliseconds} ms.");
            Assert.Equal(0, metrics.Desyncs);
            Assert.True(metrics.Actions >= Actions, $"The metric of the action time has {metrics.Actions} values.");
        }
        finally
        {
            foreach (var player in players)
            {
                await player.DisposeAsync();
            }

            truth?.Dispose();
        }
    }

    /// <summary>
    /// The 4 players send actions with no pause, each from 3 connections, as a player with the game open in 3 browser tabs.
    /// SignalR runs one call at a time for each connection, so the test has 12 writers on one game, on two API hosts:
    /// many more conflicts of the compare-and-set than a real table. Each action must get a sequence number, not
    /// <c>service_unavailable</c>: the random wait between the attempts lets the conflicting writes go one after the other.
    /// </summary>
    [Fact]
    public async Task FourPlayersThatActWithNoPauseGetNoRefusal()
    {
        WebApplicationFactory<Program>[] hosts = [await instance.GetAsync(), await instance.GetSecondAsync()];
        var (_, tokens) = await NewGameAsync();
        var clients = new List<(HubTestClient Client, int Seat, int Tab)>();
        try
        {
            for (var tab = 0; tab < TabsPerPlayer; tab++)
            {
                for (var seat = 0; seat < tokens.Count; seat++)
                {
                    var client = await HubTestClient.ConnectAsync(hosts[(seat + tab) % 2], tokens[seat], Cancel);
                    clients.Add((client, seat, tab));
                    await client.NextAsync("Lobby");
                }
            }

            await clients[0].Client.InvokeAsync("Start");

            // Life changes are always legal, so a connection does not need a view to send the next action.
            async Task PlayAsync(HubTestClient client, int seat, int tab)
            {
                for (var index = tab; index < ContinuousActionsPerPlayer; index += TabsPerPlayer)
                {
                    var answer = await client.InvokeAsync("Act", $"run-{index}", new { type = "life", playerId = SeatId(seat), change = -1 });
                    Assert.True(answer["result"]?["accepted"] is not null, $"Seat {seat}, action {index}: {answer.ToJsonString()}");
                }
            }

            await Task.WhenAll(clients.Select(item => Task.Run(() => PlayAsync(item.Client, item.Seat, item.Tab))));
            var view = (await clients[0].Client.InvokeAsync("Resync", "view"))["result"]!["view"]!;

            // Each action applied one time: each player lost one life point for each action.
            Assert.All(view["players"]!.AsArray(), player => Assert.Equal(40 - ContinuousActionsPerPlayer, player!["life"]!.GetValue<int>()));
        }
        finally
        {
            foreach (var (client, _, _) in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    private static async Task<RunResult> RunActionsAsync(List<GatePlayer> players)
    {
        var random = new Random(Seed);
        var randoms = players.Select((_, index) => new Random((Seed * 10) + index)).ToList();
        var types = new HashSet<string>(StringComparer.Ordinal);
        var result = new RunResult(types);
        var actionId = 0;
        string NextId() => $"gate-{++actionId}";
        TableAction Make(int index)
        {
            var action = GateActions.Next(randoms[index], players[index].View!, players[index].Seat);
            types.Add(GateActions.TypeOf(action));
            return action;
        }

        // The opening: two players take a mulligan.
        await Task.WhenAll(players[1].ActAsync(NextId(), new MulliganAction("1")), players[3].ActAsync(NextId(), new MulliganAction("3")));
        types.Add("mulligan");
        var done = 2;
        var nextReconnect = ReconnectEvery;
        while (done < Actions)
        {
            if (done >= nextReconnect)
            {
                await ReconnectAsync(players[result.Reconnects % players.Count], NextId(), Make(result.Reconnects % players.Count));
                result.Reconnects++;
                nextReconnect += ReconnectEvery;
                done++;
                continue;
            }

            // Most actions go alone. Some go at the same time as the action of another player, or of all the players.
            var roll = random.NextDouble();
            var size = Math.Min(roll < 0.8 ? 1 : roll < 0.95 ? 2 : players.Count, Actions - done);
            var chosen = Enumerable.Range(0, players.Count).OrderBy(_ => random.Next()).Take(size).ToList();
            var round = chosen.Select(index => (Index: index, Id: NextId(), Action: Make(index))).ToList();
            var seqs = await Task.WhenAll(round.Select(item => players[item.Index].ActAsync(item.Id, item.Action)));
            result.Simultaneous += size > 1 ? 1 : 0;
            done += size;

            for (var position = 0; position < round.Count; position++)
            {
                if (random.NextDouble() < DuplicateRate)
                {
                    var (index, id, action) = round[position];
                    var again = await players[index].SendActAsync(id, action);
                    Assert.Equal(seqs[position], again);
                    result.Duplicates++;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The player sends an action and loses the connection before the answer. The player connects to the other host,
    /// gets a snapshot and sends the action again with the same ID, as hubClient.ts.
    /// </summary>
    private static async Task ReconnectAsync(GatePlayer player, string id, TableAction action)
    {
        var lost = player.SendActAsync(id, action);
        await player.ReconnectToTheOtherHostAsync(Cancel);
        long? first = null;
        try
        {
            first = await lost;
        }
        catch (InvalidOperationException)
        {
            // The connection closed before the answer. The server applied the action, or not.
        }

        var seq = await player.SendActAsync(id, action);
        if (first is not null)
        {
            Assert.Equal(first, seq);
        }

        await player.WaitForAsync(seq);
    }

    private static double Percentile(List<double> sorted, double rank) =>
        sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(rank * sorted.Count) - 1)];

    private static string SeatId(int seat) => seat.ToString(CultureInfo.InvariantCulture);

    private static string Deck(int seat) => $"""
        Commander
        1 {Commanders[seat]}

        Main
        1 Sol Ring
        1 Aether Vial
        1 Counterspell
        1 Cultivate
        1 Lightning Bolt
        1 Llanowar Elves
        1 Swords to Plowshares
        1 Thassa's Oracle
        1 Command Tower
        1 Demonic Consultation
        1 Jötun Grunt
        50 Forest
        """;

    /// <summary>Creates a game with 4 seats: an account and 3 guests, with a different deck for each seat.</summary>
    private async Task<(Guid GameId, List<string> Tokens)> NewGameAsync()
    {
        using var host = await instance.NewUserAsync("Ana");
        var response = await host.PostAsJsonAsync("/api/v1/playtest/games", new { deckText = Deck(0), seats = 4 }, Cancel);
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<GameSeatResponse>(Cancel))!;
        var tokens = new List<string> { created.GameToken };
        for (var seat = 1; seat < 4; seat++)
        {
            using var guest = await instance.GuestAsync();
            var join = await guest.PostAsJsonAsync(
                $"/api/v1/playtest/games/{created.GameId}/join",
                new { inviteSecret = created.InviteSecret, nickname = $"Guest {seat}", deckText = Deck(seat) },
                Cancel);
            join.EnsureSuccessStatusCode();
            var seatResponse = (await join.Content.ReadFromJsonAsync<GameSeatResponse>(Cancel))!;
            Assert.Equal(SeatId(seat), seatResponse.Seat);
            tokens.Add(seatResponse.GameToken);
        }

        return (created.GameId, tokens);
    }

    private sealed class RunResult(HashSet<string> types)
    {
        public HashSet<string> Types { get; } = types;

        public int Reconnects { get; set; }

        public int Duplicates { get; set; }

        public int Simultaneous { get; set; }
    }

    /// <summary>
    /// Listens to the metrics of the online games during the test: the desynchronizations and the times of the actions.
    /// </summary>
    private sealed class GateMetrics : IDisposable
    {
        private readonly MeterListener listener = new();
        private long desyncs;
        private long actions;

        public GateMetrics()
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == Telemetry.Name && instrument.Name is PlaytestMetrics.DesyncsName or PlaytestMetrics.ActionDurationName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref desyncs, value));
            listener.SetMeasurementEventCallback<double>((_, value, _, _) =>
            {
                Interlocked.Increment(ref actions);
                lock (ServerMilliseconds)
                {
                    ServerMilliseconds.Add(value * 1000);
                }
            });
            listener.Start();
        }

        /// <summary>Gets the values of the histogram of the action time, in milliseconds.</summary>
        public List<double> ServerMilliseconds { get; } = [];

        public long Desyncs => Interlocked.Read(ref desyncs);

        public long Actions => Interlocked.Read(ref actions);

        public void Dispose() => listener.Dispose();
    }
}
