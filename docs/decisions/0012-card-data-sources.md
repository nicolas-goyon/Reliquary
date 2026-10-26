# 0012. Card data sources and card images

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

Reliquary needs card data: Oracle cards, card faces, card names, printings and legalities.
It also needs external IDs for marketplaces, recommendations for Commander decks and combo detection.
Public sources give this data, and each source has its own terms.

## Decision

- [Scryfall](https://scryfall.com/docs/api) gives the daily bulk data: cards, printings, card faces, localized names and legalities.
  Auto-tag uses the `otag:` search of Scryfall.
- [MTGJSON](https://mtgjson.com/) gives external IDs (Cardmarket, TCGplayer and MTGO) and preconstructed decks.
- Reliquary follows the terms of Scryfall and MTGJSON.
- Reliquary shows the card images from the Scryfall URLs.
  Reliquary never stores copies of the images.
- [EDHREC](https://edhrec.com/) gives the Commander recommendations.
- [Commander Spellbook](https://commanderspellbook.com/) gives the combo detection.
- recommender.cards is an optional source.
  Reliquary uses it only if access to it is possible.
- A scheduled task synchronizes the data.
  The task keeps a version of each import, so that it can run an import again.

## Consequences

- The card images depend on the Scryfall servers.
- Scryfall Tagger has no official API, so auto-tag must use the `otag:` search of Scryfall.
  The feasibility of auto-tag is still open.
- Access to recommender.cards is also still open.
- The [data model](../architecture/data-model.md) gives the entities of the card data.
