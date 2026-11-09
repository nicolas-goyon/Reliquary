using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// A small test client of the table hub: the JSON hub protocol of SignalR over a raw WebSocket, with no SignalR client package.
/// It is the C# side of web/src/playtest/online/hubClient.ts: the same handshake, the same messages and the same names.
/// </summary>
public sealed class HubTestClient : IAsyncDisposable
{
    private const char RecordSeparator = '\u001e';

    private readonly WebSocket socket;
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> pending = [];
    private readonly Lock gate = new();
    private readonly Task reader;
    private int nextId;

    private HubTestClient(WebSocket socket)
    {
        this.socket = socket;
        reader = Task.Run(ReadAsync);
    }

    /// <summary>Gets the messages that the server sent with no invocation ID: Lobby, Patch and Closed.</summary>
    public Channel<(string Target, JsonNode? Argument)> Messages { get; } = Channel.CreateUnbounded<(string, JsonNode?)>();

    /// <summary>Gets all the raw text frames that the server sent, for the checks of the hidden information.</summary>
    public List<string> Frames { get; } = [];

    /// <summary>
    /// Connects to the hub of an API host with a game token, as the web app: WebSockets only, no negotiation.
    /// </summary>
    public static async Task<HubTestClient> ConnectAsync(WebApplicationFactory<Program> host, string gameToken, CancellationToken cancellationToken)
    {
        var client = host.Server.CreateWebSocketClient();
        var socket = await client.ConnectAsync(new Uri(host.Server.BaseAddress, $"/api/v1/playtest/hub?access_token={Uri.EscapeDataString(gameToken)}"), cancellationToken);
        var hub = new HubTestClient(socket);
        await hub.SendAsync("""{"protocol":"json","version":1}""", cancellationToken);
        return hub;
    }

    /// <summary>
    /// Calls a hub method and waits for its completion.
    /// </summary>
    /// <returns>The completion message: <c>result</c> or <c>error</c>.</returns>
    public async Task<JsonObject> InvokeAsync(string method, params object?[] arguments)
    {
        var id = Interlocked.Increment(ref nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            pending[id] = completion;
        }

        var message = new JsonObject { ["type"] = 1, ["invocationId"] = id, ["target"] = method, ["arguments"] = JsonSerializer.SerializeToNode(arguments) };
        await SendAsync(message.ToJsonString(), stop.Token);
        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), stop.Token);
    }

    /// <summary>
    /// Waits for the next message of the server with a target, and drops the other messages.
    /// </summary>
    public async Task<JsonNode?> NextAsync(string target, TimeSpan? timeout = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        limit.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            var (name, argument) = await Messages.Reader.ReadAsync(limit.Token);
            if (name == target)
            {
                return argument;
            }
        }
    }

    /// <summary>Closes the WebSocket with no message, as a lost connection.</summary>
    public void Drop() => socket.Abort();

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        socket.Abort();
        try
        {
            await reader;
        }
        catch (OperationCanceledException)
        {
        }

        socket.Dispose();
        stop.Dispose();
    }

    private Task SendAsync(string text, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text + RecordSeparator), WebSocketMessageType.Text, true, cancellationToken);

    private async Task ReadAsync()
    {
        var buffer = new byte[64 * 1024];
        // The bytes of one message. A UTF-8 character can be split between two receives, so decode the full message only.
        using var message = new MemoryStream();
        var handshake = true;
        try
        {
            while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, stop.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var frame = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                lock (gate)
                {
                    Frames.Add(frame);
                }

                foreach (var record in frame.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (handshake)
                    {
                        handshake = false;
                        continue;
                    }

                    Handle(JsonNode.Parse(record)!.AsObject());
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException or IOException)
        {
            // The connection closed: Drop, DisposeAsync or the server.
        }
        finally
        {
            Messages.Writer.TryComplete();
            lock (gate)
            {
                foreach (var completion in pending.Values)
                {
                    completion.TrySetException(new InvalidOperationException("The connection closed before the answer."));
                }

                pending.Clear();
            }
        }
    }

    private void Handle(JsonObject message)
    {
        switch (message["type"]!.GetValue<int>())
        {
            case 1:
                Messages.Writer.TryWrite((message["target"]!.GetValue<string>(), message["arguments"]!.AsArray().FirstOrDefault()?.DeepClone()));
                break;
            case 3:
                TaskCompletionSource<JsonObject>? completion;
                lock (gate)
                {
                    pending.Remove(message["invocationId"]!.GetValue<string>(), out completion);
                }

                completion?.TrySetResult(message);
                break;
            case 7:
                Messages.Writer.TryWrite(("Close", message.DeepClone()));
                break;
        }
    }
}
