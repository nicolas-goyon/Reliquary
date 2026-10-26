# 0014. Product scope

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

The project needs a name for the product and a name for its extensions.
It must also choose a priority format for the first milestones.
Two features need a scope decision: card loans and game tracking.

## Decision

- The name of the product is **Reliquary**: the reliquary where players keep their cards, their decks and their collection.
  The project keeps three other names in reserve: Tutor, Folio and Brewkit.
- Extensions keep the standard name **plugins**, so that users understand the name immediately.
  The trusted extensions with a server part have the name **modules** ([ADR 0010](0010-plugins-and-modules.md)).
- Commander is the priority format.
  The minimum viable product (MVP) of milestone M1 first targets the Commander player who builds and tests decks.
- Card loans are out of scope.
- Reliquary does not track games.
  A separate tool tracks games, and a plugin connects this tool to Reliquary.

## Consequences

- The legality checks and the deck analysis support Commander first.
  Other formats come later.
- The data model has no entity for card loans.
- The connection to a game tracking tool needs plugins, which arrive in milestone M4.
