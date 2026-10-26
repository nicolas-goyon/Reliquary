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

- [Scryfall](https://scryfall.com/docs/api): the daily bulk data (cards, printings, card faces, localized names and legalities), and the `otag:` search for auto-tag.
  Reliquary shows the card images from the Scryfall servers.
- [MTGJSON](https://mtgjson.com/): external IDs (Cardmarket, TCGplayer and MTGO) and preconstructed decks.
- [EDHREC](https://edhrec.com/): Commander recommendations.
  Reliquary can also use recommender.cards, if access to it is possible.
- [Commander Spellbook](https://commanderspellbook.com/): combo detection.

A scheduled task synchronizes the card data.
The task keeps a version of each import, so that it can run an import again.
[ADR 0012](../decisions/0012-card-data-sources.md) records the choice of the sources.

## Reference data

All users share the reference data.

| Entity | Content | Note |
| --- | --- | --- |
| OracleCard | Oracle identity, legalities, color identity | One for each logical card |
| CardFace | Name, cost, types and text of each face or half | Covers double-faced cards, split cards, Rooms, Adventures, prepared creatures and meld cards |
| CardName | All the names that identify the card: localized names, alternate names and face names | Search and import use these names |
| Printing | Set, collector number, language, finishes, external IDs | Several for each OracleCard |

## User data

Reliquary can split all the user data by `owner_id`.

| Entity | Content | Note |
| --- | --- | --- |
| Deck | Name, format, visibility, folder, tags, and the location if the deck is an assembled deck | Refers to its current deck version |
| DeckVersion | Frozen list of deck lines, message, parent versions, variant | Never changes, like a Git commit |
| DeckLine | OracleCard, quantity, zone, tags, note, purchase preferences (wanted or avoided printings, languages, minimum condition, foil, maximum price) | The printing is optional |
| Location | Name, type (rack, binder, storage box, assembled deck or cube), optional parent location, `locked`, `pickable` | Two levels at most: the location, then the section |
| LocationEntry | Location, OracleCard, quantity, language, optional printing, optional advanced fields | The collection is the sum of the location entries |
| Inventory | Location, date, recorded content | A dated record of one location |
| Movement | Type (entry, exit or move), origin and destination (a location, a purchase, a sale, a gift or "nowhere"), quantity, date | A simple log |
| Tag, Folder | Global tags, or local tags of one deck. Nested folders. | — |
| CollectionShare | Owner, guest, locations, rights (read, difference or edit) | In the model from the start. A later milestone delivers the feature. |
| ApiKey, UsageCounter | API keys, request counters for each day | Basis of the quotas |
| ExtensionInstall | Type (plugin or module), version, permissions, slots, settings | Plugin: one for each user. Module: one for each instance. |
| AccountLink | User, external site, extension, authentication method, encrypted access tokens, state, last synchronization, consent to act while the user is offline | Source of the **Linked accounts** panel |
| DomainEvent (outbox) | Event type, version, `owner_id`, payload, date | Basis of the event bus for modules |
