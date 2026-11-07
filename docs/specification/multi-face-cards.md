# Multi-face cards

This file describes cards with more than one face and cards with more than one name.
These cards often cause problems in other tools.
Reliquary handles them in its [data model](../architecture/data-model.md) from the start.
Terms follow the [glossary](../glossary.md).

## Cards with several faces

Double-faced cards (transform and modal), flip cards, split cards and meld cards have several card faces.
These requirements apply to each card face:

- The user can see the details of the card face.
- The search can find the card face.
- Reliquary can show the card face.

## Cards with two parts

Some cards have two parts: Rooms, Adventures, prepared creatures and the cards of other mechanics with two halves.
Search and analysis must see the two parts: cost, types and text.

## Several names for one card

An Oracle card can have several card names: localized names, names of special printings and face names.
The names of special printings include Universes Beyond names and other alternate names.
The import accepts all these names and links each name to the same Oracle card.

## Analysis rules

The analysis rules must state these points explicitly:

- which cost the mana curve uses: the cost of the front face, or the Oracle value
- whether the analysis treats a modal card with a land face and a spell face as a land

## Milestones

The project delivers the features of this file in these [milestones](../roadmap.md).

| Milestone | Features |
| --- | --- |
| M0 (Foundations) | The data model and the search |
| M1 (Commander deck MVP) | The rules to show the card faces, and the analysis rules |
