# Deck testing

This file describes the deck testing modes of Reliquary and the table engine that they share.
Terms follow the [glossary](../glossary.md).

## Test modes

Four test modes share the same table engine.
The modes go from the simplest mode to the most social mode.
The table engine has zones, draw, shuffle, tokens, counters and flip.
It has no automated game rules.

| Mode | Use | Specific features |
| --- | --- | --- |
| Goldfish | Test hands and first turns, with no precise goal | London mulligan, draw, hand statistics |
| Local multiplayer | Test a multiplayer deck alone | 3 to 4 decks. The user opens one window for each player, or one window where the user passes the turn to themselves. |
| Online game (simplified) | PvP games against other people | Reliquary links the boards of the players. The players resolve their own effects and manage the stack themselves. The other players cannot see the hand and the library of a player. |
| Shared screen | Screen sharing on Discord, two screens or a stream | A table window that masks hidden information, and a private player window with the hand of the player |

- Players on camera can also use the shared screen mode.
  One player films their real playmat, and the other player shares the table window of Reliquary.
- Users can combine the table window with the local multiplayer mode and with the online game mode.
- Reliquary has no rules engine.
  The players enter the life points, the counters and the commander damage.
- Each online game uses a real-time game session with SignalR.
  The [technology stack](../architecture/tech-stack.md) gives the requirements.

## Milestones

The project delivers the features of this file in this [milestone](../roadmap.md).

| Milestone | Features |
| --- | --- |
| M3 (Deck testing) | All the features of this file |
