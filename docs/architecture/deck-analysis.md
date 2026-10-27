# Deck analysis

This page tells what the deck analysis calculates, with the formulas, the tables and their sources.
The [specification](../specification/deck-analysis.md) gives the features.
[ADR 0025](../decisions/0025-multi-face-analysis-rules.md) gives the rules for the cards with more than one face.
[ADR 0027](../decisions/0027-deck-analysis.md) gives the reasons for the design.
Terms follow the [glossary](../glossary.md).

## Endpoint

`GET /api/v1/decks/{id}/analysis` returns the analysis of the deck.
The access rules are the rules of `GET /api/v1/decks/{id}`.
The owner gets each deck, and the other users and the visitors get the unlisted and public decks.
The response uses stable codes in `snake_case`, so that the web app can translate them.

The class `DeckAnalyzer` in `src/Components/Reliquary.Decks/Analysis` calculates the analysis.
It has no input or output operations: it reads the deck lines and the card facts of `ICardCatalog` only.
For a deck of 100 cards, the engine takes less than 1 ms on a Release build.
The query of the card facts takes most of the time of the request.

## Scope

The analysis reads the commander, companion and main zones.
The sideboard, considering and tokens zones are outside the analysis.
Tokens, emblems, meld results and the other extra cards are outside the analysis ([ADR 0025](../decisions/0025-multi-face-analysis-rules.md), rule 10).
A line with a card that the catalog does not know has no effect.

## Response

