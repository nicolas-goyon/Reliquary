# 0045. The windows of a game on one computer sync with BroadcastChannel

- Status: Accepted
- Date: 2026-11-09
- Deciders: @nicolas-goyon

## Context

Local multiplayer ([#53](https://github.com/nicolas-goyon/reliquary/issues/53)) can use one window for each player.
The shared screen ([#55](https://github.com/nicolas-goyon/reliquary/issues/55)) uses a table window and a player window.
These windows show one game, so they must show the same state.
[ADR 0040](0040-table-engine-in-the-browser.md) says that the modes on one computer send no game data to the API.

The options are:

- A server connection, for example SignalR. It needs an account and the API, and the data leaves the computer.
- The `storage` event of `localStorage`. It works, but it writes the full game to the disk at each action, and the data stays after the game.
- `BroadcastChannel`. All current browsers have it. It sends messages between the windows of one origin in one browser. It needs no dependency, and the data stays in memory.

## Decision

- The windows of a game on one computer use `BroadcastChannel`. Reliquary adds no dependency for the sync.
- One window keeps the state: the game window. Only the game window applies the actions with the table engine.
- Each other window is a viewer with a viewer ID: the ID of a player, or `table` for the table window.
- A viewer sends its actions on the control channel of the game. The game window checks the format of each action, and it applies only the actions that the viewer can do. A player window acts only for its own player. The table window does no actions.
- The game window sends the state of each viewer on a channel for that viewer only. A view function gives the state that each viewer can see.
- A random game ID of 64 bits is in the name of each channel and in the path of each player window.
- The code is in `web/src/playtest/sync.ts`.

## Consequences

- The windows must be in the same browser profile, on the same computer. A window in a different browser cannot join the game.
- When the game window closes, the game stops. A reload of the game window starts a new game.
- The view function has the same contract as the view of a player in an online game ([ADR 0047](0047-trusted-server-for-online-games.md)). In an online game, the API makes the view of each player on the server.
- `BroadcastChannel` is not a security limit: each script of the origin can read the channels. The per-viewer channels prevent mistakes in what a window shows. They do not hide data from the user of the computer.
