# 0013. Locations with two levels

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

Users keep their cards in racks, binders, storage boxes, assembled decks and cubes.
Inside a rack, the cards can be in boxes.
A collector wants to know where each card is.
For example, a collector asks "Where is my Sol Ring?", and the answer is "Rack 2, blue section".
The specification wants a simple management of the collection: the user adds, removes and moves cards.

## Decision

- A location has two levels at most:
  - the location: a rack, a binder, a storage box, an assembled deck or a cube
  - an optional section, for example a box in a rack
- Reliquary has no level for a page or a pocket.
- In the data model, the Location entity has an optional parent, with two levels at most.

## Consequences

- The user can find a card by location and section, for example "rack 2, blue section".
- Reliquary cannot give the page or the pocket of a card in a binder.
- The [data model](../architecture/data-model.md) gives the Location entity.
