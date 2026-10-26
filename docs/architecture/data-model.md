# Data model

This file describes the data model of Reliquary: the two kinds of data, the data sources, the reference data and the user data.
Terms follow the [glossary](../glossary.md).

## Two kinds of data

The model separates two kinds of data:

- **Reference data** is the card data.
  All users share it.
  Users can read it, but they cannot change it.
- **User data** is the data of each user.
  Each table of user data has an `owner_id` column, so that Reliquary can split the user data by user.

The [database](./database.md) file tells how Reliquary splits and moves the data.

## Sources

Reliquary uses these sources:

- [Scryfall](https://scryfall.com/docs/api): the bulk data (cards, printings, card faces, localized names, legalities, rulings, Oracle tags and illustration tags) and the sets API.
  Reliquary shows the card images from the Scryfall servers.
- [MTGJSON](https://mtgjson.com/): external IDs (Cardmarket, TCGplayer, Card Kingdom and others) and preconstructed decks.
- [EDHREC](https://edhrec.com/): Commander recommendations.
  Reliquary can also use [recommander.cards](https://recommander.cards), an optional source with a public API.
- [Commander Spellbook](https://commanderspellbook.com/): combo detection.

A scheduled job imports the card data every 12 hours.
The table `reference.import_runs` records each import, so that the job can skip a file that did not change.
[ADR 0012](../decisions/0012-card-data-sources.md) records the choice of the sources, and [ADR 0020](../decisions/0020-card-data-import.md) records the import.

## Reference data

All users share the reference data.
The Cards core component owns the tables of the `reference` schema.

| Entity | Table | Content | Note |
| --- | --- | --- | --- |
| OracleCard | `oracle_cards` | Oracle identity, costs, types, text, colors, legalities, color identity, default printing | One for each logical card. For a card with more than one face, the text columns contain all the faces. |
| CardFace | `card_faces` | Name, cost, mana value, types, text, colors and statistics of each face or half | One row for a card with one face. Covers double-faced cards, split cards, Rooms, Adventures, prepared creatures, flip cards and meld cards. |
| CardName | `card_names` | All the names that identify the card: Oracle name, face names, localized names and alternate names | The search and the import use these names. |
| Printing | `printings` | Set, collector number, language, rarity, finishes, images, prices, external IDs | Several for each OracleCard |
| PrintingFace | `printing_faces` | The printed text and the images of each face of a printing | Only for a card with more than one face |
| Set | `sets` | Code, name, type, release date, block | From the Scryfall sets API |
| CardRelation | `card_relations` | Tokens, meld parts, meld results and combo pieces of a card | — |
| Tag | `tags`, `tag_closure` | Oracle tags and illustration tags of Scryfall Tagger, with their parent tags | `tag_closure` gives all the descendants of each tag. |
| Tagging | `oracle_taggings`, `illustration_taggings` | The links between the tags and the cards or the illustrations | — |
| Ruling | `rulings` | The rulings of each card | — |
| ImportRun | `import_runs` | Each import of a source file: source, date of the file, result | — |

### Rules of the card data

- The identifier of an Oracle card is the Scryfall Oracle ID. The identifier of a printing is the Scryfall ID.
- A reversible card shows the same card on its two sides. Its first face gives the Oracle card.
- Scryfall gives no colors for the halves of a split card or an Adventure. Their colors come from their mana costs.
- The comparison of the names ignores the case and the accents of the Latin, Greek and Cyrillic letters. It also makes the apostrophes and the dashes simple.
- An extra card is not for a deck. Examples: a token, an emblem, an art card, a plane, a scheme or a vanguard.
  A card that exists only in token or memorabilia sets is also an extra card.
- The default printing of a card is its most recent printing that a player recognizes.
  The import prefers an English printing that Wizards of the Coast already released, in a regular set.
  It also prefers a normal collector number, a modern frame and no special treatment.

## User data

Reliquary can split all the user data by `owner_id`.

| Entity | Content | Note |
| --- | --- | --- |
| Deck | Name, format, visibility, folder, tags, and the location if the deck is an assembled deck | Refers to its current deck version |
| DeckVersion | Frozen list of deck lines, message, parent versions, variant | Never changes, like a Git commit |
| DeckLine | OracleCard, quantity, zone, tags, note, purchase preferences (wanted or avoided printings, languages, minimum condition, foil, maximum price) | The printing is optional |
| Location | Name, type (rack, binder, storage box, assembled deck or cube), optional parent location (for a section), `locked`, `pickable` | Two levels at most: the location, then the section |
| LocationEntry | Location, OracleCard, quantity, language, optional printing, optional advanced fields | The collection is the sum of the location entries |
| Inventory | Location, date, recorded content | A dated record of one location |
| Movement | Type (cards in, cards out or a move), origin and destination (a location, a purchase, a sale, a gift or "nowhere"), quantity, date | A simple log |
| Tag, Folder | Global tags, or local tags of one deck. Nested folders. | — |
| CollectionShare | Owner, guest, locations, rights (read, difference or edit) | In the model from the start. A later milestone delivers the feature. |
| ApiKey, UsageCounter | API keys, daily request counters | Basis of the quotas |
| ExtensionInstall | Type (plugin or module), version, permissions, slots, settings | Plugin: one for each user. Module: one for each instance. |
| AccountLink | User, external site, extension, authentication method, encrypted access tokens, state, last synchronization, consent to act while the user is not connected | Source of the **Linked accounts** panel |
| DomainEvent (outbox) | Event type, version, `owner_id`, payload, date | Basis of the event bus for modules |
