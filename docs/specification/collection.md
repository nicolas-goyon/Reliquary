# Collection

This file describes the collection of a user.
It covers the detail level, the locations, the movements and inventories, the sharing, and the import and export.
Terms follow the [glossary](../glossary.md).

## Detail level

The user chooses the detail level of the collection.

- By default, a location entry has only these fields: the Oracle card, the quantity, the location, the language and, optionally, the printing.
- The **advanced collection** setting adds optional fields: foil, condition, purchase price, entry date and notes.
  The user enables each optional field separately.
  Disabled fields disappear from the interface, and Reliquary never requires them.

## Locations

A location has two levels at most.
The first level is the location: a rack, a binder, a storage box, an assembled deck or a cube.
The second level is an optional section, for example a box in a rack.
Reliquary has no level for a page or a pocket.

- The user sees the collection **by location**.
  The user opens a rack and sees its list of cards.
- The global view combines all the locations and automatically shows the duplicates.
  Example: "3 Sol Ring: rack 1, rack 2, Atraxa deck."
- Each location has two independent flags: **locked** and **not pickable**.
  The [deck and collection rules](./overview.md#deck-and-collection-rules) explain the two flags.
- Optional storage rules suggest where to store a new card.
  Example: "This rack is for the blue cards."

## Movements and inventories

- Reliquary keeps movements simple on purpose: the user can add, remove and move cards.
- An **inventory** freezes the state of a location on a date.
  When the user records a new inventory of the location, Reliquary shows the differences.
  For each card, Reliquary asks for the origin or the destination of the card.
  A group option lets the user give one answer for several cards.
- The user can see the history: the state of a location on the date of each inventory.

## Sharing

- A user can share a collection with other accounts, with read, difference or edit rights.
  The [data model](../architecture/data-model.md) includes collection sharing, but the project delivers the feature in a later [milestone](../roadmap.md).
- A friend can then include the shared cards when they calculate their differences.

## Import and export

- Reliquary imports and exports decks and collections.
  The decks can be assembled decks or not.
- The formats are text, CSV, `.dek`, MTGA, MTGO, and the formats of Moxfield, Archidekt, Deckbox, ManaBox and Dragon Shield.
- Plugins can add new formats, for example a detailed JSON format with printings.

## Milestones

The project delivers the features of this file in these [milestones](../roadmap.md).

| Milestone | Features |
| --- | --- |
| M2 (Versions and collection) | Detail level, locations, location entries, movements, inventories, import and export |
| M4 (Plugins) | Import and export formats from plugins |
| Later | Collection sharing. The data model of M2 keeps room for it. |
