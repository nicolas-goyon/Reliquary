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

## Online games

[ADR 0047](../decisions/0047-trusted-server-for-online-games.md) gives the server design.
The [online games architecture](../architecture/online-games.md) gives the endpoints and the limits.
The goldfish page has a link "Online game".

1. A user with an account selects the number of players, 2 to 4, and creates the game with the deck that they test. The user can change their name in the game. The default name is the public name of the account.
2. The host gets the invite link `/play/{game}#{secret}` in the lobby. The host can copy the link and make a new link.
3. A person with the link opens the join page.
   A user with an account gives a link of one of their decks or of a public deck.
   That user can also change their name in the game.
   A guest gives a nickname and a link of a public deck or a deck list.
4. The lobby shows the players. The host can remove a player and start the game when 2 players or more are in the lobby.
5. The online table shows the board and the hand of the player, and the boards of the other players.
   The hands and the libraries of the other players are counts.

- A game has 2 to 4 players. Only a user with an account can create a game. A guest needs no account.
- The invite link is valid for 24 hours, and only until the start of the game. A new link stops the old link.
- The server applies each action. The table shows only the view that the server sends.
- The server never sends the identity of a hidden card to another player.
  A hidden card is a card in a hand or a library, or a face-down card.
- Each player changes only their own cards and counters. Only the active player can give the turn.
- When the server refuses an action, the table shows a message.
- The table shows the public text of the last actions in the game log.
- The page keeps the seat in the browser tab. A reload of the page connects to the same seat.
- The page connects again after a network problem.
- In the lobby, a player can leave the game. The seat becomes free.
- The host can end the game for all the players, in the lobby and at the table. When the host leaves the lobby, the game ends.
- At the table, a player can leave the table. The seat stays, and the player can go back to the table.
- The online table is also a game window: the user can open a table window for the shared screen.
- When a change does not apply to the table of the page, the page gets the full table again from the server. The page shows the number of times.
- A game ends after 1 hour with no action, and 8 hours after its creation at most. When a game ends, each page shows a message.

### Limits

| Limit | Value |
| --- | --- |
| Open games of one account | 3 |
| New games of one account in one day | 20 |
| Join requests from one IP address in one minute | 20 |
| Cards in a deck of the game | 250 |
| Actions of one player in one second | 10 on average, 30 in a burst |

The administrator can change these values. The [settings](../architecture/online-games.md#settings) give the names.

### Data of the players

Reliquary keeps the data of an online game only while the game is open, and only in Redis:

- The nickname of each player, and the deck of each player.
- The account ID of each player with an account.

A guest has no account, so Reliquary keeps no data of a guest after the game.
The export of the personal data does not contain the online games.
The [online games architecture](../architecture/online-games.md#personal-data) gives the details.

### Automatic test

The [gate test](../architecture/online-games.md#gate-test) of the milestone M3 runs in each pull request.
It sends the actions of one hour of a game with 4 players to 2 API replicas.

## Milestones

The project delivers the features of this file in this [milestone](../roadmap.md).

| Milestone | Features |
| --- | --- |
| M3 (Deck testing) | All the features of this file |
