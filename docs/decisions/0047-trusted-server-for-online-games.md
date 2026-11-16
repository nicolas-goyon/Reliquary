# 0047. Online games use a trusted server

- Status: Accepted. Amends [0040](0040-table-engine-in-the-browser.md).
- Date: 2026-11-09
- Deciders: @nicolas-goyon

## Context

Issue [#54](https://github.com/nicolas-goyon/reliquary/issues/54) asks for online games: 2 to 4 players on different computers.
In an online game, the other players must not get the hand and the library of a player.
A player must reconnect to the game through any API replica.

[ADR 0040](0040-table-engine-in-the-browser.md) puts the table engine in the browser.
Its consequence for online games is: each online player keeps their private zones in their own browser.
This option has problems:

- The other players cannot check a shuffle or a draw count.
- A player who reconnects from a different device loses their library.

Mental poker protocols remove the trusted party.
But each reveal needs all the players, a shuffle with proofs is slow, and a player who leaves stops the reveals.
These protocols also need a cryptography dependency.

## Decision

- The API keeps the full state of each online game in Redis.
- The API applies each action of an online game with a C# port of the table engine.
- Each action gets a sequence number. A compare-and-set in Redis gives the order of the actions, so all API replicas can apply actions.
- Each player gets only their view. In the view of a player, the hands and the libraries of the other players are counts.
  The deck lists of the other players are hidden.
- The random seed of a game stays on the server.
- The TypeScript engine stays the engine of goldfish, local multiplayer and the shared screen.
  Shared test vectors keep the C# engine and the TypeScript engine equal.
- Only an account can create an online game. Accounts and guests join a game with an invite link.
- The API keeps the online games only in Redis.

## Consequences

- The administrator of the instance can see all the hands. This is acceptable for casual test games on a self-hosted instance.
- The API checks each action of an online game.
- The project has two table engines. A change to the TypeScript engine needs the same change to the C# engine and new test vectors.
- When Redis restarts, the online games in progress stop.
- The consequence "Each online player keeps their private zones in their own browser" of ADR 0040 does not apply.