| Member | Content |
| --- | --- |
| `format` | The format of the deck. |
| `cardCount` | The number of cards in the three zones. |
| `landCount` | The number of lands, with the modal lands. |
| `modalLandCount` | The number of modal lands. |
| `spellCount` | The number of cards that are not lands. The modal lands are in this count too. |
| `averageManaValue` | The average mana value of the cards that are not lands, with two decimals. |
| `manaCurve` | A list of `{ manaValue, count }` from 0 to 7. The point 7 also counts the cards with a higher mana value. |
| `types` | A list of `{ key, count }` for each card type, for example `creature`. |
| `colors` | A list of `{ key, count }` for `W`, `U`, `B`, `R`, `G` and `C` (colorless). |
| `categories` | A list of `{ key, count }` for each category, and `uncategorized`. |
| `mana` | The mana sources and the requirements of each color. See [Mana sources](#mana-sources). |
| `draws` | The draw probabilities. See [Draw probabilities](#draw-probabilities). |
| `needs` | The category needs of a Commander deck, or `null`. See [Commander needs](#commander-needs). |
| `bracket` | The indicative bracket of a Commander deck, or `null`. See [Bracket](#bracket). |

The Commander formats are `commander`, `duel`, `predh`, `paupercommander` and `brawl`.
These formats have 100 cards, with the commander.
The other formats get no `needs` and no `bracket`.

## Curve and distributions

The analysis applies the rules of [ADR 0025](../decisions/0025-multi-face-analysis-rules.md):

- The mana curve uses the mana value of the primary face. For a split card or a Room, it is the half with the lowest mana value.
- A land is not in the mana curve.
- A modal land counts as a land, and it counts in the mana curve with its spell face.
- The type distribution counts each card one time for each card type of its castable faces.
- The color distribution counts the cards that are not lands, one time for each color of the castable faces. A card with no color counts as `C`.
- The average mana value uses the same values as the mana curve.

The category distribution counts each card one time for each category of its line.
Each land and each modal land is in the category `land`, also when its line has no category.
A user can also put a card that is not a land in the category `land`, for example a land search spell.
The key `uncategorized` counts the cards that are not lands and have no category.

## Mana sources

The analysis compares the mana sources of each color with the colored mana symbols of the cards.
It uses the method of Frank Karsten, from the article [How Many Sources Do You Need to Consistently Cast Your Spells? A 2022 Update](https://www.tcgplayer.com/content/article/How-Many-Sources-Do-You-Need-to-Consistently-Cast-Your-Spells-A-2022-Update/dc23a7d2-0a16-4c0b-ad36-586fcca03ad8/) (TCGplayer, 2 August 2022).

Frank Karsten defines "on curve" like this.
A deck can cast a card with mana value M and N symbols of a color on curve when one condition is true.
The probability to draw N sources of that color by turn M, on the play, must be (89 + M) percent or more.
The probability is conditional: the player draws M lands by turn M.
The model includes the London mulligan, and for Commander the free mulligan and the draw on turn 1.

### Tables

The tables give the number of sources of one color that a deck needs.
"C" is one symbol of the color, and the number before it is the generic mana.
The 99-card table applies to the Commander formats, and the 60-card table applies to the other formats.

| Mana cost | Example | 60-card deck | 99-card deck |
| --- | --- | --- | --- |
| `C` | Monastery Swiftspear | 14 | 19 |
| `1C` | Ledger Shredder | 13 | 19 |
| `2C` | Reckless Stormseeker | 12 | 18 |
| `3C` | Collected Company | 10 | 16 |
| `4C` | Doubling Season | 9 | 15 |
| `5C` | Drowner of Hope | 9 | 14 |
| `CC` | Lord of Atlantis | 21 | 30 |
| `1CC` | Narset, Parter of Veils | 18 | 28 |
| `2CC` | Wrath of God | 16 | 26 |
| `3CC` | Baneslayer Angel | 15 | 23 |
| `4CC` | Primeval Titan | 13 | 22 |
| `5CC` | Hullbreaker Horror | 12 | 20 |
| `CCC` | Goblin Chainwhirler | 23 | 36 |
| `1CCC` | Cryptic Command | 21 | 33 |
| `2CCC` | Garruk, Primal Hunter | 19 | 30 |
| `3CCC` | Massacre Wurm | 17 | 28 |
| `4CCC` | Nyxbloom Ancient | 16 | 26 |
| `CCCC` | Dawn Elemental | 24 | 39 |
| `1CCCC` | Unnatural Growth | 22 | 36 |

A card outside the table uses the nearest cell:

- A higher mana value uses the last column of its row. For example, `8C` uses `5C`.
- Five symbols or more use the row of four symbols, at the column of the same generic mana. For example, `CCCCC` uses `CCCC`.

### Symbols

The analysis reads the mana cost of the primary face of each card that is not a land, in the three zones.

- `{W}`, `{U}`, `{B}`, `{R}` and `{G}` are symbols of one color.
- `{C}` needs colorless mana. Frank Karsten treats colorless mana as a color, so the analysis does the same.
- A hybrid symbol, for example `{W/U}`, does not count. Frank Karsten counts the sources of either color for a hybrid card. The analysis does not check these combined sources.
- A Phyrexian symbol, for example `{W/P}`, does not count, because the player can pay life.
- A symbol such as `{2/W}` does not count, because the player can pay generic mana.
- `{X}`, generic mana and snow mana do not count.

Frank Karsten adds one source to each color of a gold card, because the player needs all the colors at the same time.
The analysis adds one source to each color of a card with symbols of two colors or more.
It does not check the combined sources of the colors of a gold card.

### Sources

The sources are the cards of the main zone, because the player draws them.
The fact `ProducedMana` of `CardFacts` gives the colors that a card produces.

| Source | Value | For the cards with a mana value of |
| --- | --- | --- |
| Land or modal land | 1 | All |
| Creature that produces mana | 0.5 | More than the mana value of the creature |
| Other permanent that produces mana, for example a mana rock | 0.75 | More than the mana value of the permanent |
| Instant or sorcery, for example a ritual | 0 | — |

The values come from the same article of Frank Karsten.
He counts a mana creature as half a source for the cards with a mana value of 2 or more.
He counts a mana rock as three-fourths of a source for the cards with a mana value of 3 or more.
The analysis applies the general rule: a permanent helps with the cards that cost more than the permanent.

Some lands and ramp spells search the library for a land.
The card data does not give the colors of the lands that they can find, so these cards are not sources in the analysis.
Frank Karsten counts a non-mythic modal land as 0.8 source.
The card facts do not give the rarity, so a modal land counts as 1 source.

### Result of a color

The member `mana.colors` gives one item for each color that a card needs or that a source produces.

| Member | Content |
| --- | --- |
| `color` | `W`, `U`, `B`, `R`, `G` or `C`. |
| `symbols` | The number of symbols of the color in the primary mana costs, multiplied by the quantities. |
| `landSources` | The number of lands and modal lands that produce the color. |
| `otherSources` | The number of other permanents that produce the color. |
| `sources` | The sources for the card of the requirement, with the values of the table above. |
| `required` | The number of sources that the card of the requirement needs. |
| `oracleId`, `pips`, `manaValue` | The card of the requirement, its number of symbols of the color and its mana value. |
| `status` | `ok`, `low` (fewer sources than required) or `no_requirement`. |

For each color, the card of the requirement is the card with the largest difference between `required` and `sources`.
The member `mana.tableDeckSize` gives the table: 99 or 60.

## Draw probabilities

The analysis calculates the probability to have at least 1 to 7 lands at each checkpoint.
It uses the hypergeometric distribution: draws without replacement from the library.

P(X = k) = C(K, k) × C(N − K, n − k) / C(N, n)

- N is the number of cards in the library: the main zone. The commander and the companion are not in the library.
- K is the number of lands in the library, with the modal lands.
- n is the number of cards that the player saw.
- C(a, b) is the number of combinations of b items from a items.

The class `Hypergeometric` calculates the exact fraction with integers of unlimited size, then divides one time.
The tests compare the results with exact fractions from an independent calculation.

| Code | Turn | Player | Cards seen |
| --- | --- | --- | --- |
| `opening_hand` | 1 | On the play | 7 |
| `turn_2_play` | 2 | On the play | 8 |
| `turn_2_draw` | 2 | On the draw | 9 |
| `turn_3_play` | 3 | On the play | 9 |
| `turn_3_draw` | 3 | On the draw | 10 |
| `turn_4_play` | 4 | On the play | 10 |
| `turn_4_draw` | 4 | On the draw | 11 |

The probabilities do not include mulligans.
In a multiplayer Commander game, the starting player also draws on turn 1, so "on the draw" applies to all the players.

## Commander needs

For the Commander formats, the member `needs` compares each category with a target.

| Category | Target | Lowest count `ok` | Highest count `ok` | Source |
| --- | --- | --- | --- | --- |
| `land` | Formula L | L − 2 | L + 2 | Frank Karsten, 99-card formula |
| `ramp` | Formula R | R − 2 | R + 5 | The Command Zone, adjusted to the mana value |
| `card_draw` | 12 | 10 | 18 | The Command Zone |
| `targeted_interaction` | 12 | 9 | 18 | The Command Zone |
| `board_wipe` | 6 | 3 | 9 | The Command Zone |
| `synergy` | — | — | — | No target |
| `win_condition` | — | — | — | No target |
| `protection` | — | — | — | No target |

The fixed targets come from the template of The Command Zone, episode 658.
The title of the episode is ["Commander Deckbuilding Template for the New Era"](https://edhrec.com/articles/the-command-zone-commander-deckbuilding-template-for-the-new-era-the-command-zone-658-mtg-edh-magic-gathering) (18 February 2025).
The template gives 38 lands, 10 ramp, 12 card advantage, 12 targeted disruption, 6 mass disruption and about 22 plan cards.
The plan cards of the template are the synergy and win condition cards of the deck, so the analysis gives them no target.

The land target L uses the formula of Frank Karsten for 99-card decks.
The source is [How Many Lands Do You Need in Your Deck? An Updated Analysis](https://www.tcgplayer.com/content/article/How-Many-Lands-Do-You-Need-in-Your-Deck-An-Updated-Analysis/cd1c1a24-d439-4a8e-b369-b936edb0b38a/) (TCGplayer, 29 July 2022):

L = 31.42 + 3.13 × average mana value − 0.28 × cheap ramp and card draw cards

- The average mana value is the value of the analysis.
- A cheap ramp or card draw card has the category `ramp` or `card_draw`. It is not a land, and its mana value is 2 or less.
- The analysis rounds L to the nearest integer.

For example, a deck with an average mana value of 3 and 10 cheap ramp and card draw cards gets 38 lands, as the template.
The formula of Frank Karsten counts a modal land as a part of a land.
The analysis counts it as a full land ([ADR 0025](../decisions/0025-multi-face-analysis-rules.md), rule 3).

The ramp target R adjusts the template value of 10 to the average mana value:

R = 10 + 2 × (average mana value − 3), rounded to the nearest integer, from 8 to 14

A deck with expensive cards needs more acceleration, and a deck with cheap cards needs less.
Reliquary chose this formula. No published source gives it.

Each need has these members:

| Member | Content |
| --- | --- |
| `category` | The category. |
| `count` | The number of cards in the category, as in the category distribution. |
| `target`, `min`, `max` | The target and the range with the status `ok`, or `null`. |
| `status` | `low`, `ok`, `high` or `no_target`. |
| `difference` | `count` − `target`. A negative difference gives the number of missing cards. |

## Bracket

For the Commander formats, the member `bracket` gives an indicative bracket: the lowest bracket that the deck fits.
The rules come from these official sources of Wizards of the Coast:

- [Introducing Commander Brackets Beta](https://magic.wizards.com/en/news/announcements/introducing-commander-brackets-beta) (11 February 2025)
- [Commander Brackets Beta Update](https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-october-21-2025) (21 October 2025). This update removed the limits on tutors.
- [Commander Brackets Beta Update](https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-february-9-2026) (9 February 2026). This update changed the Game Changers list only.

| Rule | Bracket 1 | Bracket 2 | Bracket 3 | Bracket 4 |
| --- | --- | --- | --- | --- |
| Game Changers | None | None | Up to 3 | No limit |
| Mass land denial | No | No | No | Yes |
| Extra turn cards | No | Low quantities | Low quantities | Yes |
| Two-card combos | No | No | No early-game combo | Yes |

Bracket 5 has the same card rules as bracket 4.
The difference is the intention of the player, so the analysis never gives bracket 5.

The analysis applies these rules to the distinct cards of the three zones:

| Code | Rule | Bracket |
| --- | --- | --- |
| `game_changers` | The card data marks the card as a Game Changer. 1 to 3 cards give bracket 3, and 4 or more give bracket 4. | 3 or 4 |
| `mass_land_denial` | The text of the card agrees with a rule below. | 4 |
| `extra_turns` | The text of the card says that a player takes one or more extra turns. 1 to 3 cards give bracket 2, and 4 or more give bracket 4. | 2 or 4 |

The official rules do not give a number for "low quantities".
The analysis accepts 3 extra turn cards or fewer.
It cannot find the extra turns that a deck chains or loops.

The official definition of mass land denial has three parts.
The cards destroy, exile or bounce other lands, keep lands tapped, or change the mana that lands produce.
The effect applies to four or more lands of each player, without a replacement.
The analysis finds these texts in all the faces of a card:

1. "Destroy all", "exile all" or "return all" with "lands" in the same sentence, for example Armageddon, Jokulhaups and Sunder. "Lands you control" does not count.
2. "Each player sacrifices" four or more, X, half or all lands, for example Wildfire.
3. "Lands don't untap", for example Back to Basics. Also "can't untap more than" one or two lands or permanents, for example Winter Orb.
4. "Nonbasic lands are" a basic land type, for example Blood Moon.

Each reason has a `code`, the `bracket` that it permits and the `oracleIds` of its cards.
The member `unchecked` gives the rules that the analysis does not check: `two_card_combos`.
Issue [#103](https://github.com/nicolas-goyon/reliquary/issues/103) adds the combos from Commander Spellbook.

## Limits

- The analysis does not include mulligans in the draw probabilities.
- Lands and spells that search the library for a land are not mana sources.
- Hybrid symbols and the combined sources of gold cards are not in the mana requirements.
- The text rules of the bracket can miss a card, or find a card with an unusual text.
- The bracket does not include two-card combos.
