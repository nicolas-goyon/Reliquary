# Deck testing

This file describes the deck testing modes of Reliquary and the table engine that they share.
Terms follow the [glossary](../glossary.md).

## Test modes

Four test modes share the same table engine.
The table lists the modes from the simplest mode to the most social mode.
The table engine has zones, draw, shuffle, tokens, counters and flip.
It has no automated game rules.

| Mode | Use | Specific features |
| --- | --- | --- |
| Goldfish | Test hands and first turns, with no precise goal | London mulligan, draw, hand statistics |
| Local multiplayer | Test a multiplayer deck alone | 3 to 4 decks. The user opens one window for each player, or one window where the user plays each player in turn. |
| Online game (simplified) | PvP games against other people | Reliquary links the boards of the players. The players resolve their own effects and manage the stack themselves. The other players cannot see the hand and the library of a player. |
| Shared screen | Screen sharing on Discord, two screens or a stream | A table window that masks hidden information, and a private player window with the hand of the player |

- Players on camera can also use the shared screen mode.
  One player films their real playmat, and the other player shares the table window of Reliquary.
- Users can combine the table window with the local multiplayer mode and with the online game mode.
- Reliquary has no rules engine.
  The players enter the life points, the counters and the commander damage.
- Each online game uses a real-time game session with SignalR.
  The [technology stack](../architecture/tech-stack.md) gives the requirements.

## Local multiplayer

One person tests a deck against 2 or 3 other decks on one computer.
The page of the goldfish mode has a link to this mode.

1. The user selects the number of players: 3 or 4.
2. The user selects the deck of each other player in their deck list. The same deck can be in the game more than one time.
3. The user selects the mode: one window, or one window for each player.

A user who is not signed in has no deck list.
That user can play copies of the deck that they test.

### One window

- The window shows the board of one player. At the start of each turn, it shows the board of the active player.
- The user can select a player in the list of the players to see the board of that player.
- The hand of the player stays hidden. The hand zone shows only the number of cards and a button "Show the hand".
- The hand stays visible until the user hides it or until the turn changes.
- The button "Next player" gives the turn to the next player. The table engine untaps the permanents of that player, and the player draws a card.

### One window for each player

- The window of the game keeps the game. It shows the life points and the number of cards of each player, and a link to the player window of each player.
- Each player window shows the board and the hand of one player, and the controls of that player.
- A player window can act only for its own player. Each player window can give the turn to the next player.
- The windows must be in the same browser. [ADR 0045](../decisions/0045-window-sync-with-broadcastchannel.md) gives the sync design.
- When the user closes the game window, the game stops. The player windows show a message.

### Counters

The players enter the values by hand.
Reliquary applies no game rules to them.

- Life points
- Poison, energy and experience counters
- Commander damage from each other player, only in a game that uses it: the Commander format, or a deck with a commander
- Counters on a card on the battlefield

## Shared screen

The goldfish page and the local multiplayer page are game windows.
Each game window has a panel "Shared screen" with a link to the table window.
In the local multiplayer mode with one window for each player, the panel also has a link to the player window of each player.

- The table window shows the board of each player: the battlefield, the command zone, the graveyard and the exile.
- The table window shows the life points, the counters and the commander damage of each player.
- The table window shows only the number of cards of each hand and of each library.
- The table window shows the back of each face-down card.
- The table window has no game controls. The user plays in the game window or in the player windows.
- The player window shows the hand of its player. It shows the back of the face-down cards of the other players.

The game window sends to each window only the public projection of the game for that window.
The function `projectPublicState` in `web/src/playtest/projection.ts` makes the projection.
It removes the identity of each hidden card: the name, the Oracle ID, the image and the ID of the copy.
It also removes the seed of the random number generator, because the seed gives the order of the libraries.
Online games can use the same function. [ADR 0045](../decisions/0045-window-sync-with-broadcastchannel.md) gives the design.

## Milestones

The project delivers the features of this file in this [milestone](../roadmap.md).

| Milestone | Features |
| --- | --- |
| M3 (Deck testing) | All the features of this file |
