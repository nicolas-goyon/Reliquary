# Auto-tag

This page tells how auto-tag suggests the categories of the deck lines.
The [specification](../specification/deck-building.md#auto-tag) gives the feature.
[ADR 0031](../decisions/0031-auto-tag.md) gives the reasons for the design and the map from the Oracle tags to the categories.
Terms follow the [glossary](../glossary.md).

## Endpoints

All the paths start with `/api/v1`.
Only the owner of the deck can use these endpoints.
The other users get the error `not_found`.

| Endpoint | Effect |
| --- | --- |
| `GET /decks/{id}/auto-tag` | Suggests the categories of each line of the deck. The lines of the tokens zone and the extra cards get no suggestion. |
| `POST /decks/{id}/auto-tag` | Sets the categories of the given lines in one change. The deck gets one new version. |

The response of `GET` contains `deckId`, `version` and one item in `lines` for each line:

| Member | Content |
| --- | --- |
| `lineId`, `oracleId`, `name`, `zone` | The line and its card. |
| `categories` | The current categories of the line. |
| `suggested` | The suggested categories, in the order of the analysis. |
| `source` | The main source of the suggestion: `user_rule`, `oracle_tag`, `text_rule` or `none`. |
| `reasons` | One item `{ category, source, detail }` for each suggested category. `detail` is the slug of the Oracle tag, or the code of the text rule. |
| `changes` | Whether the suggested categories differ from the current categories. |

The body of `POST` contains `lines`, a list of `{ lineId, categories }`, and an optional `expectedVersion`.
The response is the deck, as `GET /decks/{id}` gives it.
The API refuses the change with the status 409 and the code `deck_version_conflict` when the deck has another version.

## Order of priority

`AutoTagger` in `src/Components/Reliquary.Decks/AutoTag` suggests the categories of a card:

1. When the user has a category rule for the card, auto-tag suggests the categories of the rule.
   An empty rule gives no category.
2. Otherwise, auto-tag uses the map of the Oracle tags.
   A tag gives a category when the tag or one of its ancestors is in the map.
3. When the tags give no category, auto-tag uses the text rules.

A card with a land as its first face also gets `land`, with the code `land_type`.
A land never gets `ramp`, because a deck counts its lands apart from its ramp.

## Category rules

The table `content.category_rules` keeps one rule for each user and Oracle card.
These changes write a rule:

- A change of the categories of a line with `PATCH /decks/{id}/lines/{lineId}`.
  A change of the quantity, the zone or the note only does not write a rule.
- A choice in `POST /decks/{id}/auto-tag` that differs from the automatic suggestion.
  The automatic suggestion is the suggestion with no category rule.

A choice in `POST /decks/{id}/auto-tag` that is equal to the automatic suggestion deletes the rule of the card.

## Text rules

The text rules read the text of all the faces, with no reminder text.
The ramp rules ignore the land faces, so a modal land is not a ramp card.

| Code | Category | Example of a text that the rule finds |
| --- | --- | --- |
| `mana_ability` | `ramp` | "{T}: Add {C}{C}." |
| `land_to_battlefield` | `ramp` | "Search your library for a basic land card, put it onto the battlefield tapped" |
| `extra_land` | `ramp` | "You may play an additional land on each of your turns." |
| `treasure` | `ramp` | "create a Treasure token" |
| `draw_cards` | `card_draw` | "Draw two cards." The draws of an opponent do not count. |
| `impulse_draw` | `card_draw` | "Exile the top three cards of your library. You may play them this turn." |
| `destroy_or_exile_target` | `targeted_interaction` | "Destroy target creature." A card in a graveyard does not count. |
| `counter_target` | `targeted_interaction` | "Counter target noncreature spell." |
| `bounce_target` | `targeted_interaction` | "Return target nonland permanent to its owner's hand." |
| `damage_target` | `targeted_interaction` | "deals 3 damage to any target" |
| `shrink_target` | `targeted_interaction` | "Target creature gets -3/-3 until end of turn." |
| `tuck_target` | `targeted_interaction` | "The owner of target permanent shuffles it into their library." |
| `edict` | `targeted_interaction` | "Target player sacrifices a creature." |
| `destroy_all` | `board_wipe` | "Destroy all creatures." |
| `shrink_all` | `board_wipe` | "All creatures get -X/-X until end of turn." |
| `damage_all` | `board_wipe` | "deals 13 damage to each creature" |
| `bounce_all` | `board_wipe` | "Return all nonland permanents to their owners' hands." |
| `keyword_to_yours` | `protection` | "Permanents you control gain hexproof and indestructible" |
| `phase_out` | `protection` | "All permanents you control phase out." |
| `silence` | `protection` | "your opponents can't cast spells" |
| `redirect` | `protection` | "choose new targets for target spell or ability" |
| `win_game` | `win_condition` | "you win the game", "each opponent loses the game" |
| `drain_x` | `win_condition` | "Each opponent loses X life." |
| `overrun` | `win_condition` | "creatures you control gain trample and get +X/+X" |

The text rules give no `synergy`, because synergy depends on the deck.

## Acceptance test

The folder `tests/Reliquary.Decks.Tests/Fixtures/AutoTag` contains:

- `cards.jsonl` and `oracle_tags.jsonl`: an extract of `data/demo` in the format of the Scryfall bulk files, for the cards of the reference decks.
- `reference-decks.json`: 10 reference decks with the expected categories of each card.

The test `AutoTagAcceptanceTests` fails when auto-tag is correct for less than 80 % of the reference cards.
It also writes the result of each deck and each card that is not correct.

## Limits

- The text rules use patterns, not a full grammar of the rules text.
  They can miss a card with an unusual text, or find a card by mistake.
- The map of the Oracle tags and the text rules are data in code.
  A change needs a test and a release.
