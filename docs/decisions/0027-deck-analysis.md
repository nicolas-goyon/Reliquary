# 0027. Deck analysis on the server, with rules and tables as data in code

- Status: Accepted
- Date: 2026-10-27
- Deciders: @nicolas-goyon

## Context

Milestone M1 delivers the deck analysis of the [specification](../specification/deck-analysis.md):

- the mana curve and the distributions ([#100](https://github.com/nicolas-goyon/reliquary/issues/100))
- the mana sources and the draw probabilities ([#101](https://github.com/nicolas-goyon/reliquary/issues/101))
- the needs of a Commander deck ([#102](https://github.com/nicolas-goyon/reliquary/issues/102))
- an indicative bracket ([#104](https://github.com/nicolas-goyon/reliquary/issues/104))

The analysis of a deck of 100 cards must take less than 100 ms on the server.

The analysis needs these choices:

- Where the analysis runs: on the server, or in the web app.
- Where the tables and the targets come from: published sources, data in the database, or data in code.
- How the analysis finds the mass land denial and extra turn cards. The card data has no flag for them.
- How the web app shows the results in French and in English.

An analysis in the web app only gives no result to plugins, scripts and third-party clients.
Tables in the database need migrations and an administration interface, but the tables change rarely.

## Decision

- The Decks component analyzes the deck on the server, in the endpoint `GET /api/v1/decks/{id}/analysis`.
  The class `DeckAnalyzer` is a pure function of the deck lines and the card facts of `ICardCatalog`. It has no input or output operations.
- The tables and the targets are data in code, with their source in a comment and in the [deck analysis page](../architecture/deck-analysis.md):
  - the colored source tables of Frank Karsten (2022) for 60-card and 99-card decks
  - the land formula of Frank Karsten for 99-card decks (2022)
  - the category targets of the template of The Command Zone, episode 658 (2025)
  - the rules of the Commander brackets of Wizards of the Coast (February 2025, October 2025 and February 2026)
- The ramp target follows the average mana value: 10 + 2 × (average mana value − 3), from 8 to 14. Reliquary chose this formula.
- Each land and each modal land is in the category `land` for the analysis, also when its line has no category. The user does not need to tag the lands.
- The Game Changers come from the flag of the card data, so the list follows Scryfall without a change of code.
- Rules on the card text find the mass land denial and extra turn cards. The tests cover each rule.
- The bracket is the lowest bracket that the deck fits. The analysis never gives bracket 5, because bracket 5 is an intention of the player.
- The analysis does not check two-card combos until [#103](https://github.com/nicolas-goyon/reliquary/issues/103). The result tells it with the code `two_card_combos` in `unchecked`.
- The response gives stable codes in `snake_case` for the statuses, the checkpoints and the reasons. The web app translates the codes.
- The analysis does not keep its results. Each request calculates the analysis again.

## Consequences

- Plugins, scripts and the web app get the same results from the public API.
- The engine takes less than 1 ms for 100 cards. The query of the card facts takes most of the time of the request.
- A change of a published table, of a target or of the bracket rules needs a change of code, a test and a release.
- The text rules can miss a card with an unusual text, or find a card by mistake. The bracket is indicative.
- The web app must add a translation for each new code.
- The tests of the analysis cover each layout of [ADR 0025](0025-multi-face-analysis-rules.md), as [#36](https://github.com/nicolas-goyon/reliquary/issues/36) asks.
