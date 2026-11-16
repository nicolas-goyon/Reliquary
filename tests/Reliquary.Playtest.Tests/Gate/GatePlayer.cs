using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Reliquary.Playtest.Engine;
using Reliquary.Playtest.Views;

namespace Reliquary.Playtest.Tests.Gate;

/// <summary>
/// One player of the gate test: a connection to one API host, and the view of the player, rebuilt from the patches
/// with <see cref="PlayerViews.Apply"/>, as the web app does. The player checks each new view against the view of the server.
/// </summary>
/// <remarks>
/// One task applies the events in their order: the patches, the snapshots of <c>Join</c> and a tick for the gap timeout.
/// The patches go in a reorder buffer by sequence number, as in hubClient.ts.
/// </remarks>
internal sealed class GatePlayer : IAsyncDisposable
{
    /// <summary>A gap in the sequence numbers that stays open for this time causes a <c>Resync</c>, as <c>gapTimeout</c> in hubClient.ts.</summary>
    public static readonly TimeSpan GapTimeout = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<WebApplicationFactory<Program>> hosts;
    private readonly string token;
    private readonly Channel<Event> events = Channel.CreateUnbounded<Event>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SortedDictionary<long, (IReadOnlyList<ViewChange> Changes, string Hash)> buffer = [];
    private readonly ConcurrentDictionary<long, long> arrivals = new();
    private readonly List<HubTestClient> connections = [];
    private readonly List<(long Seq, TaskCompletionSource Done)> waiters = [];
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stop = new();
    private Task? processor;
    private Task? ticker;
    private HubTestClient? client;
    private int hostIndex;
    private long appliedSeq = -1;
    private long? gapSince;

    public GatePlayer(string seat, string token, IReadOnlyList<WebApplicationFactory<Program>> hosts, int hostIndex)
    {
        Seat = seat;
        this.token = token;
        this.hosts = hosts;
        this.hostIndex = hostIndex;
    }

    /// <summary>Gets or sets the states of the server. The test sets it after the start, before the first snapshot.</summary>
    public GateTruth? Truth { get; set; }

    /// <summary>Gets the ID of the player: the number of the seat.</summary>
    public string Seat { get; }

    /// <summary>Gets the view of the player.</summary>
    public TableState? View { get; private set; }

    /// <summary>Gets the times from the send of <c>Act</c> to the receipt of the own patch, in milliseconds.</summary>
    public List<double> Latencies { get; } = [];

    /// <summary>Gets the desynchronizations: each one caused a <c>Resync</c>.</summary>
    public ConcurrentQueue<string> Desyncs { get; } = new();

    /// <summary>Gets the errors that the player found: a view that is not the view of the server, or a patch that cannot apply.</summary>
    public ConcurrentQueue<string> Errors { get; } = new();

    /// <summary>Gets the number of patches that the player applied.</summary>
    public int AppliedPatches { get; private set; }

    /// <summary>Gets the number of views that the player compared with the view of the server.</summary>
    public int CheckedViews { get; private set; }

    /// <summary>Gets all the connections of the player, also the closed ones, for the scan of the frames.</summary>
    public IReadOnlyList<HubTestClient> Connections => connections;

    /// <summary>Gets the sequence number of the view of the player.</summary>
    public long Seq => Interlocked.Read(ref appliedSeq);

    /// <summary>
    /// Connects to the current host and starts the task that applies the events.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        processor ??= Task.Run(ProcessAsync, CancellationToken.None);
        ticker ??= Task.Run(TickAsync, CancellationToken.None);
        var connection = await HubTestClient.ConnectAsync(hosts[hostIndex], token, cancellationToken);
        lock (gate)
        {
            connections.Add(connection);
            client = connection;
        }

