# Decks

This page tells how the Decks component stores the decks and checks them.
Terms follow the [glossary](../glossary.md).
[ADR 0026](../decisions/0026-deck-model.md) gives the reasons for the deck model.

## Endpoints

All the paths start with `/api/v1`.
The OpenAPI document gives the bodies and the responses.

| Endpoint | Access | Effect |
| --- | --- | --- |
| `GET /decks` | Owner | Lists the decks of the user, the most recent change first. |
| `POST /decks` | User | Creates a deck, with its first lines. |
| `GET /decks/{id}` | Owner, or anyone for an unlisted or public deck | Gets a deck with its lines, cards, printings and tags. |
| `PATCH /decks/{id}` | Owner | Changes the name, the format, the visibility, the folder or the description. |
| `DELETE /decks/{id}` | Owner | Deletes a deck. |
| `POST /decks/{id}/lines` | Owner | Adds cards. |
| `PUT /decks/{id}/lines` | Owner | Replaces all the lines. |
| `PATCH /decks/{id}/lines/{lineId}` | Owner | Changes a line. A quantity of 0 removes the line. |
| `DELETE /decks/{id}/lines/{lineId}` | Owner | Removes a line. |
| `GET /decks/{id}/text` | Like the deck | Gets the lines in the [deck text format](../development/deck-text-format.md). |
| `PUT /decks/{id}/text` | Owner | Replaces the lines from a text. |
| `POST /decks/import` | User | Makes a deck from a file of another tool. See [deck import and export](../development/deck-import-export.md). |
| `GET /decks/{id}/export` | Like the deck | Gives the deck as a file of another tool. |
| `POST /decks/{id}/copy` | User who can see the deck | Copies the deck into a new deck of the user. |
| `GET /decks/{id}/legality` | Like the deck | Checks the legality of the deck in its format. |
| `GET /tags` | User | Lists the global tags of the user. |
| `PATCH /tags/{id}` | Owner | Renames a tag, or merges it with a tag of the same name. |
| `DELETE /tags/{id}` | Owner | Deletes a tag. |

## Copy of a deck

The copy keeps the lines, the zones, the printings, the notes, the tags and the categories.
The copy of the deck of another user keeps a link to the original deck in `sourceDeckId`.
Its tags become local tags of the copy, because the global tags of another user are not the tags of the user.

## Legality

The legality check reads the commander, companion and main zones.
The considering and tokens zones are outside the deck.

| Format | Deck size | Copies | Other rules |
| --- | --- | --- | --- |
| `commander`, `duel`, `predh`, `paupercommander`, `brawl` | 100, with the commanders | 1 | Commanders and color identity |
| `standardbrawl` | 60, with the commanders | 1 | Commanders and color identity |
| `oathbreaker` | 60 | 1 | — |
| `freeform` | No limit | No limit | No check of the card legalities |
| Other formats | 60 or more | 4 | 15 cards or fewer in the sideboard |

Basic lands, and cards that say "A deck can have any number of cards named", have no copy limit.
Cards that say "A deck can have up to N cards named" have the limit N.

The response gives each problem with a stable code:

| Code | Problem |
| --- | --- |
| `deck_size` | The deck does not have the permitted number of cards. `count` and `limit` give the numbers. |
| `too_many_copies` | The deck has too many copies of a card. |
| `sideboard_size` | The sideboard has too many cards. |
| `card_banned` | The format bans the card. |
| `card_not_legal` | The card is not legal in the format. |
| `card_restricted` | The deck has more than one copy of a restricted card. |
| `commander_missing` | The deck has no commander. |
| `commander_not_allowed` | The card cannot be a commander. |
| `too_many_commanders` | The deck has more than two commanders. |
| `commanders_cannot_pair` | The two commanders have no Partner, Friends forever, Background or Doctor pair. |
| `outside_color_identity` | The card is outside the color identity of the commanders. |
| `companion_not_allowed` | The card in the companion zone has no companion ability. |

The check does not read the deck building conditions of the companions.
