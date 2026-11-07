# 0040. The table engine runs in the browser

- Status: Accepted
- Date: 2026-11-04
- Deciders: @nicolas-goyon

## Context

Issue [#51](https://github.com/nicolas-goyon/reliquary/issues/51) asks for one table engine for goldfish, local multiplayer, the shared screen and online games.
Goldfish and local multiplayer need no server.
In an online game, the other players must not get the hand and the library of a player.

## Decision

- The table engine is a pure TypeScript function in `web/src/playtest/engine.ts`. It takes a state and an action, and it gives a new state.
- The engine has a seeded random number generator, so a test can repeat a game.
- The engine has no game rules.
- Goldfish and local multiplayer send no game data to the API. They only read the deck.

## Consequences

- The deck testing modes that use one computer also work with a slow API.
- The API cannot check a game.
- Each online player keeps their private zones in their own browser. The online game design must send only public actions. The issue of online games records this design.
