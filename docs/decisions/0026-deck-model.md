# 0026. Deck model of milestone M1

- Status: Accepted
- Date: 2026-10-27
- Deciders: @nicolas-goyon

## Context

The [data model](../architecture/data-model.md) gives a deck, frozen deck versions like Git commits, and deck lines.
Milestone M1 delivers the deck building. Milestone M2 delivers the deck versions and the variants.
The decks are user data, so they are in the shard of their owner ([ADR 0008](0008-postgresql-schemas-and-shard-directory.md)).
A link to an unlisted or public deck must work for any user, also when the owner is in another shard.
The Decks component must not read the tables of the Cards component ([ADR 0007](0007-modular-monolith.md)).

## Decision

- The table `content.decks` contains the fields of a deck: name, format, visibility, folder, description, pin, archive and the source deck of a copy.
- The table `content.deck_lines` contains the **working list** of a deck: the lines that the user changes.
  A line has an Oracle card, an optional printing, an optional finish, a quantity, a zone and a note.
  A deck has one line for each card, printing and finish in a zone. An addition to an existing line adds the quantity.
- Each change of a deck or of its lines increases the version of the deck, in the same transaction.
  A client can send the version that it read, and the API refuses the change when the deck has another version.
- Milestone M2 adds the frozen deck versions: a commit copies the working list into a version.
  The working list stays the list that the user changes, so M2 does not change the endpoints of M1.
- The table `users.deck_directory` gives the owner of each deck. The API uses it to find the shard of a deck from its ID only.
- The owner sees and changes each of their decks. The other users and the visitors only see the unlisted and public decks.
  A private deck of another user gives the error `not_found`, so the API does not tell that it exists.
- The Decks component gets the card data through `ICardCatalog` ([ADR 0025](0025-multi-face-analysis-rules.md)).
  It checks that each card and each printing exists, and that each printing is a printing of the card of its line.
- The component writes the domain events `decks.deck.created`, `decks.deck.updated` and `decks.deck.deleted` to the outbox of the shard.

## Consequences

- [Decks](../architecture/decks.md) gives the endpoints, the copy and the legality rules.

- The deck page gets a deck, its lines, its cards and its printings in one request.
- The read model of the public decks ([#107](https://github.com/nicolas-goyon/reliquary/issues/107)) can use the domain events.
- A deck directory entry with no deck can stay after a failure. It has no effect, because the deck is not in the shard.
- The table `content.tags` contains the global tags of a user and the local tags of each deck. A line can have tags and predefined categories.
  A request gives the tags by name, and the API creates the tags that do not exist.
