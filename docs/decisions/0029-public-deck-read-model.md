# 0029. Read model of the public decks

- Status: Accepted
- Date: 2026-10-27
- Deciders: @nicolas-goyon

## Context

The decks are in the shard of their owner ([ADR 0008](0008-postgresql-schemas-and-shard-directory.md)).
The search of public decks must find the decks of all the shards.
It has filters by format, commander, colors, card and author, and a sort by popularity.
A query on each shard for each search does not scale, and it is slow when an instance has many shards.
A deck that becomes public must show in the search after less than 10 seconds ([#107](https://github.com/nicolas-goyon/reliquary/issues/107)).
The core components must not read the tables of another core component ([ADR 0007](0007-modular-monolith.md)).

## Decision

- The Decks component keeps a **read model** of the public decks in the users database: the table `users.public_decks`.
  A row contains the fields of the search: name, format, description, color identity, commanders and card count.
  It also contains the Oracle IDs of the cards of the main zones and the deck version.
- The domain events of the decks fill the read model ([domain events](../architecture/domain-events.md)).
  A consumer in the worker receives `decks.deck.created`, `decks.deck.updated` and `decks.deck.deleted`.
- The consumer does not use the payload to build the row. It reads the current state of the deck in its shard.
  A public deck gets a row. A deck with another visibility, or a deleted deck, has no row.
  So an event that arrives two times, or after a more recent event, gives the same result.
  The row keeps the most recent deck version: an older version never replaces a more recent version.
- The popularity of a deck is the number of copies by other users plus the number of favorites.
  The table `users.public_deck_copies` has one row for each copy, so an event that arrives two times counts one time.
- The favorites (`users.deck_favorites`) and the followed authors (`users.author_follows`) are in the users database too.
- The Decks component gets the names of the authors through the interface `IAccountDirectory` of the building blocks.
  The Accounts component implements it.
- The outbox publisher reads the outboxes every second (`Outbox:PollInterval`), so a change shows in the search after about one second.

## Consequences

- The search reads one database and uses GIN indexes on the commanders and the cards.
- The read model is a copy. A failure of the consumer delays the search, and the publisher tries again ([domain events](../architecture/domain-events.md)).
- A public deck that existed before this migration has no row until its next change.
  Milestone M1 has no production data, so the project accepts this. A command that builds the read model again can come later.
- The deletion of an account ([#113](https://github.com/nicolas-goyon/reliquary/issues/113)) must remove the rows of the account from the read model, the favorites and the follows.
