# Deck building

This file describes how a user builds a deck in Reliquary.
It covers card search, deck editing, tags and categories, auto-tag, purchase preferences, assembled decks, duplication and visibility.
Terms follow the [glossary](../glossary.md).

## Card search

- Card search accepts the full Scryfall syntax, including `lang:fr`, `is:`, `otag:`, `art:`, `prefer:`, operators and regular expressions.
- A user can search by localized name, in French and in other languages.
- Visual filters and autocomplete help the users who do not know the syntax.
- Reliquary recommends cards for the context of each deck:
  - EDHREC gives the popular cards for the commander of the deck, and synergy information.
  - An optional source is [recommander.cards](https://recommander.cards), through its public API.
    Its terms allow personal, non-commercial use, and ask for a credit to the source.

## Editing

- A user adds cards from a search, with drag and drop, or with pasted text.
- A deck has these zones: commanders, companion, main deck, sideboard, considering and tokens.
  Other tools call the considering zone the maybeboard.
- **Bulk editing in text mode**: the full list is free text, and the user can change it as on Moxfield.
  The text syntax contains the quantities, the sets, the tags and the zones.
- **Bulk editing in table mode**: the user quickly selects many cards with a click, Shift+click or filters.
  Then the user applies one group action to the selection: language, set, tags, zone (for example sideboard or considering), add or remove.
- Reliquary checks the legality of a deck for each format, with Commander first.
  The check covers bans, color identity and the singleton rule.

## Tags and categories

- A user has **global tags** and **local tags**.
  The user defines a global tag once and can use it again in all their decks.
  A local tag exists in one deck only.
- The predefined categories are the categories that the [analysis](./deck-analysis.md) uses: ramp, card draw, targeted interaction, board wipe, synergy, land, win condition and protection.

## Auto-tag

Auto-tag suggests tags for all the cards.
It uses the Oracle tags of Scryfall Tagger (`otag:`), and rules on the card text complete these tags.
For example, the text "add one mana" gives the ramp tag.
The user accepts or corrects each suggested tag.
Each correction becomes a rule of that user.

The project must still check the feasibility of auto-tag.
Scryfall Tagger has no official API, so auto-tag will need to use the `otag:` search of Scryfall.

## Purchase preferences

For a deck that is not an assembled deck, each card can have purchase preferences:

- wanted printings, or printings to avoid
- languages
- minimum condition
- foil or not foil
- maximum price

Reliquary uses these criteria for the export to marketplaces (Cardmarket and others) and for the search in the collection.
If no card of the collection matches the criteria, Reliquary tells the user and suggests the closest match.
For example: "Not in French, but 1 English copy in rack 2."

## Assembled decks and tokens

- For each token of an assembled deck, the user can mark whether they own the token.
- The user chooses whether the purchase list includes the tokens or not.

## Duplication

- A user can duplicate a deck: their own deck or the deck of another user.
  In the same window, the user chooses the name, the visibility and the destination folder of the copy.
- The visibility of a deck is private, unlisted or public.
  People with the link can open an unlisted deck.

## Milestones

The project delivers the features of this file in these [milestones](../roadmap.md).

| Milestone | Features |
| --- | --- |
| M0 (Foundations) | Card search with the full Scryfall syntax and localized names (search API) |
| M1 (Commander deck MVP) | Visual filters, autocomplete, recommendations, editing, zones, bulk editing, legality checks, tags, categories, auto-tag, duplication and visibility |
| M2 (Versions and collection) | Purchase preferences, and assembled decks with tokens |
