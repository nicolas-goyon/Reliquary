# Online games

This file gives the endpoints, the data and the limits of the online games.
[ADR 0047](../decisions/0047-trusted-server-for-online-games.md) gives the decision: the API keeps the full state of each online game.
Terms follow the [glossary](../glossary.md).

## Seats and game tokens

- An online game has 2 to 4 seats. The seats have the numbers 0 to 3. The host has the seat 0.
- The ID of the player of a seat is the number of the seat as a text, for example `"1"`. The table, the views and the API use this ID.
- Each seat has one game token. A game token is a JSON Web Token, signed with the key of the access tokens (`Auth:SigningKey`).
  Its audience is `reliquary-game`, so the API does not accept it as an access token.
- The game token has the claims `game`, `seat` and `jti`. The seat keeps the `jti` of its only valid token.
  A new join to the seat or a kick makes the old token not valid.
- A game token expires 8 hours after the join, or at the end of the game.
- Send the game token in the header `X-Game-Token` to the game endpoints.
  The hub reads it from the query parameter `access_token`, because a browser cannot set a header on a WebSocket.

## Invite links

- The host gets an invite link when the game starts in the lobby: `/play/{gameId}#{inviteSecret}`.
- The secret has 128 random bits. It is in the fragment of the link, so the servers do not write it in their logs. The client sends it in the body of the join request.
- The API keeps only the SHA-256 hash of the secret. It cannot give the secret again. The host makes a new link with `POST /playtest/games/{id}/invite`, and the old link stops.
- An invite link is valid for 24 hours, until the start of the game, and until the end of the game.
- A wrong secret, an unknown game and an expired link give the same error, `game_invite_invalid`.

## Endpoints

All the paths start with `/api/v1`.

| Endpoint | Credential | Result |
| --- | --- | --- |
| `POST /playtest/games` | Access token, API key, or delegation token with `decks:read` | Creates a game. The host gets the seat 0, a game token and an invite secret. |
| `POST /playtest/games/{id}/join` | None (guest), or the credential of an account | Gives a seat and a game token. An account that has a seat gets its seat again with a new token. |
| `GET /playtest/games/{id}` | Game token | Gets the lobby. |
| `POST /playtest/games/{id}/invite` | Game token of the host | Makes a new invite secret. |
| `DELETE /playtest/games/{id}/seats/{seat}` | Game token of the host | Removes a player. The token of the player stops. |
| `DELETE /playtest/games/{id}` | Game token of the host | Closes the game and deletes all its data. |

The request of a new game and of a join gives a deck in one of two forms:

- `deckId`: a deck that the caller can see. An account can use its own decks and the unlisted and public decks of the other users. A guest can use only unlisted and public decks.
- `deckText`: a deck text in the format of the deck import. See [Deck text format](../development/deck-text-format.md). The text has 10 KB and 250 lines at most.

The game uses the main zone as the library, and the commander zone and the companion zone as the command zone.
The other zones of the deck are not in the game.
The API gives each copy of a card a random ID, so an ID does not tell the deck line of a card.

The lobby gives the nickname, the number of cards and the "ready" flag of each seat.
It gives the name of the deck only to the seat of the deck, and it never gives the cards of a deck.
A player gets a seat only with a deck, so each occupied seat is ready.

The actions on the table and the start of the game use the hub. Issue [#233](https://github.com/nicolas-goyon/reliquary/issues/233) adds the hub.

## Error codes

| Code | Status | Meaning |
| --- | --- | --- |
| `game_invite_invalid` | 404 | The game does not exist, the game ended, the secret is not correct, or the invite link expired. |
| `game_token_invalid` | 401 | The game token is missing, is not valid, expired, or a newer token or a kick replaced it. |
| `game_full` | 409 | All the seats of the game have a player. |
| `game_started` | 409 | The game started. New players cannot join it, and the lobby cannot change. |
| `game_open_limit` | 409 | The account has the maximum number of open games. |
| `deck_text_invalid` | 400 | The deck text has lines that the API cannot use. The member `lineErrors` gives each line. |
| `rate_limited` | 429 | The account created the maximum number of games today, or the IP address sent too many join requests. |
| `service_unavailable` | 503 | Redis is not available. |

## Data in Redis

The online games are only in Redis. A restart of Redis ends the games in progress.

| Key | Content |
| --- | --- |
| `reliquary:playtest:game:{id}:state` | The game as JSON: seats, nicknames, accounts, decks, the hash of the invite secret, the times, and the table after the start |
| `reliquary:playtest:game:{id}:seq` | The sequence number of the last change |
| `reliquary:playtest:game:{id}:log` | The action stream, about 10,000 entries at most |
| `reliquary:playtest:game:{id}:actions` | The last client action IDs of each seat |
| `reliquary:playtest:limits:open:{account}` | The open games of a host |
| `reliquary:playtest:limits:created:{day}:{account}` | The number of games of a host on one day (UTC). It expires after 2 days. |
| `reliquary:playtest:limits:join:{minute}:{address}` | The number of join requests of an IP address in one minute. It expires after 2 minutes. |

Each change of a game sets the lifetime of its keys again to 1 hour.
Thus a game with no change for 1 hour goes out of Redis.
A game also closes 8 hours after its creation: the API compares the current time with the end time in the state.

### Personal data of the guests

A guest has no account. The API keeps these data of a guest in the state of the game:

- The nickname.
- The deck: the card names and the image addresses, from the deck text or from the public deck.

These data are only in Redis. They go out with the game:

- After 1 hour with no change.
- At the end of the game, 8 hours after its creation at most.
- When the host closes the game.

The API does not write them in PostgreSQL or in the logs.
The IP address of a guest is in a join counter for 2 minutes.
Issue [#238](https://github.com/nicolas-goyon/reliquary/issues/238) adds these data to the personal data page.

## Settings

All the API replicas must have the same values.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Playtest:GamesPerAccountPerDay` | `20` | The maximum number of games that one account creates in one day (UTC). |
| `Playtest:OpenGamesPerAccount` | `3` | The maximum number of open games of one host. |
| `Playtest:JoinsPerIpPerMinute` | `20` | The maximum number of join requests from one IP address in one minute. |
| `Playtest:MinSeats` | `2` | The minimum number of seats of a game. |
| `Playtest:MaxSeats` | `4` | The maximum number of seats of a game. |
| `Playtest:MaxDeckTextBytes` | `10240` | The maximum size of a deck text, in UTF-8 bytes. |
| `Playtest:MaxDeckTextLines` | `250` | The maximum number of lines of a deck text. |
| `Playtest:MaxDeckCards` | `250` | The maximum number of cards of a deck in a game. |
| `Playtest:GameLifetime` | `08:00:00` | The hard limit of the lifetime of a game, from its creation. |
| `Playtest:InviteLifetime` | `1.00:00:00` | The lifetime of an invite link. |
| `Playtest:TokenLifetime` | `08:00:00` | The lifetime of a game token. |
| `Playtest:Store:IdleLifetime` | `01:00:00` | The time with no change after which Redis deletes a game. |
| `Playtest:Store:ActionStreamLength` | `10000` | The approximate maximum number of entries of the action stream of a game. |
| `Playtest:Store:ClientActionIdsPerSeat` | `64` | The number of client action IDs that the store keeps for each seat. |
