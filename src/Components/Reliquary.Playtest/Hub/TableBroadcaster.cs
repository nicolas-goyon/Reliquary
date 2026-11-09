using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.SignalR;
using Reliquary.Playtest.Api;
using Reliquary.Playtest.Engine;
using Reliquary.Playtest.Sessions;
using Reliquary.Playtest.Views;

namespace Reliquary.Playtest.Hub;

/// <summary>
/// Sends the lobby, the patches and the end of a game to the seats. Each seat gets only its own view.
/// </summary>
/// <remarks>
/// Each connection is in the group of its seat and of its game token: <c>game:{id}:seat:{n}:{jti}</c>.
/// A kick or a new join gives a new token, so the old connection gets no more message.
/// With the Redis backplane, a group send reaches the connections on all the API replicas.
/// </remarks>
internal sealed class TableBroadcaster(IHubContext<TableHub> hub)
{
    private static readonly Meter Meter = new("Reliquary.Playtest");

    private static readonly Counter<long> Patches = Meter.CreateCounter<long>("reliquary.playtest.patches", description: "The number of patches that the API sent to the seats of the online games.");

    /// <summary>
    /// Gets the group of the connections of a seat with one game token.
    /// </summary>
    public static string SeatGroup(Guid gameId, int seat, string tokenId) => $"game:{gameId:N}:seat:{seat}:{tokenId}";

    /// <summary>
    /// Gives the full view of a seat. Before the start, the view is <see langword="null"/>.
    /// </summary>
    public static HubSnapshot Snapshot(OnlineGame game, long seq, int seat)
    {
        if (game.Table is null)
        {
            return new HubSnapshot(seq, null, string.Empty);
        }

        var view = PlayerViews.View(game.Table, OnlineGame.PlayerId(seat));
        return new HubSnapshot(seq, PlayerViews.ToJson(view), PlayerViews.Hash(view));
    }

    /// <summary>
    /// Gives the lobby of a seat, with the shape of <c>GET /playtest/games/{id}</c>.
    /// </summary>
    public static GameLobbyResponse Lobby(OnlineGame game, int seat) => GameEndpoints.ToLobby(game, seat);

    /// <summary>
    /// Sends a change of the lobby to each seat: the new lobby and a patch with no change, so the sequence numbers of the clients have no gap.
    /// </summary>
    public async Task SendChangeAsync(GameUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        await SendLobbyAsync(update.Game);
        await SendPatchesAsync(update);
    }

    /// <summary>
    /// Sends the lobby to each seat.
    /// </summary>
    public async Task SendLobbyAsync(OnlineGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        foreach (var seat in game.Seats)
        {
            await hub.Clients.Group(SeatGroup(game.Id, seat.Seat, seat.TokenId)).SendAsync("Lobby", Lobby(game, seat.Seat));
        }
    }

    /// <summary>
    /// Sends the patch of a change to each seat: the changes of the view of the seat and the hash of its new view.
    /// </summary>
    /// <remarks>
    /// The start and the changes of the lobby have no table before, so the patch has no change. Its hash is the hash of the new view.
    /// A client with no view, or with another hash, asks for a full view with <c>Resync</c>.
    /// </remarks>
    public async Task SendPatchesAsync(GameUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var game = update.Game;
        foreach (var seat in game.Seats)
        {
            var playerId = OnlineGame.PlayerId(seat.Seat);
            IReadOnlyList<ViewChange> changes = [];
            var hash = string.Empty;
            if (game.Table is not null)
            {
                var after = PlayerViews.View(game.Table, playerId);
                hash = PlayerViews.Hash(after);
                if (update.Before is not null && update.Before.Players.Count == game.Table.Players.Count)
                {
                    changes = PlayerViews.Diff(PlayerViews.View(update.Before, playerId), after);
                }
            }

            await hub.Clients.Group(SeatGroup(game.Id, seat.Seat, seat.TokenId))
                .SendAsync("Patch", new HubPatch(update.Seq, OnlineGame.PlayerId(update.ActorSeat), update.Log, changes, hash));
            Patches.Add(1);
        }
    }

    /// <summary>
    /// Sends <c>Closed</c> to all the seats of a game.
    /// </summary>
    public async Task SendClosedAsync(OnlineGame game, string reason)
    {
        ArgumentNullException.ThrowIfNull(game);
        foreach (var seat in game.Seats)
        {
            await SendClosedAsync(game.Id, seat, reason);
        }
    }

    /// <summary>
    /// Sends <c>Closed</c> to one seat.
    /// </summary>
    public Task SendClosedAsync(Guid gameId, GameSeat seat, string reason)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return hub.Clients.Group(SeatGroup(gameId, seat.Seat, seat.TokenId)).SendAsync("Closed", new { reason });
    }
}
