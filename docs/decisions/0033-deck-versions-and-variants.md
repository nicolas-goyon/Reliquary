# 0033. Deck versions, variants and the assembled version

- Status: Accepted
- Date: 2026-10-28
- Deciders: @nicolas-goyon

## Context

Milestone M2 adds the deck versions, the variants, the merge of a variant and the assembled version ([specification](../specification/versions-and-variants.md)).
[ADR 0026](0026-deck-model.md) gives the M1 model: each deck has one **working list** in `content.deck_lines`, and each change increases the version number of the deck.
ADR 0026 also says that M2 must not change the endpoints of M1.
The deck page, the text mode, the table mode, the import, the analysis and the legality all work on the working list of one deck.

A variant needs its own list that the user changes like a deck.
There are two options:

- Add a variant ID to each deck line. Each service of the Decks component must then filter the lines by variant.
- Store a variant as a deck row that refers to its main deck. All the services of M1 work on a variant with no change.

## Decision

### Deck versions

- The table `content.deck_versions` contains the versions of a deck.
  A version has a number in its deck (1, 2, 3 and more), a message, one or two parent versions and a date.
  It also has a kind: `commit`, `merge`, `revert` or `restore`.
- The table `content.deck_version_lines` contains the frozen lines of a version.
  A frozen line has the Oracle card, the printing, the finish, the quantity, the zone, the note, the categories and the purchase preferences.
  It keeps the tags as names, with a flag for the global tags, because a user can rename or delete a tag later.
- A **commit** copies the working list into a new version. The parent is the head version of the deck.
- A database trigger refuses each change and each deletion of a version row or a version line.
  Only the deletion of the deck deletes its versions.
- A **restore** copies a version into the working list and commits the result as a new version of the kind `restore`.
  The history never loses a version.
- The difference between two lists compares the lines by Oracle card and zone.
  A change of printing or finish only is a separate kind of change, so the comparison can hide it.

### Variants

- A variant is a row of `content.decks` with two more columns.
  The column `variant_of` refers to the main deck. The column `base_version_id` refers to the version of the main deck where the variant starts.
- The creation of a variant copies the lines of the base version into the working list of the variant.
  Then it commits the first version of the variant.
- A variant has its own working list and its own versions. The endpoints of M1 work on a variant with its deck ID.
- A variant has the owner, the format and the visibility of its main deck. The deck list, the folders, the public decks and the search do not show the variants as separate decks.
- The deletion of a main deck deletes its variants.
- A **merge** copies the working list of the variant into the working list of the main deck.
  It commits a version of the kind `merge` with two parents: the head of the main deck and the head of the variant.
- A **revert** of a merge copies the first parent of the merge into the working list of the main deck.
  It commits a version of the kind `revert`. The merge version stays in the history.

### Assembled version

- The column `assembled_version_id` of `content.decks` refers to the assembled version. A deck with this column is an assembled deck.
- An assembled deck is a location of the collection ([ADR 0034](0034-collection-model.md)).
  The Decks component gives the cards of the assembled version to the Collection component through the interface `IAssembledDeckLocations` of the building blocks.
- A change of the assembled version changes the content of the location.
  The Collection component records the changes as movements from or to "nowhere", because only the user knows the physical origin of the cards.
  The transition plan ([#144](https://github.com/nicolas-goyon/reliquary/issues/144)) records real movements between locations.
- A variant cannot be an assembled deck. Its main deck can.

### Events

- The component writes the events `decks.deck_version.created` and `decks.deck.assembled` to the outbox of the shard.
- `decks.deck.created`, `decks.deck.updated` and `decks.deck.deleted` stay. Their payload gets the optional field `variantOf`, so the consumers can ignore the variants.

## Consequences

- All the features of M1 work on the variants with no change: text mode, table mode, analysis, legality, import and export.
- The queries of the deck list, the folders and the public decks must filter out the variants.
  The personal data export must group the variants under their main deck.
- The versions use space: each version copies all its lines. A deck of 100 lines with 200 versions has 20,000 version lines. The project accepts this for M2.
- A frozen line keeps the tag names. A version does not follow a later rename of a tag.
