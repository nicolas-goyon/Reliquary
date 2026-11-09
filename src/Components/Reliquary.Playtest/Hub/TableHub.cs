using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Playtest.Engine;
using Reliquary.Playtest.Sessions;
using Reliquary.Playtest.Views;
using StackExchange.Redis;

namespace Reliquary.Playtest.Hub;

/// <summary>
/// The full view of a player at one sequence number: the result of <c>Join</c> and <c>Resync</c>.
/// </summary>
/// <param name="Seq">The sequence number of the last change in the view.</param>
/// <param name="View">The view of the player, or <see langword="null"/> before the start of the game.</param>
/// <param name="Hash">The hash of the view, or an empty text before the start.</param>
public sealed record HubSnapshot(long Seq, JsonObject? View, string Hash);

/// <summary>
/// The changes of one change of the game in the view of one player: the message <c>Patch</c>.
/// </summary>
/// <param name="Seq">The sequence number of the change.</param>
/// <param name="ActorSeat">The seat that did the change.</param>
/// <param name="Log">The public text of the change.</param>
/// <param name="Changes">The changes of the view. A change of the lobby and the start have no change: the client asks for a full view.</param>
/// <param name="Hash">The hash of the view after the change, or an empty text when the patch has no view.</param>
public sealed record HubPatch(long Seq, string ActorSeat, string Log, IReadOnlyList<ViewChange> Changes, string Hash);

/// <summary>
/// The table hub of the online games: SignalR with the JSON hub protocol, WebSockets only. See docs/architecture/online-games.md.
/// </summary>
/// <remarks>
/// <para>The client sends the game token in the query parameter <c>access_token</c>. Each method checks the token again,
/// so a kick or a new join stops the old connection at its next call.</para>
/// <para>The hub is thin: it calls <see cref="GameService"/> and sends the results with <see cref="TableBroadcaster"/>.
/// The order of the changes is the sequence number of the game store, not the order of the messages: the client keeps a reorder buffer.</para>
/// </remarks>
internal sealed class TableHub(GameService games, GameTokens tokens, TableBroadcaster broadcaster, ILogger<TableHub> logger) : Microsoft.AspNetCore.SignalR.Hub
{
    /// <summary>The path of the hub in the public API.</summary>
    public const string Path = "/playtest/hub";

    /// <summary>The maximum number of actions of one connection in one second, on average.</summary>
    public const double ActionsPerSecond = 10;

    /// <summary>The maximum number of actions of one connection in a burst.</summary>
    public const double ActionBurst = 30;

    private const string AccessItem = "reliquary.access";

    private const string BucketItem = "reliquary.bucket";

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var token = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
        var claims = await tokens.ReadAsync(token);
        if (claims is null)
        {
            await CloseAsync("kicked");
            return;
        }

        try
        {
            var (stored, seat) = await games.ReadForSeatAsync(claims.GameId, token);
            Context.Items[AccessItem] = new SeatAccess(claims.GameId, seat, token!);
            Context.Items[BucketItem] = new ActionBucket(ActionsPerSecond, ActionBurst);
            await Groups.AddToGroupAsync(Context.ConnectionId, TableBroadcaster.SeatGroup(claims.GameId, seat, claims.TokenId));
            await Clients.Caller.SendAsync("Lobby", TableBroadcaster.Lobby(stored.State, seat));
        }
        catch (GameException exception)
        {
            await CloseAsync(ClosedReason(exception) ?? "ended");
            return;
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Gives the full view of the caller. The client calls it after each connection, with the last sequence number that it knows.
    /// </summary>
    /// <param name="lastSeq">The last sequence number of the client, or <see langword="null"/>. The hub always gives a full view.</param>
    /// <returns>The snapshot.</returns>
    public Task<HubSnapshot> Join(long? lastSeq) => SnapshotAsync();

    /// <summary>
    /// Gives the full view of the caller again. The client calls it when its view is not in sync.
    /// </summary>
    /// <returns>The snapshot.</returns>
    public Task<HubSnapshot> Resync() => SnapshotAsync();

    /// <summary>
    /// Applies an action of the caller to the table.
    /// </summary>
    /// <param name="clientActionId">The ID that the client gave to the action. A retry with the same ID does not apply the action two times.</param>
    /// <param name="action">The action, with the JSON of <c>TableAction</c> in engine.ts.</param>
    /// <returns><c>{ accepted: seq }</c> or <c>{ rejected: code }</c>.</returns>
    public async Task<JsonObject> Act(string clientActionId, JsonElement action)
    {
        var access = Access();
        if (Context.Items[BucketItem] is ActionBucket bucket && !bucket.TryTake())
        {
            return Rejected(ActionRejections.RateLimited);
        }

        TableAction? parsed;
        try
        {
            parsed = action.Deserialize<TableAction>(TableJson.Options);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            parsed = null;
        }

        if (parsed is null)
        {
            return Rejected(ActionRejections.InvalidAction);
        }

        var result = await CallAsync(() => games.ActAsync(access.GameId, access.Token, clientActionId, parsed));
        if (result.Update is { } update)
        {
            await broadcaster.SendPatchesAsync(update);
        }

        return result.Rejected is { } code ? Rejected(code) : new JsonObject { ["accepted"] = result.Seq!.Value };
    }

    /// <summary>
    /// Starts the game. Only the host can do it, with 2 players or more. Each seat then gets the new lobby and a patch with the new sequence number.
    /// </summary>
    /// <returns>The sequence number of the start.</returns>
    public async Task<long> Start()
    {
        var access = Access();
        var update = await CallAsync(() => games.StartAsync(access.GameId, access.Token));
        await broadcaster.SendLobbyAsync(update.Game);
        await broadcaster.SendPatchesAsync(update);
        return update.Seq;
    }

    /// <summary>
    /// Removes a player. Only the host can do it, before the start. The player gets <c>Closed</c> with the reason <c>kicked</c>.
    /// </summary>
    /// <param name="seat">The seat to free: <c>"1"</c> to <c>"3"</c>.</param>
    /// <returns>A task that completes when the seats got the new lobby.</returns>
    public async Task Kick(string seat)
    {
        if (!int.TryParse(seat, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            throw new HubException("validation_failed: The seat must be a seat number, for example \"1\".");
        }

        var access = Access();
        var (stored, _) = await CallAsync(() => games.ReadForSeatAsync(access.GameId, access.Token));
        var kicked = stored.State.FindSeat(number);
        var update = await CallAsync(() => games.KickAsync(access.GameId, access.Token, number));
        if (kicked is not null)
        {
            await broadcaster.SendClosedAsync(access.GameId, kicked, "kicked");
        }

        await broadcaster.SendChangeAsync(update);
    }

    /// <summary>
    /// Makes a new invite secret. The old invite link stops. Only the host can do it, before the start.
    /// </summary>
    /// <returns><c>{ inviteSecret, inviteExpiresAt }</c>.</returns>
    public async Task<JsonObject> NewInvite()
    {
        var access = Access();
        var (update, secret) = await CallAsync(() => games.NewInviteAsync(access.GameId, access.Token));
        await broadcaster.SendChangeAsync(update);
        return new JsonObject { ["inviteSecret"] = secret, ["inviteExpiresAt"] = update.Game.InviteExpiresAt };
    }

    /// <summary>
    /// The caller leaves the game. In the lobby, the seat becomes free. When the host leaves the lobby, the game ends for all the players.
    /// After the start, the seat stays: the player can come back with the same game token.
    /// </summary>
    /// <returns>A task that completes when the other seats got the change.</returns>
    public async Task Leave()
    {
        var access = Access();
        var (stored, _) = await CallAsync(() => games.ReadForSeatAsync(access.GameId, access.Token));
        var (update, closed) = await CallAsync(() => games.LeaveAsync(access.GameId, access.Token));
        if (closed)
        {
            await broadcaster.SendClosedAsync(stored.State, "ended");
        }
        else if (update is not null)
        {
            await broadcaster.SendChangeAsync(update);
            var seat = stored.State.FindSeat(access.Seat);
            if (seat is not null)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, TableBroadcaster.SeatGroup(access.GameId, access.Seat, seat.TokenId));
            }
        }
    }

