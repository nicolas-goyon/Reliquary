# Versions and variants

This file describes deck versions, variants and the theorycraft features.
These features make Reliquary different from the existing tools.
Terms follow the [glossary](../glossary.md).

## Deck versions

- Each deck has a history of deck versions, like Git commits.
  Each deck version has a message, a date and the list of changes.
- The **assembled version** is the list of cards that are actually in the location of the deck.

## Variants

A deck can have several theoretical variants, for example a budget variant, a cEDH variant or an anti-aggro variant.
Each variant starts from one deck version.

## Comparison

- A user can compare any two lists: deck versions, variants or different decks.
- The comparison shows the added cards, the removed cards, and the effect on the curve and on the categories.
- By default, the comparison ignores the changes of printing.
  A separate panel shows these changes.

## Transition plan

The transition plan gives the steps from the assembled version to a variant:

- the cards to remove from the deck
- the cards to take from the collection, with the location of each card
- the cards to buy

## Merge

A user can merge a variant into the main version of the deck.
The user can then revert the merge.

## Milestones

The project delivers the features of this file in this [milestone](../roadmap.md).

| Milestone | Features |
| --- | --- |
| M2 (Versions and collection) | All the features of this file |
