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

The actions on the table and the start of the game use the table hub.
A change of the lobby through these endpoints also goes to the connected players through the hub.

## Table hub

The table hub is at `/api/v1/playtest/hub`. It uses SignalR with the JSON hub protocol, and WebSockets only.
The client connects with no negotiation (`SkipNegotiation`), so it can connect to any API replica with no sticky session.
The client sends the game token in the query parameter `access_token`.
The web client is `web/src/playtest/online/hubClient.ts`.

Methods that the client calls:

| Method | Arguments | Result |
| --- | --- | --- |
| `Join` | `lastSeq` or `null` | The snapshot `{ seq, view, hash }`. Before the start, `view` is `null` and `hash` is empty. |
| `Resync` | `reason`: `hash`, `gap`, `patch` or `view` | The snapshot. See [Metrics](#metrics) for the reasons. |
| `Act` | `clientActionId`, `action` (the `TableAction` of engine.ts) | `{ accepted: seq }` or `{ rejected: code }` |
| `Start` | None | The sequence number of the start. Only the host, with 2 players or more. |
| `Kick` | `seat`, for example `"1"` | None. Only the host, before the start. |
| `NewInvite` | None | `{ inviteSecret, inviteExpiresAt }`. Only the host, before the start. |
| `Leave` | None | None. In the lobby, the seat becomes free. When the host leaves the lobby, the game ends. After the start, the seat stays. |
| `Close` | None | None. Only the host. |

Messages that the server sends:

| Message | Content |
| --- | --- |
| `Lobby` | The lobby of the seat. It has the same shape as the response of `GET /playtest/games/{id}`. The hub sends it after the connection and after each change of the lobby. |
| `Patch` | `{ seq, actorSeat, log, changes, hash }`: the changes of the view of the seat and the hash of the new view. A change of the lobby and the start give a patch with no change, so the sequence numbers have no gap. |
| `Closed` | `{ reason }`: `ended` or `kicked`. The client does not connect again. |

A method that fails gives a completion with an error. The text of the error contains `HubException: <code>: <message>`, for example `HubException: forbidden: Only the host of the game can do this operation.`

Rules:

- The order of the changes is the sequence number of the game store, not the order of the messages. The client keeps a reorder buffer.
- Each change of a game is a compare-and-set in Redis. On one API replica, only one change of a game runs at a time. The other changes wait.
  After a conflict with another replica, the API waits from 1 ms to 5 ms. Then it reads the game again and tries again, 16 times at most.
- The start makes the table with the decks of the seats and a random seed. The seed never leaves the server. The commander damage applies when the format is `commander` or when a deck has a command zone.
- Each player changes only their own objects. Only the active player can end the turn. A draw has 20 cards at most. A token name has 64 characters at most.
- A retry of `Act` with the same `clientActionId` gives the sequence number of the first action and applies nothing.
  This is also true when the action is not valid now, for example a retry of `nextTurn`.
- One connection sends 10 actions per second on average, and 30 in a burst. An action over the limit gets `{ rejected: "rate_limited" }`.
  The settings `Playtest:ActionsPerSecond` and `Playtest:ActionBurst` change these limits.
- The log text of an action never gives the name of a card in a hidden zone.
- Each connection is in the group of its seat and of its game token. A kick or a new join gives a new token, so the old connection gets no more messages.
- The Redis backplane sends the group messages to the connections on all the API replicas. Its channels start with `reliquary:playtest:hub`.
  When Redis is not available, the backplane loses the messages. The client then finds a gap in the sequence numbers and asks for a full view.

Reason codes of `{ rejected: code }`:

| Code | Meaning |
| --- | --- |
| `invalid_action` | The action is not valid: unknown type, missing field, or a value out of range. |
| `not_your_object` | The action changes the objects of another player. |
| `not_your_turn` | Only the active player can end the turn. |
| `unknown_card` | The card is not a card of the player. |
| `not_started` | The game did not start. |
| `game_closed` | The game ended. |
| `rate_limited` | The connection sent too many actions in a short time. |

### Reverse proxy

The table hub needs a WebSocket location in the reverse proxy. `web/nginx.conf.template` has it:
`proxy_http_version 1.1`, the headers `Upgrade` and `Connection`, and a read timeout of 1 hour.
The access log of this location does not contain the query string, so it never contains a game token.
A reverse proxy in front of the web container must also let WebSockets through.

## Metrics

The API gives these metrics on the meter `Reliquary`.
The guide [Observability](../development/observability.md) tells how to read them.

| Metric | Type | Unit | Meaning |
| --- | --- | --- | --- |
| `reliquary.playtest.action.duration` | Histogram | `s` | The time from the receipt of `Act` to the send of the patches to all the seats. The attribute `result` is `accepted`, `duplicate` or `rejected`. |
| `reliquary.playtest.desyncs` | Counter | `{desync}` | The calls of `Resync` because the view of a client was not correct. The attribute `reason` gives the cause. |
| `reliquary.playtest.games.active` | Gauge | `{game}` | The open games: in the lobby or started, with a change in the last hour. |
| `reliquary.playtest.patches` | Counter | `{patch}` | The patches that the API sent to the seats. |

The client gives a reason to each call of `Resync`:

| Reason | Cause | Counted |
| --- | --- | --- |
| `hash` | The hash of the view after a patch is not the hash of the patch. | Yes |
| `gap` | A gap in the sequence numbers stayed open for 1 second. | Yes |
| `patch` | The client cannot apply a patch. | Yes |
| `view` | The client has no view, for example after the start. This is not a desynchronization. | No |

The API records another reason, or no reason, as `other`.
On a healthy instance, `reliquary.playtest.desyncs` stays at 0.

All the API replicas share the games in Redis.
Thus each replica gives the number of all the open games in `reliquary.playtest.games.active`.
Use the maximum of the replicas, not the sum.
The API counts the games again every 15 seconds at most.

## Gate test

The gate of the milestone M3 is the test `OnlineGameWithFourPlayersHasNoDesync` in `tests/Reliquary.Playtest.Tests`.
It runs in CI for each pull request.

The test starts two API hosts on one Redis: two replicas with the real backplane.
Two players connect to each host with the JSON hub protocol over a raw WebSocket, as the web app.
One hour of a 4-player game has about 2,400 actions, so the test sends 4,000 actions.
Each player makes random actions from its own view, so each action is legal for this player.
The actions include all the types of `TableAction`.

The test adds these faults:

- Every 300 actions, a player sends an action and loses its connection before the answer.
  The player connects to the other host, gets a snapshot, and sends the action again with the same ID.
- 20% of the rounds send the actions of two or four players at the same time. These actions cause conflicts of the compare-and-set.
- 3% of the actions go a second time with the same ID.

The test checks these conditions:

- Each player applies each patch with `PlayerViews.Apply`, the C# side of the web code. The hash of the new view is the hash of the patch.
- After each change, the view of each player is equal to the view of the server, field by field.
  The view of the server comes from the state in Redis at the start and the actions of the action stream.
- The action stream has each action one time. The replay of the stream gives the state in Redis.
- No message to a player contains the card ID of a hidden card of another player.
  A hidden card is a card in a hand or a library, or a face-down card.
  No message contains the Oracle ID of a hidden card, unless the player sees a copy of the same card.
- No player calls `Resync`, and the metric `reliquary.playtest.desyncs` does not change.
- The 95th percentile of the time from the send of `Act` to the receipt of the patch of the player is less than 200 ms.

The output of the test gives the latency, the server time and the duration.

The test `FourPlayersThatActWithNoPauseGetNoRefusal` sends 400 life changes from 12 connections (3 for each player, on the two hosts) with no pause.
Each action must get a sequence number, not `service_unavailable`.

The [end-to-end test](../development/end-to-end-test.md) adds a check in real browsers.
It runs on a schedule: 4 players with accounts and guests, 2 API replicas behind Nginx, 10 minutes each night and 60 minutes each week.

## Error codes

| Code | Status | Meaning |
| --- | --- | --- |
| `game_invite_invalid` | 404 | The game does not exist, the game ended, the secret is not correct, or the invite link expired. |
| `game_token_invalid` | 401 | The game token is missing, is not valid, expired, or a newer token or a kick replaced it. |
| `game_full` | 409 | All the seats of the game have a player. |
| `game_started` | 409 | The game started. New players cannot join it, and the lobby cannot change. |
| `game_open_limit` | 409 | The account has the maximum number of open games. |
| `game_not_enough_players` | 409 | The game has not enough players to start. The hub gives this code. |
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
| `reliquary:playtest:games:active` | The open games, with the time of their last change. Only the gauge `reliquary.playtest.games.active` uses it. |
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
| `Playtest:ActionsPerSecond` | `10` | The number of actions that one connection to the table hub sends in one second, on average. |
| `Playtest:ActionBurst` | `30` | The number of actions that one connection to the table hub sends in a burst. |
| `Playtest:Store:IdleLifetime` | `01:00:00` | The time with no change after which Redis deletes a game. |
| `Playtest:Store:ActionStreamLength` | `10000` | The approximate maximum number of entries of the action stream of a game. |
| `Playtest:Store:ClientActionIdsPerSeat` | `64` | The number of client action IDs that the store keeps for each seat. |