        _ = Task.Run(() => ForwardAsync(connection), CancellationToken.None);
    }

    /// <summary>Calls a hub method on the current connection.</summary>
    public Task<JsonObject> InvokeAsync(string method, params object?[] arguments) => Current.InvokeAsync(method, arguments);

    /// <summary>
    /// Calls <c>Join</c> with the last sequence number and applies the snapshot.
    /// </summary>
    public async Task JoinAsync()
    {
        var answer = await Current.InvokeAsync("Join", View is null ? null : Seq);
        var snapshot = Result(answer, "Join").AsObject();
        if (snapshot["view"] is not null)
        {
            // Wait until the player compared the snapshot with the view of the server.
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await events.Writer.WriteAsync(new SnapshotEvent(snapshot, done));
            await done.Task.WaitAsync(TimeSpan.FromSeconds(20), stop.Token);
        }
    }

    /// <summary>
    /// Closes the connection with no message, as a lost network, and connects to the other API host.
    /// </summary>
    public async Task ReconnectToTheOtherHostAsync(CancellationToken cancellationToken)
    {
        Current.Drop();
        hostIndex = (hostIndex + 1) % hosts.Count;
        await ConnectAsync(cancellationToken);
        await JoinAsync();
    }

    /// <summary>
    /// Sends an action and waits until the view of the player has the change.
    /// </summary>
    /// <param name="clientActionId">The ID of the action.</param>
    /// <param name="action">The action.</param>
    /// <returns>The sequence number of the action.</returns>
    public async Task<long> ActAsync(string clientActionId, TableAction action)
    {
        var sent = Stopwatch.GetTimestamp();
        var seq = await SendActAsync(Current, clientActionId, action);
        await WaitForAsync(seq);
        if (arrivals.TryGetValue(seq, out var received))
        {
            lock (gate)
            {
                Latencies.Add(Stopwatch.GetElapsedTime(sent, received).TotalMilliseconds);
            }
        }

        return seq;
    }

    /// <summary>
    /// Sends an action on the current connection, and gives its sequence number. It does not wait for the patch.
    /// </summary>
    public Task<long> SendActAsync(string clientActionId, TableAction action) => SendActAsync(Current, clientActionId, action);

    /// <summary>
    /// Waits until the view of the player has the sequence number, or a later one.
    /// </summary>
    public async Task WaitForAsync(long seq)
    {
        TaskCompletionSource done;
        lock (gate)
        {
            if (appliedSeq >= seq)
            {
                return;
            }

            done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters.Add((seq, done));
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(20), stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        events.Writer.TryComplete();
        await stop.CancelAsync();
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }

        foreach (var task in new[] { processor, ticker })
        {
            if (task is not null)
            {
                await task.ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        stop.Dispose();
    }

    private HubTestClient Current
    {
        get
        {
            lock (gate)
            {
                return client ?? throw new InvalidOperationException("The player is not connected.");
            }
        }
    }

    private static async Task<long> SendActAsync(HubTestClient connection, string clientActionId, TableAction action)
    {
        var json = JsonSerializer.SerializeToNode(action, TableJson.Options);
        var answer = await connection.InvokeAsync("Act", clientActionId, json);
        var result = Result(answer, "Act");
        return result["accepted"]?.GetValue<long>()
            ?? throw new InvalidOperationException($"The server refused the action {json}: {result["rejected"]}.");
    }

    private static JsonNode Result(JsonObject answer, string method) =>
        answer.ContainsKey("error")
            ? throw new InvalidOperationException($"{method} failed: {answer["error"]}")
            : answer["result"] ?? throw new InvalidOperationException($"{method} gave no result.");

    private async Task ForwardAsync(HubTestClient connection)
    {
        try
        {
            await foreach (var (target, argument) in connection.Messages.Reader.ReadAllAsync(stop.Token))
            {
                var received = Stopwatch.GetTimestamp();
                switch (target)
                {
                    case "Patch":
                        var patch = argument!.AsObject();
                        var seq = patch["seq"]!.GetValue<long>();
                        arrivals.TryAdd(seq, received);
                        var changes = patch["changes"].Deserialize<List<ViewChange>>(TableJson.Options)!;
                        await events.Writer.WriteAsync(new PatchEvent(seq, changes, patch["hash"]!.GetValue<string>()));
                        break;
                    case "Closed":
                    case "Close":
                        Errors.Enqueue($"Seat {Seat}: the server closed the connection: {argument?.ToJsonString()}.");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }

    private async Task TickAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(100, stop.Token);
                events.Writer.TryWrite(new TickEvent());
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var item in events.Reader.ReadAllAsync(stop.Token))
            {
                switch (item)
                {
                    case SnapshotEvent snapshot:
                        await AcceptSnapshotAsync(snapshot.Snapshot);
                        snapshot.Done.TrySetResult();
                        break;
                    case PatchEvent patch when View is null || patch.Seq > appliedSeq:
                        buffer[patch.Seq] = (patch.Changes, patch.Hash);
                        break;
                    case TickEvent when gapSince is { } since && Stopwatch.GetElapsedTime(since) >= GapTimeout:
                        await ResyncAsync("gap");
                        break;
                }

                await DrainAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or ChannelClosedException or TimeoutException)
        {
            Errors.Enqueue($"Seat {Seat}: the player stopped: {exception.Message}");
        }
    }

    private async Task DrainAsync()
    {
        if (View is null)
        {
            return;
        }

        foreach (var old in buffer.Keys.Where(seq => seq <= appliedSeq).ToList())
        {
            buffer.Remove(old);
        }

        while (buffer.Remove(appliedSeq + 1, out var patch))
        {
            TableState next;
            try
            {
                next = PlayerViews.Apply(View, patch.Changes);
            }
            catch (ArgumentException exception)
            {
                Errors.Enqueue($"Seat {Seat}: the patch {appliedSeq + 1} cannot apply: {exception.Message}");
                await ResyncAsync("patch");
                return;
            }

            AppliedPatches++;
            if (PlayerViews.Hash(next) != patch.Hash)
            {
                await ReportHashAsync(next, appliedSeq + 1, patch.Changes);
                await ResyncAsync("hash");
                return;
            }

            await SetViewAsync(next, appliedSeq + 1);
        }

        gapSince = buffer.Count > 0 ? gapSince ?? Stopwatch.GetTimestamp() : null;
    }

    private async Task AcceptSnapshotAsync(JsonObject snapshot)
    {
        var seq = snapshot["seq"]!.GetValue<long>();
        var view = snapshot["view"].Deserialize<TableState>(TableJson.Options)!;
        if (PlayerViews.Hash(view) != snapshot["hash"]!.GetValue<string>())
        {
            Errors.Enqueue($"Seat {Seat}: the hash of the snapshot {seq} is not the hash of its view.");
        }

        if (View is not null && seq <= appliedSeq)
        {
            // The patches gave a newer view before the snapshot. The snapshot must still be the view of the server at its number.
            await CompareAsync(view, seq);
            return;
        }

        await SetViewAsync(view, seq);
    }

    private async Task ResyncAsync(string reason)
    {
        Desyncs.Enqueue($"Seat {Seat}: {reason} at the sequence number {appliedSeq + 1}.");
        gapSince = null;
        var answer = await Current.InvokeAsync("Resync", reason);
        var snapshot = Result(answer, "Resync").AsObject();
        await SetViewAsync(snapshot["view"].Deserialize<TableState>(TableJson.Options)!, snapshot["seq"]!.GetValue<long>());
    }

    private async Task SetViewAsync(TableState view, long seq)
    {
        await CompareAsync(view, seq);
        View = view;
        lock (gate)
        {
            appliedSeq = seq;
            foreach (var waiter in waiters.Where(item => item.Seq <= seq).ToList())
            {
                waiters.Remove(waiter);
                waiter.Done.TrySetResult();
            }
        }
    }

    /// <summary>Tells why the hash of a view is not the hash of its patch: the difference with the view of the server.</summary>
    private async Task ReportHashAsync(TableState view, long seq, IReadOnlyList<ViewChange> changes)
    {
        var truth = Truth ?? throw new InvalidOperationException("The test did not set the states of the server.");
        var expected = PlayerViews.View(await truth.StateAtAsync(seq), Seat);
        var patch = JsonSerializer.Serialize(changes, TableJson.Options);
        Errors.Enqueue($"Seat {Seat}: the hash of the view at {seq} is not the hash of the patch. "
            + $"Difference with the server: {ViewComparer.Difference(expected, view) ?? "none"}. "
            + $"Hash of the server view: {PlayerViews.Hash(expected)}, of the client view: {PlayerViews.Hash(view)}. "
            + $"Patch: {patch[..Math.Min(patch.Length, 1500)]}");
    }

    private async Task CompareAsync(TableState view, long seq)
    {
        var truth = Truth ?? throw new InvalidOperationException("The test did not set the states of the server.");
        var expected = PlayerViews.View(await truth.StateAtAsync(seq), Seat);
        CheckedViews++;
        if (ViewComparer.Difference(expected, view) is { } difference)
        {
            Errors.Enqueue($"Seat {Seat}: the view at the sequence number {seq} is not the view of the server: {difference}");
        }
    }

    private abstract record Event;

    private sealed record PatchEvent(long Seq, IReadOnlyList<ViewChange> Changes, string Hash) : Event;

    private sealed record SnapshotEvent(JsonObject Snapshot, TaskCompletionSource Done) : Event;

    private sealed record TickEvent : Event;
}
