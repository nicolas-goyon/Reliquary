# 0007. Modular monolith for the core

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

The core has several feature areas: accounts, card data and card search, decks, the collection and deck testing.
Trusted modules come after milestone M4.
A module will have its own schema and its own tables.
It will communicate with the core through a versioned internal API and domain events.
It will never change the tables of the core.
The arrival of modules must not need a rewrite of the core.

## Decision

The core is a modular monolith with five core components:

| Core component | Content |
| --- | --- |
| Accounts | Accounts, authentication, API keys, quotas, service accounts and delegation tokens |
| Cards | Card reference data, card search and deck analysis |
| Decks | Decks, deck versions, variants, tags and folders |
| Collection | Locations, location entries, movements and inventories |
| Playtest | The table engine and the game sessions of online games |

These rules apply to the core components:

- Each core component owns its tables.
- A core component communicates with other core components only through interfaces and domain events.
  It never reads or writes the tables of another core component.
- The core components publish versioned domain events through an outbox table from milestone M0, even when no consumer exists.
- The core applies to itself the constraints that it will apply to modules.

## Consequences

- The arrival of modules needs no rewrite of the core.
- A feature that needs the data of two core components uses an interface or a domain event, not a SQL join between their tables.
- Architecture tests check that no core component references another core component.
  The tests are in `tests/Reliquary.Architecture.Tests`.
- The outbox and the domain events exist from milestone M0, before any module can consume them.
- The [architecture overview](../architecture/overview.md) and the [extensions](../architecture/extensions.md) file give the details.