    /// <summary>
    /// Closes the game for all the players and deletes its data. Only the host can do it.
    /// </summary>
    /// <returns>A task that completes when the seats got the message <c>Closed</c>.</returns>
    public async Task Close()
    {
        var access = Access();
        var game = await CallAsync(() => games.CloseAsync(access.GameId, access.Token));
        await broadcaster.SendClosedAsync(game, "ended");
    }

    private static JsonObject Rejected(string code) => new() { ["rejected"] = code };

    private static string? ClosedReason(GameException exception) => exception.Code switch
    {
        PlaytestErrorCodes.TokenInvalid => "kicked",
        ErrorCodes.NotFound => "ended",
        _ => null,
    };

    private async Task<HubSnapshot> SnapshotAsync()
    {
        var access = Access();
        var (stored, seat) = await CallAsync(() => games.ReadForSeatAsync(access.GameId, access.Token));
        return TableBroadcaster.Snapshot(stored.State, stored.Seq, seat);
    }

    /// <summary>
    /// Calls the game service. An error becomes a hub error with the code and the English message, so the client gets the reason.
    /// A token that is not valid any more, or a game that ended, also sends <c>Closed</c> to the caller and closes the connection.
    /// </summary>
    private async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (GameException exception)
        {
            if (ClosedReason(exception) is { } reason)
            {
                await CloseAsync(reason);
            }

            throw new HubException($"{exception.Code}: {exception.Message}");
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            logger.StoreUnavailable(exception);
            throw new HubException("service_unavailable: The store of the online games is not available. Try again later.");
        }
    }

    private async Task CloseAsync(string reason)
    {
        await Clients.Caller.SendAsync("Closed", new { reason });
        Context.Abort();
    }

    private SeatAccess Access() =>
        Context.Items[AccessItem] as SeatAccess ?? throw new HubException("game_token_invalid: The connection has no game token. Connect again.");

    private sealed record SeatAccess(Guid GameId, int Seat, string Token);
}

/// <summary>
/// A token bucket for the actions of one connection.
/// </summary>
/// <param name="perSecond">The number of tokens that the bucket gets each second.</param>
/// <param name="burst">The maximum number of tokens.</param>
internal sealed class ActionBucket(double perSecond, double burst)
{
    private readonly object gate = new();
    private readonly double capacity = burst;
    private double tokens = burst;
    private long last = Environment.TickCount64;

    /// <summary>Takes one token.</summary>
    /// <returns><see langword="true"/> when the action can continue.</returns>
    public bool TryTake()
    {
        lock (gate)
        {
            var now = Environment.TickCount64;
            tokens = Math.Min(capacity, tokens + ((now - last) / 1000.0 * perSecond));
            last = now;
            if (tokens < 1)
            {
                return false;
            }

            tokens--;
            return true;
        }
    }
}
