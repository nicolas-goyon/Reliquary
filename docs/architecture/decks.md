# Decks

This page tells how the Decks component stores the decks and checks them.
Terms follow the [glossary](../glossary.md).
[ADR 0026](../decisions/0026-deck-model.md) gives the reasons for the deck model.

## Endpoints

All the paths start with `/api/v1`.
The OpenAPI document gives the bodies and the responses.

| Endpoint | Access | Effect |
| --- | --- | --- |
| `GET /decks` | Owner | Lists the decks of the user, with filters. See [deck list](#deck-list). |
| `GET /decks/tags` | Owner | Lists the deck tags of the user, with the number of decks of each tag. |
| `POST /decks/group` | Owner | Applies one action to several decks. See [group actions](#group-actions). |
| `POST /decks/export` | Owner | Gives several decks in one ZIP file, with one file for each deck. |
| `POST /decks` | User | Creates a deck, with its first lines. |
| `GET /decks/{id}` | Owner, or anyone for an unlisted or public deck | Gets a deck with its lines, cards, printings and tags. |
| `PATCH /decks/{id}` | Owner | Changes the name, the format, the visibility, the folder, the description, the pin, the archive or the deck tags. |
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
| `GET /decks/{id}/analysis` | Like the deck | Analyzes the deck. See [Deck analysis](deck-analysis.md). |
| `GET /tags` | User | Lists the global tags of the user. |
| `PATCH /tags/{id}` | Owner | Renames a tag, or merges it with a tag of the same name. |
| `DELETE /tags/{id}` | Owner | Deletes a tag. |
| `GET /deck-folders` | User | Lists all the folders of the user, with the parent of each folder. |
| `POST /deck-folders` | User | Creates a folder, at the top or in another folder. |
| `PATCH /deck-folders/{id}` | Owner | Renames a folder or moves it into another folder. |
| `DELETE /deck-folders/{id}` | Owner | Deletes a folder and its subfolders. Their decks stay, with no folder. |

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

## Deck list

The query of `GET /decks` accepts these parameters:

| Parameter | Values | Default |
| --- | --- | --- |
| `folderId` | The ID of a folder, or `none` for the decks with no folder | All the decks |
| `format` | A format, for example `modern` | All the formats |
| `colors` | The exact color identity: the letters `W`, `U`, `B`, `R` and `G` in any order, or `C` for colorless | All the colors |
| `tag` | A deck tag. The comparison ignores the case. | All the decks |
| `archived` | `exclude`, `include` or `only` | `exclude` |
| `sort` | `updated`, `created`, `name` or `format` | `updated` |

The pinned decks always come first. Then the sort applies.

The color identity of a deck is the color identity of its commanders.
A deck with no commander uses the cards of its main deck and its companions.
Each change of the lines calculates the color identity again.

A deck tag is a label on a deck. A deck has 20 deck tags or fewer.
A folder can contain other folders, with 8 levels or fewer.

## Group actions

The body of `POST /decks/group` gives the deck IDs, 500 or fewer, and one action:

| Action | Effect |
| --- | --- |
| `move` | Moves the decks to the folder `folderId`. With no `folderId`, the decks leave their folder. |
| `tag`, `untag` | Adds or removes the deck tag `tag`. |
| `pin`, `unpin` | Puts the decks at the top of the list, or not. |
| `archive`, `unarchive` | Archives the decks, or not. The deck list does not show the archived decks by default. |
| `delete` | Deletes the decks. |

When a deck is not a deck of the user, the API changes no deck and gives the status 404.

## Public decks

[ADR 0029](../decisions/0029-public-deck-read-model.md) describes the read model of the public decks.

| Endpoint | Access | Effect |
| --- | --- | --- |
| `GET /public-decks` | Anyone | Searches the public decks. See the parameters below. |
| `GET /public-decks/favorites` | User | Lists the favorite decks of the user. |
| `GET /public-decks/following` | User | Lists the public decks of the authors that the user follows. |
| `GET /public-decks/{id}/social` | Anyone | Tells whether the deck is public and a favorite of the user, and gives its counts and its author. |
| `PUT /public-decks/{id}/favorite` | User | Adds a public deck to the favorites. |
| `DELETE /public-decks/{id}/favorite` | User | Removes a deck from the favorites. |
| `GET /authors/{id}` | Anyone | Gets the profile of an author. |
| `GET /authors/following` | User | Lists the authors that the user follows. |
| `PUT /authors/{id}/follow` | User | Follows an author. A user cannot follow themselves. |
| `DELETE /authors/{id}/follow` | User | Stops to follow an author. |

The query of `GET /public-decks` accepts these parameters:

| Parameter | Values |
| --- | --- |
| `q` | A part of the deck name |
| `format` | A format, for example `commander` |
| `commander` | The Oracle ID or the name of a commander, in any language |
| `card` | The Oracle ID or the name of a card of the main zones, in any language |
| `colors` | The exact color identity, like the deck list |
| `author` | The account ID of the author |
| `sort` | `recent` (default) or `popular`: the number of copies by other users plus the number of favorites |

A visitor with no account opens an unlisted or public deck with its link: `GET /decks/{id}`.
