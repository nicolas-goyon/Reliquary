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
| `POST /decks/{id}/lines/group` | Owner | Applies one action to several lines in one change. See [group actions on lines](#group-actions-on-lines). |
| `DELETE /decks/{id}/lines/{lineId}` | Owner | Removes a line. |
| `GET /decks/{id}/text` | Like the deck | Gets the lines in the [deck text format](../development/deck-text-format.md). |
| `PUT /decks/{id}/text` | Owner | Replaces the lines from a text. |
| `POST /decks/import` | User | Makes a deck from a file of another tool. See [deck import and export](../development/deck-import-export.md). |
| `GET /decks/{id}/export` | Like the deck | Gives the deck as a file of another tool. |
| `POST /decks/{id}/copy` | User who can see the deck | Copies the deck into a new deck of the user. |
| `GET /decks/{id}/legality` | Like the deck | Checks the legality of the deck in its format. |
| `GET /decks/{id}/analysis` | Like the deck | Analyzes the deck. See [Deck analysis](deck-analysis.md). |
| `GET /decks/{id}/recommendations` | Owner | Recommends cards for the commanders of the deck. See [recommendations](#recommendations). |

| `GET /decks/{id}/auto-tag` | Owner | Suggests the categories of the lines. See [Auto-tag](auto-tag.md). |
| `POST /decks/{id}/auto-tag` | Owner | Sets the categories of lines in one change. See [Auto-tag](auto-tag.md). |
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

## Recommendations

`GET /decks/{id}/recommendations` gives cards for the commanders of the deck, from the public API of [recommander.cards](https://recommander.cards).
[ADR 0028](../decisions/0028-card-recommendations-source.md) gives the reasons for the source and for the limits.

- The administrator enables the recommendations with the setting `Recommendations:Enabled`.
  When the setting is off, the endpoint gives the status 404 with the code `recommendations_disabled`, and the web app does not show the panel.
- The API sends only the Oracle IDs of the first two commanders to recommander.cards, never the deck list.
- Redis keeps the list of each commander pair, so all the decks with the same commanders use one request.
- The response does not contain the cards of the deck, in all the zones, and the cards that the card data does not know.
- The query parameter `limit` gives the maximum number of cards, from 1 to 100. The default is 30.
- Each client that shows the list must show the credit of the source: the response gives its name and its address in `source`.

The field `status` of the response has one of these values:

| Status | Meaning |
| --- | --- |
| `ok` | The list comes from the source. The list can be empty. |
| `no_commander` | The deck has no commander. The list is empty. |
| `unavailable` | The source failed, stopped after the timeout or is at its rate limit. The list is empty. Try again later. |

The settings of the API host:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Recommendations:Enabled` | `false` | Enables the recommendations. The terms of recommander.cards allow personal, non-commercial use. |
| `Recommendations:BaseUrl` | `https://api.recommander.cards/public-release/` | The address of the public API. |
| `Recommendations:CacheDuration` | `1.00:00:00` | How long Redis keeps the list of a commander pair. |
| `Recommendations:FailureCacheDuration` | `00:10:00` | How long Redis keeps a failure or an unknown commander. A longer `Retry-After` header of the source wins. |
| `Recommendations:RequestsPerMinute` | `20` | The maximum number of requests to the source in one minute, for all the API replicas. |
| `Recommendations:Timeout` | `00:00:05` | The time after which a request to the source stops. |

The deck page shows the panel "Recommendations" to the owner.
The owner adds a card to the main deck or to the considering zone with one button.
The panel shows the credit "Recommendations by Recommander" with a link to the site.

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

## Moderation

[ADR 0035](../decisions/0035-moderation-of-public-content.md) describes the moderation of the public decks and of the author profiles.

| Endpoint | Access | Effect |
| --- | --- | --- |
| `POST /reports` | User | Reports a public deck or an author profile. See the body below. |
| `GET /moderation/reports` | Administrator | Lists the reports. The query parameter `status` is `open` (default), `resolved` or `dismissed`. |
| `POST /moderation/reports/{id}/resolve` | Administrator | Closes an open report as resolved. |
| `POST /moderation/reports/{id}/dismiss` | Administrator | Closes an open report as dismissed. |
| `GET /moderation/hidden` | Administrator | Lists the hidden decks and author profiles. |
| `PUT /moderation/hidden/{type}/{id}` | Administrator | Hides a deck (`deck`) or an author profile (`author`). The open reports of the item become resolved. |
| `DELETE /moderation/hidden/{type}/{id}` | Administrator | Shows a hidden item again. The query parameter `reason` gives the reason. |
| `GET /moderation/log` | Administrator | Lists the actions of the administrators, the most recent first. |

The body of `POST /reports` has these fields:

| Field | Values |
| --- | --- |
| `itemType` | `deck` or `author` |
| `itemId` | The ID of a public deck, or the account ID of an author |
| `reason` | `spam`, `offensive`, `copyright` or `other` |
| `comment` | An optional text of 500 characters or fewer |

The API refuses a report in these cases:

| Case | Status and code |
| --- | --- |
| The item is not a public deck or a visible author profile. | 404 `not_found` |
| The user owns the item. | 400 `validation_failed` |
| The user has an open report about the item. | 409 `report_exists` |
| The user sent 20 reports in the last 24 hours. The setting `Moderation:ReportsPerDay` gives the limit. | 429 `rate_limited` |

The body of the actions of the administrators has an optional field `reason`, with 500 characters or fewer.
The moderation log keeps the reason.

A hidden deck has these effects:

- The search of public decks does not show it after the response of `PUT /moderation/hidden/deck/{id}`.
- `GET /decks/{id}` gives the status 404 to all the users except the owner. The owner gets the field `hidden: true`.
- The other users cannot copy it or add it to their favorites.

A hidden author profile has these effects:

- `GET /authors/{id}` gives the status 404 to all the users except the author. The author gets the field `hidden: true`.
- The decks of the author leave the search, and the other users cannot follow the author.
- The links of the unlisted and public decks of the author continue to work.

## Group actions on lines

The table mode of the deck page uses `POST /decks/{id}/lines/group` to change many lines in one request.
The body gives the line IDs in `lineIds`, 1,000 or fewer, and one action:

| Action | Fields | Effect |
| --- | --- | --- |
| `move` | `zone` | Moves the lines to the zone. |
| `tag` | `tags`, `globalTags`, `categories` | Adds the local tags, the global tags and the categories to each line. The API creates the tags that do not exist. |
| `untag` | `tags`, `globalTags`, `categories` | Removes the local tags, the global tags and the categories from each line. |
| `printing` | `clearPrinting`, `finish` | `clearPrinting: true` removes the printing, so the default printing of the card applies. `finish` sets the finish. |
| `quantity` | `quantityDelta` | Adds the number to the quantity of each line, from -999 to 999. A line with a quantity of 0 or less goes out of the deck. |
| `remove` | — | Removes the lines from the deck. |

The API applies the action to all the lines in one transaction.
The deck gets one new version, and the outbox gets one `decks.deck.updated` event.
With `expectedVersion`, the API refuses the change when the deck has another version, with the status 409.

After the action, two lines can have the same zone, card, printing and finish.
The API then joins them into one line, like `POST /decks/{id}/lines`.
The line that the action did not change keeps its ID, and it gets the sum of the quantities, the tags and the categories.

When a line ID is not a line of the deck, the API changes nothing and gives the status 404.
An incorrect field gives the status 400 with the list of field errors.

## Versions and variants

[ADR 0033](../decisions/0033-deck-versions-and-variants.md) describes the model.

| Endpoint | Access | Effect |
| --- | --- | --- |
| `GET /decks/{id}/versions` | Like the deck | Lists the versions, the most recent first. `workingListChanged` tells whether the working list differs from the last version. |
| `POST /decks/{id}/versions` | Owner | Commits the working list as a new version, with a message. The error `deck_no_changes` tells that nothing changed. |
| `GET /decks/{id}/versions/{versionId}` | Like the deck | Gets a version with its frozen lines and the data of its cards. |
| `GET /decks/{id}/versions/{versionId}/changes` | Like the deck | Compares a version with its first parent. |
| `POST /decks/{id}/versions/{versionId}/restore` | Owner | Copies a version into the working list, and commits a version of the kind `restore`. |
| `POST /decks/{id}/versions/{versionId}/revert` | Owner | Reverts a merge, with a version of the kind `revert`. |
| `GET /decks/{id}/variants` | Like the deck | Lists the variants of the main deck. |
| `POST /decks/{id}/variants` | Owner | Creates a variant from a version of the main deck. The default is the most recent version. |
| `POST /decks/{id}/merge` | Owner | Merges the variant `{id}` into its main deck, with a version of the kind `merge` and two parents. |
| `PUT /decks/{id}/assembled` | Owner | Marks a version as the assembled version. The default is the most recent version, after a commit of the working list when it changed. |
| `DELETE /decks/{id}/assembled` | Owner | Releases the assembled deck. Its cards go to the location `targetLocationId`, or leave the collection. |

A variant is a deck: the endpoints of the decks and of the lines work with its ID.
A variant has the format, the visibility and the folder of its main deck. A change of these fields on a variant gives a validation error.
The deck list, the folders and the public decks do not show the variants.

A comparison groups the lines by Oracle card and zone.
A change of printing or finish only, with the same quantity, is in the list `printings`, not in the list `quantities`.

### Comparison of two lists

`POST /lists/compare` compares two lists that the user can see.
Each list is the working list of a deck or of a variant, or one of its versions: `{ "deckId": "...", "versionId": null }`.
The two lists can come from different decks.
The response gives the changes from the first list to the second list, and the numbers of each list.
The numbers are the card count, the lands, the average mana value, the mana curve, the types and the categories.
The analysis of the two lists uses the format of the first deck.

### Assembled decks

The location of an assembled deck gets the cards of the commander, companion, main and sideboard zones of the assembled version.
The considering zone and the tokens are not in the location. The tokens of an assembled deck are in the deck tokens of the collection.
The language of a card is the language of its printing, or English when the line has no printing.
The API refuses the deletion of an assembled deck with the error `deck_assembled`. Release the deck first.
A variant cannot be an assembled deck.
