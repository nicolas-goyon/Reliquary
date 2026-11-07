# 0034. Collection model

- Status: Accepted
- Date: 2026-10-28
- Deciders: @nicolas-goyon

## Context

Milestone M2 adds the collection: locations, location entries, movements, inventories, storage rules and the import and export ([specification](../specification/collection.md)).
The [deck and collection rules](../specification/overview.md#deck-and-collection-rules) connect the collection to the decks:

- An assembled deck is a location.
- The available cards are the cards of the pickable locations. The cards of an assembled deck are never available cards.
- A locked location cannot change until the user unlocks it.

The purchase list and the transition plan need the decks and the collection together.
The core components must not read the tables of another core component ([ADR 0007](0007-modular-monolith.md)).
A collection can have 50,000 cards or more.

## Decision

### Tables

The Collection component keeps the collection in the shard of the owner, in the `content` schema.

| Table | Content |
| --- | --- |
| `locations` | Name, kind (`rack`, `binder`, `box`, `deck` or `cube`), optional parent location, `locked`, `pickable`, the deck ID of an assembled deck, order |
| `location_entries` | Location, Oracle card, optional printing, language, quantity and the advanced fields: finish, condition, purchase price and currency, entry date and note |
| `collection_settings` | The advanced fields that the owner enabled |
| `movements` | Kind (`add`, `remove` or `move`), origin, destination, the card fields, quantity, date and a batch ID for group actions. An origin or a destination is a location, `purchase`, `sale`, `gift`, `trade` or `nowhere`. |
| `inventories`, `inventory_entries` | The date of each inventory of a location, and the frozen content of the location at this date |
| `storage_rules` | A location, a card search in the syntax of the card search, and a priority |
| `deck_tokens` | For each assembled deck, the tokens and whether the owner has them |

- A location has two levels at most. A section has a parent location, and a section cannot have a section.
  The section has the kind of its parent.
- A location of the kind `deck` has the ID of its deck. Only the Decks component creates it, through the interface below.
- Two entries of one location with the same card, printing, language and advanced fields are one entry. A new copy adds to its quantity.
- An advanced field that the owner did not enable has no value. The API does not require it and does not return it.
- A locked location refuses each change of its entries, also a movement. The API gives the error `location_locked`.
- Each change of the entries writes a movement in the same transaction. A movement never changes.

### Collection shares

- The table `users.collection_shares` gives the owner, the guest, the locations (or all of them) and the rights (`read`, `difference` or `edit`).
  It is in the users database, because a guest must find the shares of other owners.
- M2 creates the table and does not use it. A later milestone delivers the sharing ([#66](https://github.com/nicolas-goyon/reliquary/issues/66)).

### Interfaces of the building blocks

- `ICollectionCatalog` gives the copies of some Oracle cards in the collection of an owner, with their location and their availability.
  A copy is available when its location is pickable and is not an assembled deck.
  The Decks component uses it for the purchase list and the transition plan.
- `IAssembledDeckLocations` lets the Decks component create the location of an assembled deck, set its content and release it.
- The Collection component implements the two interfaces.

### Events

The component writes the events `collection.location.created`, `collection.location.updated`, `collection.location.deleted` and `collection.entries.changed` to the outbox of the shard.

## Consequences

- The global view and the availability queries read one shard and use indexes on `(owner_id, oracle_id)`.
  A collection of 50,000 cards has at most 50,000 entries, so a query with the index stays fast.
- The transition plan can take cards from a locked location. It tells the user to unlock the location first.
- The personal data export and deletion ([ADR 0032](0032-personal-data.md)) must include all the tables of the collection.
