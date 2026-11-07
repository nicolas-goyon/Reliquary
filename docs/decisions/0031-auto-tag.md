# 0031. Auto-tag with the imported Oracle tags, text rules and category rules

- Status: Accepted
- Date: 2026-10-28
- Deciders: @nicolas-goyon

## Context

Issue [#97](https://github.com/nicolas-goyon/reliquary/issues/97) asks auto-tag to suggest the categories of all the cards of a deck.
The categories are the categories of the analysis: `ramp`, `card_draw`, `targeted_interaction`, `board_wipe`, `synergy`, `land`, `win_condition` and `protection`.
The [specification](../specification/deck-building.md#auto-tag) asks for these parts:

- Auto-tag uses the Oracle tags of Scryfall Tagger (`otag:`).
- Rules on the card text complete the tags.
- The user accepts or changes each suggestion. Each change becomes a rule of that user.

The acceptance criterion is: auto-tag gives the correct categories to at least 80 % of the cards of 10 reference decks.

[ADR 0012](0012-card-data-sources.md) left the feasibility open, because Tagger has no official API.
The spike of [ADR 0020](0020-card-data-import.md) found the bulk file `oracle_tags`.
The import job `cards.import` already stores the tags, the links to the cards and the tree of the tags in the tables `reference.tags`, `reference.oracle_taggings` and `reference.tag_closure`.

We checked these facts again on 2026-10-27:

- The [search reference](https://scryfall.com/docs/syntax) of Scryfall tells that the data of `otag:`, `function:` and `oracletag:` comes from the Tagger project.
- The [bulk data page](https://scryfall.com/docs/api/bulk-data) lists five files: Oracle cards, unique artwork, default cards, all cards and rulings.
  It does not list the tag files.
  The bulk data API gives `oracle_tags` and `art_tags` (ADR 0020), and client libraries know these two types, for example [scruffy](https://scruffy.hexdocs.pm/scruffy/bulk_data.html).
- The [API terms](https://scryfall.com/docs/api) ask for a delay of 50 to 100 ms between requests and recommend the bulk files.
  They forbid a paywall on Scryfall data and new games made from the data.
- The Tagger data has a tree: for example, `mana-rock` is below `mana-producer`, and `mana-producer` is below `ramp`.
  Each link between a tag and a card has a weight: `weak`, `median`, `strong` or `very_strong`.

We compared three ways to get the tags:

| Option | Advantages | Disadvantages |
| --- | --- | --- |
| A request to the `otag:` search for each suggestion | Always the current data | One request for each tag. The rate limit makes a deck of 100 cards slow. An instance with no internet access gets no suggestion. |
| A curated file that maps Oracle IDs to categories | No dependency on Tagger | A person must update the file for each new set. The file copies Tagger data by hand. |
| The tags that the import already stores, and a small map from tags to categories | No new request to Scryfall. The data follows Tagger every 12 hours. The map is short and changes rarely. | Scryfall does not document the tag files on its public page. |

The Cards component owns the reference data, and the Decks component owns the categories ([ADR 0007](0007-modular-monolith.md)).

## Decision

- Auto-tag uses the Oracle tags that the import job stores ([ADR 0020](0020-card-data-import.md)).
  Auto-tag sends no request to Scryfall.
- The Cards component gives the tags through the method `ICardCatalog.GetOracleTagsAsync` of the building blocks.
  The method gives each tag of a card with its weight and its ancestors in the tree (`CardOracleTag`).
- The Decks component keeps the map from the tags to the categories as data in code, in `AutoTagRules.OracleTags`.
  A card gets a category when it has the tag of the map, or a tag below it.
  The map uses only slugs that exist in the Tagger data of `data/demo`.

  | Oracle tag | Category | Examples of tags below it |
  | --- | --- | --- |
  | `ramp` | `ramp` | `mana-rock`, `mana-dork`, `land-ramp`, `ritual` |
  | `card-advantage` | `card_draw` | `draw`, `cantrip`, `draw-engine`, `impulsive-draw`, `loot` |
  | `spot-removal` | `targeted_interaction` | — |
  | `counterspell` | `targeted_interaction` | `counterspell-noncreature`, `counterspell-free` |
  | `removal` | `targeted_interaction`, except for a board wipe | `removal-creature`, `removal-exile`, `burn-creature` |
  | `sweeper` | `board_wipe` | `sweeper-one-sided` |
  | `protection` | `protection` | `gives-hexproof`, `gives-indestructible`, `protects-all` |
  | `overrun` | `win_condition` | — |
  | `group-slug` | `win_condition` | — |
  | `sacrifice-outlet-creature`, `blood-artist-ability`, `counter-increaser`, `token-increaser` | `synergy` | `counter-doubler`, `token-doubler` |

- The tag `removal` is also above `sweeper`.
  So a board wipe gets `targeted_interaction` only from a specific tag, for example `spot-removal`.
- Auto-tag ignores the links with the weight `weak`.
- Rules on the card text apply to a card when its tags give no category.
  For example, Tagger does not know a new card yet.
  The rules are regular expressions in `AutoTagRules.TextRules`, with a stable code each.
  [Auto-tag](../architecture/auto-tag.md) lists them.
  The rules read the text of all the faces ([ADR 0025](0025-multi-face-analysis-rules.md), rule 9), with no reminder text.
  The ramp rules do not read the land faces.
- A card with a land as its first face gets the category `land` and no `ramp`.
- A **category rule** is the choice of categories of one user for one Oracle card.
  The table `content.category_rules` keeps the rules in the shard of the user.
- Auto-tag applies this order of priority:
  1. The category rule of the user for the card.
  2. The categories of the Oracle tags.
  3. The text rules.
- These changes write a category rule:
  - A change of the categories of a line with `PATCH /api/v1/decks/{id}/lines/{lineId}`.
  - A choice in `POST /api/v1/decks/{id}/auto-tag` that differs from the automatic suggestion.
    A choice that is equal to the automatic suggestion deletes the rule of the card.
- `GET /api/v1/decks/{id}/auto-tag` gives the suggestion of each line, with the reason of each category: `user_rule`, `oracle_tag` with the tag, or `text_rule` with the code of the rule.
  `POST /api/v1/decks/{id}/auto-tag` applies the accepted categories in one change, so the deck gets one new version.
  Only the owner of the deck can use the two endpoints.
- `AutoTagger` is a pure function of the card facts, the tags and the rule of the user, like `DeckAnalyzer` ([ADR 0027](0027-deck-analysis.md)).
- The acceptance test reads 10 reference decks of 16 to 19 cards in `tests/Reliquary.Decks.Tests/Fixtures/AutoTag`.
  A person wrote the expected categories of each card before the rules existed.
  A suggestion is correct when it contains each expected category, and no category that the fixture does not accept for the card.

## Consequences

- On 2026-10-28, auto-tag is correct for 162 of the 172 reference cards (94.2 %).
  The text rules alone are correct for 151 of the 172 cards (87.8 %).
- We wrote the map while we looked at the tags of the reference cards.
  The result on other decks can be lower.
  The deck with the most synergy cards has the lowest result: 11 of 16 cards.
- The category `synergy` depends on the deck.
  Auto-tag suggests it only for a few engine tags, so the category rules of the user give most of the synergy cards.
- Auto-tag works with local data only, also on an instance with no internet access.
  The suggestions follow the Tagger data of the last import.
- The import changes the tags in one transaction.
  If the import of the tag file fails, the tables keep the tags of the last successful import.
  If Scryfall stops the tag files, the text rules still give a suggestion for each card.
- A change of the map or of a text rule needs a change of code, a test and a release.
- A category rule applies to all the decks of the user.
  A card with two roles in two decks gets the same suggestion, and the user changes it in one of the decks.
- This record answers the open question of feasibility of [ADR 0012](0012-card-data-sources.md).
