# Collection

This page tells how the Collection component stores the collection and changes it.
Terms follow the [glossary](../glossary.md).
[ADR 0034](../decisions/0034-collection-model.md) gives the reasons for the collection model.
The [specification](../specification/collection.md) gives the features.

## Endpoints

All the paths start with `/api/v1/collection`.
Each endpoint needs a signed-in user or an API key, and works on the collection of this user only.
The OpenAPI document gives the bodies and the responses.

| Endpoint | Effect |
| --- | --- |
| `GET /locations` | Lists the locations, each with its sections and its number of cards. |
| `POST /locations` | Creates a location or a section. |
| `GET /locations/{id}` | Gets a location with its sections. |
| `PATCH /locations/{id}` | Changes the name, the kind, the pickable flag or the position of a location. |
| `POST /locations/{id}/lock` | Locks a location. |
| `POST /locations/{id}/unlock` | Unlocks a location. |
| `DELETE /locations/{id}` | Deletes an empty location and its empty sections. |
| `GET /locations/{id}/entries` | Lists the entries of a location by card name. `sections=true` adds the entries of the sections. `q` finds a part of the card name. `language` and `set` filter the entries. |
| `POST /locations/{id}/entries` | Adds cards to a location. `origin` gives `purchase`, `gift`, `trade` or `nowhere` (the default). |
| `PATCH /entries/{id}` | Changes the quantity or the fields of an entry. A quantity of 0 removes the entry. |
| `DELETE /entries/{id}` | Removes all the copies of an entry. `destination` gives `sale`, `gift`, `trade` or `nowhere` (the default). |
| `POST /movements` | Adds, removes or moves cards in one request. See [movements](#movements). |
| `GET /movements` | Lists the movements, the most recent first. `locationId` gives a location and its sections. `batchId` gives one request. |
| `POST /locations/{id}/inventories` | Starts an inventory with the counted content, and gives the differences. See [inventories](#inventories). |
| `GET /locations/{id}/inventories` | Lists the inventories of a location, the most recent first. |
| `GET /inventories/{id}` | Gets an inventory: the counted content and, while it is open, the differences. |
| `POST /inventories/{id}/apply` | Applies an inventory with the answers of the user. |
| `DELETE /inventories/{id}` | Stops an open inventory with no change. |
| `GET /cards` | Gives the global view: all the cards by name, grouped by Oracle card, with the copies in each location. See [global view](#global-view). |
| `GET /storage-rules` | Lists the storage rules by priority. |
| `POST /storage-rules` | Creates a storage rule. |
| `PATCH /storage-rules/{id}` | Changes the location, the card search or the priority of a storage rule. |
| `DELETE /storage-rules/{id}` | Deletes a storage rule. |
| `GET /storage-rules/suggestion?oracleId=` | Gives the first storage rule that matches a card, or no content. |
| `GET /locations/{id}/tokens` | Lists the tokens of an assembled deck, and whether the user has each token. |
| `PUT /locations/{id}/tokens/{oracleId}` | Marks a token of an assembled deck as owned or not. |
| `DELETE /locations/{id}/tokens/{oracleId}` | Removes a token from the list of an assembled deck. |
| `GET /settings` | Gets the advanced fields that the user enabled. |
| `PUT /settings` | Sets the advanced fields that the user enabled. |
| `POST /locations/{id}/import` | Adds the cards of a file to a location. See [import and export](#import-and-export). |
| `GET /locations/{id}/export?format=` | Gives the cards of a location as a file. `sections=true` adds the cards of the sections. |
| `GET /export?format=` | Gives the cards of all the locations as a file. |

## Locations

- The kinds of a location are `rack`, `binder`, `box`, `deck` and `cube`.
  The API creates the kinds `rack`, `binder`, `box` and `cube`.
  Only the Decks component creates a location of the kind `deck`, through `IAssembledDeckLocations`.
- A location has two levels at most.
  A section has a parent location, and a section cannot have a section.
- A section has the kind of its parent location.
  A change of the kind of a location also changes the kind of its sections.
- A user has 1,000 locations at most, with the sections.
- The API refuses the deletion of a location when the location or one of its sections has cards (`location_not_empty`).

## Flags

Each location has two flags: `locked` and `pickable`.

- A locked location refuses each change of its entries, also a movement, with the error `location_locked`.
  The sections of a locked location are also locked.
  The flag of a section does not change when its parent location is locked.
- The cards of a section are available cards only when the section and its parent location are pickable.
- The cards of an assembled deck are never available cards.
  The location of an assembled deck is not pickable.

## Entries

- An entry has an Oracle card, an optional printing, a language, a quantity from 1 to 100,000 and the advanced fields.
- The API checks each card with `ICardCatalog`.
  The card must exist, and the printing must be a printing of the card.
- The default language is the language of the printing, or `en` with no printing.
  When the request gives a printing and a language, the language must be the language of the printing.
- A finish must be a finish of the printing.
- Two entries of one location never have the same card, printing, language and advanced fields.
  A new copy with the same fields adds to the quantity of the entry.
- A change of a field of an entry moves the copies to the entry with the new fields.
- A request can contain 1,000 cards at most.
- The table `location_entries` keeps a copy of the Oracle name and of the set code of the printing.
  The sort, the search and the filters use these columns, because the Collection component cannot read the card tables.

## Advanced fields

The advanced collection adds optional fields to the entries.
The user enables each field separately in the table `collection_settings`.
By default, no field is enabled.

| Field | Values |
| --- | --- |
| `finish` | `nonfoil`, `foil` or `etched` |
| `condition` | `mint`, `near_mint`, `excellent`, `good`, `light_played`, `played` or `poor`, from `CardConditions` of the building blocks |
| `purchasePrice` | The price of one copy, with 2 decimals at most, and `purchaseCurrency`, a code of 3 letters such as `EUR` |
| `acquiredOn` | The date when the user got the copies, for example `2026-10-28` |
| `note` | A text of 500 characters at most |

- The API ignores a disabled field in a request, and it does not check its value.
  A client can send all the fields, for example in an import, and the API keeps the enabled fields only.
- The API never returns a disabled field. An enabled field with no value is not in the response.
- When the user disables a field, the stored values stay. They come back when the user enables the field again.
  A change of an entry keeps the stored value of a disabled field.
- `ICollectionCatalog` gives the finish and the condition only when the user enabled them.

## Storage rules

A storage rule suggests a location for a new card.
Example: the rule `c:u` with the location "Blue rack" tells that the blue cards go to this rack.

- A rule has a location, a card search in the syntax of the [card search](card-search.md) and a priority.
- The rule with the lowest priority number applies first. The suggestion gives the first rule that matches the card.
- The API checks the card search with `ICardCatalog.MatchAsync`. A query that the card search cannot use gives a field error on `query`.
- A rule cannot give the location of an assembled deck. A user has 100 rules at most.
- The form of the location page asks for the suggestion when the user selects a card.

## Tokens of the assembled decks

- `IAssembledDeckLocations.SetContentAsync` puts the tokens of the deck in the table `deck_tokens`, not in the entries.
  A token is an extra card with "Token" in its type line.
- A new token is not owned. The user marks each token as owned or not on the page of the location.
- A new content of the deck keeps the mark of each token that stays, and removes the other tokens.

## Inventories

An inventory compares a counted content with the content of a location.

1. The user counts the cards of the location and sends the counted content: `POST /locations/{id}/inventories`.
   The API keeps the counted content in `inventory_entries`, and the inventory is `open`.
   An open inventory of the same location stops.
2. The API gives the differences, card field by card field.
   The comparison uses the advanced fields that the user enabled.
   Each difference has a stable `key`, the expected and the counted quantities, and the change.
3. For each difference, the user gives an answer: the origin of the added copies, or the destination of the removed copies.
   The group answers `origin` and `destination` apply to the differences with no answer.
   An origin can be a location: the copies then move from this location.
4. `POST /inventories/{id}/apply` writes one movement for each difference, with the inventory ID, in one transaction.
   Then the location has the counted content, and the inventory is `applied` with its date.

- The API calculates the differences again when the user applies the inventory, because the location can change in the meantime.
- A locked location refuses an inventory with differences (`location_locked`). An inventory with no difference only records the content.
- An applied inventory never changes (`inventory_applied`). Its entries give the content of the location at the date of the inventory.
- The location of an assembled deck has no inventory (`deck_location`).

## Global view

`GET /cards` combines all the locations and shows the duplicates.
Each item is one Oracle card with the number of copies, and the number of copies in each location or section.
Example: "4 Sol Ring: Rack 1 (2), Rack 1 / Box A (1), Atraxa (1)".

- The locations of a card come in the order of the location list. The assembled decks come at the end.
- The filters are `q` (a part of the card name), `locationId` (a location and its sections), `language` and `set` (a set code).
- The list uses the keyset paging of the [API conventions](api-conventions.md#paging): the cursor contains the name and the Oracle ID of the last card.
- The query uses the index on `(owner_id, card_name, oracle_id)`.
  A test seeds 50,000 entries for one owner and measures the API.
  The first page of 50 cards takes 11 ms, and a search by name takes 69 ms, on the computer of the test.
  The test fails when a page takes 500 ms or more.

## Import and export

The import adds the cards of a file to one location or section that the user selects.
The export writes a location, a location with its sections, or all the collection to a file.

### Formats

| Format | Name in the API | Fields of the file |
| --- | --- | --- |
| Reliquary text | `text` | Quantity, name, set code, collector number and finish, in the syntax of the [deck text format](../development/deck-text-format.md) |
| Generic CSV | `csv` | All the fields, and the location of each entry |
| Magic Online XML | `dek` | Quantity and name |
| MTG Arena | `mtga` | Quantity, name, set code and collector number |
| Magic Online text | `mtgo` | Quantity and name |
| Moxfield | `moxfield` | The collection CSV file of Moxfield: quantity, name, set code, collector number, language, finish, condition and purchase price. The import also reads the text of Moxfield. |
| Archidekt | `archidekt` | Quantity, name, set code, collector number and finish. The import ignores the categories and the labels. |
| Deckbox | `deckbox` | Quantity, name, set code, collector number, language, finish and condition |
| ManaBox | `manabox` | Quantity, name, set code, collector number, language, finish, condition, purchase price and currency |
| Dragon Shield | `dragonshield` | Quantity, name, set code, collector number, language, finish, condition, purchase price, entry date and location |

The import of a CSV file finds the columns from the header.
The comparison of the header names ignores the case, the spaces, the hyphens and the underscores.
The separator can be a comma, a semicolon or a tab, and the import ignores a first line `sep=,`.

The generic CSV format accepts these header names:

| Field | Header names |
| --- | --- |
| Quantity | `Quantity`, `Count`, `Qty`, `Amount` |
| Name | `Name`, `Card name`, `Card` |
| Set code | `Set code`, `Set`, `Edition code` |
| Collector number | `Collector number`, `Card number`, `Number` |
| Language | `Language`, `Lang` |
| Finish | `Finish`, `Foil`, `Printing` |
| Condition | `Condition` |
| Purchase price | `Purchase price`, `Price bought`, `Price` |
| Currency | `Purchase currency`, `Purchase price currency`, `Currency` |
| Entry date | `Entry date`, `Acquired on`, `Date bought`, `Date added` |
| Note | `Note`, `Notes` |

### Rules of the import

- The import uses the same code as `POST /locations/{id}/entries`.
  It writes one movement for each line, with the origin of the request: `purchase`, `gift`, `trade` or `nowhere` (the default).
  All the movements of the import have one batch ID.
- The import adds the lines that it can use, and ignores the other lines.
  The response gives each ignored line in `errors`, with its number, its reason code and its text.
- A locked location refuses the import with the error `location_locked`.
  The location of an assembled deck refuses the import with the error `deck_location`.
- A file can have 4,000,000 characters and 20,000 card lines at most.
- The import accepts all the card names: Oracle names, face names, localized names and alternate names.
- A set code and a collector number give the printing in the language of the line.
  When the import finds no printing, the card has no printing, and the response gives a warning `printing_not_found`.
  When the printing does not have the finish, the card has no finish, and the response gives a warning `finish_not_available`.
- The import reads an advanced field only when the user enabled it. It ignores the other columns.
- A language is a code, for example `fr`, or an English name, for example `French`.
- A finish is `foil`, `etched` or a different value for a nonfoil card. The values `true`, `yes` and `1` also give a foil card.
- A price can have a symbol `$`, `€` or `£`, which gives the currency.
  A price with no currency gets the currency of the request (`currency`, by default `USD`).
- A date has the form `2026-10-28` or `10/28/2026`.

The import reads the conditions of three scales:

| Condition | Values of the files |
| --- | --- |
| `mint` | Mint, M, MT |
| `near_mint` | Near Mint, NM |
| `excellent` | Excellent, EX |
| `good` | Good, GD, Good (Lightly Played) |
| `light_played` | Light Played, Lightly Played, LP |
| `played` | Played, PL, Moderately Played, MP, Heavily Played, HP |
| `poor` | Poor, PO, Damaged |

The reason codes of the lines are:

| Code | Kind | Meaning |
| --- | --- | --- |
| `missing_name` | Error | The line has no card name. |
| `quantity_out_of_range` | Error | The quantity is not from 1 to 100,000. |
| `invalid_file` | Error | The import cannot read the XML file. |
| `missing_column` | Error | The CSV file has no column for the card names. The line is the header. |
| `card_not_found` | Error | No card has this name. |
| `unknown_language` | Error | The language is not a known language. |
| `unknown_condition` | Error | The condition is not a known condition. |
| `invalid_price` | Error | The purchase price or its currency is not valid. |
| `invalid_date` | Error | The entry date is not a date. |
| `note_too_long` | Error | The note has more than 500 characters. |
| `printing_not_found` | Warning | The import found no printing. The card has no printing. |
| `finish_not_available` | Warning | The printing does not have the finish. The card has no finish. |

### Rules of the export

- The response gives the name of the file, its media type and its content.
- The export writes only the advanced fields that the user enabled.
- The export writes the locations in the order of the list. The sections come after their location.
- The Reliquary text starts the cards of each location with a comment line, for example `// Rack 1 / Box A`, when the file has more than one location.
- The formats with fewer fields add the quantities of the cards that they write in the same way.
  For example, the Magic Online export gives one line for each card name.
- Moxfield, Deckbox and Dragon Shield have other scales of conditions.
  The export of Moxfield writes `excellent`, `good` and `light_played` as Lightly Played, `played` as Moderately Played and `poor` as Damaged.
  The export of Deckbox writes `excellent`, `good` and `light_played` as Good (Lightly Played).

## Web pages

| Page | Content |
| --- | --- |
| `/collection` | The global view with its filters, and the settings of the advanced collection |
| `/collection/locations` | The locations and their sections: create, rename, change the kind, lock, unlock, the pickable flag and delete |
| `/collection/locations/{id}` | The cards of a location and of its sections. The form adds cards with the card autocomplete and the enabled advanced fields. The user selects entries and moves all their copies in one request. |
| `/collection/movements` | The history of the movements, with a filter by location |
| `/collection/exchange` | The import of a file into a location, and the export of a location, of a location with its sections, or of all the collection |
| `/collection/locations/{id}/inventory` | The count of a location, the differences with their answers and group answers, and the history of the inventories with the content at each date |

The navigation shows the link **Collection** to the signed-in users.

## Movements

Each change of the entries writes movements in the same transaction.
All the movements of one change have the same batch ID.
A database trigger refuses each change of a movement.

| Kind | Origin | Destination |
| --- | --- | --- |
| `add` | `purchase`, `gift`, `trade` or `nowhere` | A location |
| `remove` | A location | `sale`, `gift`, `trade` or `nowhere` |
| `move` | A location | Another location |

A change of the quantity or of the fields of an entry writes movements from or to `nowhere`.

`POST /movements` gives one origin, one destination and a list of cards.
All the movements of the request get one batch ID, so the user can move 10 cards in one action.

- When the origin is a location, each card gives an entry of this location and a quantity.
  With no quantity, the movement takes all the copies of the entry.
- When the origin is not a location, each card gives the card fields, as in the addition of entries.
- The origin or the destination must be a location. The destination must be another location than the origin.
- A move keeps the card fields of the copies. The copies join the entry with the same fields in the destination.
- A locked location refuses each movement with the error `location_locked`.
  The location of an assembled deck refuses each movement with the error `deck_location`.
- An entry with fewer copies than the quantity gives the error `not_enough_cards`. The API then changes nothing.

The history gives the name of each location. A deleted location has no name, but its movements stay.

## Assembled decks

The Collection component implements the interfaces `ICollectionCatalog` and `IAssembledDeckLocations` of the building blocks.

- `ICollectionCatalog.GetCopiesAsync` gives the copies of some Oracle cards, with the location, the section and the flags.
  A copy is available when its location is pickable and is not an assembled deck.
- `IAssembledDeckLocations.EnsureLocationAsync` creates the location of a deck, or renames it.
- `IAssembledDeckLocations.SetContentAsync` gives the location the cards of the assembled version.
  It records the differences as movements from or to `nowhere`.
  The tokens of the deck go to the table `deck_tokens`, not to the entries.
- `IAssembledDeckLocations.ReleaseAsync` moves the cards to another location, or removes them.
  Then it deletes the location.
- A locked location gives the exception `LocationLockedException` to the Decks component.
- The API refuses each change of the location of an assembled deck with the error `deck_location`.
  The lock and the unlock stay possible.

## Errors

| Code | Status | Meaning |
| --- | --- | --- |
| `location_locked` | 409 | The location or its parent location is locked. Unlock the location, then try again. |
| `location_not_empty` | 409 | The location or one of its sections has cards. Move or remove the cards first. |
| `deck_location` | 409 | The location belongs to an assembled deck. Change the deck to change the location. |
| `conflict` | 409 | The location is not an assembled deck, so it has no tokens. Or the user has the maximum number of locations or storage rules. |
| `not_enough_cards` | 409 | The origin location does not have enough copies of a card. |
| `inventory_applied` | 409 | The inventory is applied. It cannot change. Start a new inventory. |

The field error `card_not_found` tells that no card has the name of a card of the request.

## Domain events

The component writes these events to the outbox of the shard, in the transaction of the change.

| Event | Payload |
| --- | --- |
| `collection.location.created` | The location, its kind, its parent location and its deck |
| `collection.location.updated` | The location, its kind and its flags |
| `collection.location.deleted` | The location |
| `collection.entries.changed` | The batch ID of the movements, the locations and the Oracle cards |

## Personal data

The export of an account contains the folder `collection`:

- `locations.json`: the locations, the sections and all the fields of their entries.
- `text/<location>.txt`: the cards of each location in the [deck text format](../development/deck-text-format.md).
  A comment line starts each section. The text files do not contain the language and the advanced fields.
- `settings.json`, `movements.json`, `inventories.json`, `storage-rules.json`, `deck-tokens.json` and `shares.json`.

The deletion of an account deletes all the rows of the owner in the collection tables.
It also deletes the collection shares where the owner is the guest.
