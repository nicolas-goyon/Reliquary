# Card search

This file describes the card search of Reliquary: the endpoint, the query syntax, the rules of the search and the performance design.
Terms follow the [glossary](../glossary.md).
[ADR 0021](../decisions/0021-card-search.md) gives the reasons for the design.

The search accepts the syntax of [Scryfall](https://scryfall.com/docs/syntax).
A query that works on Scryfall finds the same cards in Reliquary, with the exceptions in [Differences from Scryfall](#differences-from-scryfall).

## Endpoint

`GET /api/v1/cards/search` searches the Oracle cards.

| Query parameter | Meaning |
| --- | --- |
| `q` | The query, for example `t:creature c:g mv<=2`. It is necessary. It has 1,000 characters or fewer. |
| `unique` | `cards` (default): one result for each Oracle card. `prints`: one result for each printing. `art`: one result for each illustration. |
| `order` | The sort order: `name` (default), `cmc`, `power`, `toughness`, `color`, `edhrec`, `penny`, `set`, `usd`, `eur`, `tix`, `rarity`, `released` or `artist`. |
| `dir` | `asc`, `desc` or `auto` (default). `auto` sorts the dates and the prices in descending order, and the other orders in ascending order. |
| `limit` | The maximum number of results in the page, from 1 to 100. The default is 50. |
| `cursor` | The `nextCursor` value of the previous page. |

The keywords `unique:`, `order:`, `dir:` and `prefer:` can also be in the query.
A keyword in the query wins over the query parameter with the same meaning.
A client can then keep a default in its parameters, and the user can change it in the query.

The response has this form:

```json
{
  "items": [
    {
      "card": {
        "id": "4457ed35-7c10-48c8-9776-456485fdf070",
        "name": "Lightning Bolt",
        "layout": "normal",
        "manaCost": "{R}",
        "manaValue": 1,
        "typeLine": "Instant",
        "oracleText": "Lightning Bolt deals 3 damage to any target.",
        "colors": ["R"],
        "colorIdentity": ["R"],
        "power": null,
        "toughness": null,
        "loyalty": null,
        "defense": null,
        "faces": [{ "name": "Lightning Bolt", "manaCost": "{R}", "...": "..." }]
      },
      "printing": {
        "id": "77c6fa74-5543-42ac-9ead-0e890b188e99",
        "setCode": "clu",
        "setName": "Ravnica: Clue Edition",
        "collectorNumber": "141",
        "lang": "en",
        "releasedAt": "2024-02-23",
        "rarity": "uncommon",
        "printedName": null,
        "imageUris": { "normal": "https://cards.scryfall.io/normal/front/..." },
        "faces": []
      }
    }
  ],
  "nextCursor": "eyJPZmZzZXQiOjUwLCJLZXkiOiI3RjNBIn0",
  "total": 1,
  "warnings": []
}
```

- `total` is the number of results on all the pages.
- `warnings` lists the terms that the search ignored. Each warning has a `code`, a `message`, a `position` and the `term`.
- The cursor contains the position of the next page and a key of the query. A cursor of another query gives the error `validation_failed` on the field `cursor`.

### Errors

| Code | Status | Meaning |
| --- | --- | --- |
| `search_syntax_error` | 400 | The query has a syntax error. The member `position` gives the index of the character, from 0. |
| `search_query_empty` | 400 | The query has no search condition, for example because the search ignored all its terms. The member `warnings` gives the ignored terms. |
| `search_timeout` | 400 | The search took more than 5 seconds. A regular expression can be too slow, for example. |
| `validation_failed` | 400 | A parameter is not valid, for example `unique=all` or `limit=500`. |

These are examples of syntax errors:

| Query | Position | Message |
| --- | --- | --- |
| `t:goblin (` | 9 | The opening parenthesis at position 9 has no closing parenthesis. |
| `o:"draw` | 2 | The quotation mark at position 2 has no closing quotation mark. |
| `t:` | 0 | The term "t:" at position 0 has no value. |
| `t:goblin or` | 9 | The word "or" at position 9 has no condition after it. |
| `o:/(abc/` | 2 | The regular expression at position 2 is not valid. |

### Warnings

The search ignores a term that it cannot use, and it continues with the other terms, as Scryfall does.

| Code | Meaning | Example |
| --- | --- | --- |
| `unknown_keyword` | The keyword is not known. | `foo:bar` |
| `invalid_value` | The value is not valid for the keyword. | `c:xyz`, `r:huge`, `f:chess`, `mv>abc` |
| `invalid_operator` | The keyword does not accept the operator. | `t>goblin` |
| `unsupported_keyword` | Reliquary knows the keyword, but it has no data for it. | `cube:vintage`, `is:scryfallpreview` |

## Query syntax

- Terms without `or` between them must all match. The word `and` is optional.
- `or` (or `OR`) gives alternatives. `and` binds more strongly than `or`: `t:fish or t:bird c:u` is `t:fish or (t:bird c:u)`.
- Parentheses group conditions: `t:legendary (t:goblin or t:elf)`.
- A minus sign negates a term or a group: `-c:red`, `-(t:goblin or t:elf)`.
  `not:x` is the same as `-is:x`.
- Quotation marks keep spaces and punctuation in a value: `o:"enters tapped"`.
  `\"` is a quotation mark in a quoted value.
- Slashes give a regular expression: `o:/^{T}:/`.
- A word without a keyword compares the card names: `bolt`.
- An exclamation mark gives an exact name: `!fire`, `!"Sift Through Sands"`.
- The comparison operators are `:`, `=`, `!=` (or `<>`), `<`, `<=`, `>` and `>=`.
- The keywords are not case-sensitive. The values are not case-sensitive.

## Supported keywords

The table follows the sections of the Scryfall syntax guide.
"Card" means that the condition uses the Oracle card.
"Printing" means that the condition uses one printing.

| Section | Keywords | Level | Examples | Notes |
| --- | --- | --- | --- | --- |
| Colors | `c:`, `color:`, `id:`, `identity:`, `ci:`, `commander:`, `has:indicator` | Card | `c:rg`, `color>=uw`, `id<=esper`, `c=2`, `c:m`, `id:c` | Letters, color names, guilds, shards, wedges, colleges and four-color names. `c:` means "at least", and `id:` means "at most". |
| Types | `t:`, `type:` | Card | `t:merfolk t:legend`, `t:/^legendary/` | A part of a word matches. |
| Text | `o:`, `oracle:`, `fo:`, `fulloracle:`, `kw:`, `keyword:` | Card | `o:"~ enters tapped"`, `fo:"draw a card"`, `kw:flying` | `o:` ignores the reminder text. `fo:` includes it. `~` is the name of the card or a word such as "this creature". |
| Mana costs | `m:`, `mana:`, `mv:`, `manavalue:`, `cmc:`, `devotion:`, `produces:`, `is:hybrid`, `is:phyrexian` | Card | `m:2WW`, `m>3WU`, `m:{R/P}`, `mv=5`, `mv:even`, `devotion:{u/b}{u/b}{u/b}`, `produces=wu` | `m:` compares each face. Generic mana counts as an amount: `m:2WW` matches `{3}{W}{W}`. `is:phyrexian` also finds the Phyrexian symbols of the Oracle text. |
| Power, toughness, loyalty | `pow:`, `power:`, `tou:`, `toughness:`, `pt:`, `powtou:`, `loy:`, `loyalty:`, `def:`, `defense:` | Card | `pow>=8`, `pow>tou`, `pt>=10`, `loy=3` | `*` counts as 0. A comparison between two values uses all the faces. `pt:` uses the front face only, as Scryfall does. |
| Multi-faced cards | `is:split`, `is:flip`, `is:transform`, `is:tdfc`, `is:meld`, `is:meldpart`, `is:meldresult`, `is:leveler`, `is:dfc`, `is:mdfc`, `is:adventure` | Card | `is:meld`, `is:split` | — |
| Spells, permanents, effects | `is:spell`, `is:permanent`, `is:historic`, `is:party`, `is:outlaw`, `is:modal`, `is:vanilla`, `is:frenchvanilla`, `is:bear`, `is:manland` | Card | `c>=br is:spell f:duel`, `is:vanilla` | The rules use the type line and the Oracle text. |
| Extra and funny cards | `include:extras`, `is:funny` | Card | `power include:extras`, `t:scheme`, `is:funny` | See [Hidden cards](#hidden-cards). |
| Tagger tags | `otag:`, `function:`, `oracletag:`, `atag:`, `art:`, `arttag:` | Card, printing | `function:removal`, `art:squirrel` | A tag also finds the cards of its child tags. |
| Rarity | `r:`, `rarity:`, `in:` | Printing | `r:common`, `r>=r`, `in:rare` | Order: common, uncommon, rare, special, mythic, bonus. |
| Sets and blocks | `s:`, `e:`, `set:`, `edition:`, `cn:`, `number:`, `b:`, `block:`, `g:`, `group:`, `st:`, `in:`, `is:booster`, `is:<promo type>` | Printing | `e:war`, `cn>50`, `b:wwk`, `g:fin`, `st:masters`, `in:lea`, `is:prerelease` | `g:` uses the parent sets of Scryfall. |
| Cubes | `cube:` | — | `cube:vintage` | Not supported: Reliquary has no cube lists. The search ignores the term and gives a warning. |
| Format legality | `f:`, `format:`, `legal:`, `banned:`, `restricted:`, `is:commander`, `is:brawler`, `is:companion`, `is:duelcommander`, `is:oathbreaker`, `is:partner`, `is:gamechanger`, `is:reserved`, `edhrec:`, `edhrecrank:` | Card | `f:pauper`, `banned:legacy`, `is:commander`, `edhrecrank<=100` | `f:` includes the restricted cards. |
| Prices | `usd:`, `eur:`, `tix:`, `cheapest:` | Printing | `tix>15.00`, `usd>=0.50 e:ema`, `cheapest:usd` | `usd` uses the non-foil price, then the foil price, then the etched price. |
| Artist, flavor text, watermark | `a:`, `artist:`, `artists:`, `ft:`, `flavor:`, `wm:`, `watermark:`, `has:watermark`, `new:art`, `new:artist`, `new:flavor`, `illustrations:` | Printing | `a:"proce"`, `ft:mishra`, `wm:orzhov`, `new:art` | — |
| Border, frame, foil, resolution | `border:`, `frame:`, `stamp:`, `is:full`, `is:foil`, `is:nonfoil`, `is:etched`, `is:glossy`, `is:hires`, `is:new`, `is:old`, `is:colorshifted`, `is:universesbeyond`, `is:default`, `is:atypical`, `new:frame` | Printing | `border:white`, `frame:2003`, `is:foil e:c16`, `stamp:oval` | `frame:` accepts the frame editions and the frame effects. |
| Games, promos, spotlights | `game:`, `in:arena`, `is:digital`, `is:promo`, `is:spotlight`, `is:alchemy` | Printing | `game:arena`, `-in:mtgo`, `is:spotlight` | — |
| Year | `year:`, `date:` | Printing | `year<=1994`, `date>=2015-08-18`, `date>ori`, `date>now` | A set code gives the release date of the set. |
| Reprints | `is:reprint`, `not:reprint`, `is:unique`, `prints:`, `sets:`, `paperprints:`, `papersets:`, `new:rarity` | Printing, card | `e:c16 not:reprint`, `sets>=20`, `e:arn papersets=1` | The counts use the printings in the default language. |
| Languages | `lang:`, `language:`, `in:`, `new:language` | Printing | `lang:japanese`, `lang:any`, `in:ru`, `lang:ko new:language` | See [Languages](#languages). |
| Shortcuts | `is:dual`, `is:fetchland`, `is:shockland`, `is:checkland`, `is:fastland`, `is:slowland`, `is:painland`, `is:bounceland`, `is:gainland`, `is:scryland`, `is:surveilland`, `is:filterland`, `is:tangoland`, `is:canopyland`, `is:shadowland`, `is:storageland`, `is:triland`, `is:tricycleland`, `is:bikeland`, `is:bondland`, `is:pathway`, `is:creatureland`, `is:masterpiece` and their aliases | Card | `is:dual`, `is:fetchland` | Each shortcut is a rule on the type line and the Oracle text. |
| Negation | `-`, `not:` | — | `-fire c:r t:instant`, `not:reprint` | — |
| Regular expressions | `/…/` with `t:`, `o:`, `fo:`, `ft:`, `name:` and without a keyword | Card, printing | `t:creature o:/^{T}:/`, `o:/\spp/`, `name:/\bizzet\b/` | See [Regular expressions](#regular-expressions). |
| Exact names | `!word`, `!"words"` | Card | `!fire`, `!"Sift Through Sands"` | A face name also matches. |
| Or, nesting | `or`, `( )` | — | `t:land (a:titus or a:avon)` | — |
| Results | `unique:`, `order:`, `sort:`, `dir:`, `direction:`, `prefer:`, `display:`, `++`, `@@` | — | `unique:prints`, `order:rarity dir:asc`, `prefer:newest` | `display:` changes only the presentation of the Scryfall site, so the search accepts it and ignores it. |

The search also accepts `oracleid:` with an Oracle identifier.

## Rules of the search

### A card matches when one of its printings matches

Scryfall evaluates a query on each printing, then it keeps one result for each Oracle card.
Reliquary does the same: `e:war -e:war` finds no card, and `-e:lea` finds the cards with a printing outside Alpha.

The compiler gives each term a level:

- A card term uses only the Oracle card, for example `t:`, `o:`, `c:` or `f:`.
- A printing term uses the printing, for example `e:`, `r:`, `a:` or `usd:`.
- A term such as `in:lea` or `sets>=20` uses all the printings of the card. It is a card term.

When a query has only card terms, the search reads the table `oracle_cards` and does not read the printings.
When a query has a printing term, the card terms stay outside the subquery on the printings, and the other terms go into an `exists` subquery.

### Names

A word without a keyword matches a card when one of its names contains the word.
The names are all the names of the table `card_names`: the Oracle name, the face names, the localized names and the alternate names.
The comparison uses the normalization of `NameNormalizer`: no case, no accents, simple apostrophes and dashes.
So `foudre`, `Foudre` and `稲妻` find Lightning Bolt.

An exact name (`!fire`) matches a card when one of its names is equal to the text.

Scryfall compares only the English names when the query gives no language.
Reliquary compares all the names, because the specification asks for a search by localized name.

### Printing choice

In the mode `unique:cards`, each result shows one printing:

1. When the query has a printing term or a `prefer:` keyword, the search chooses the best printing among the printings that match.
2. Otherwise, the search shows the default printing of the card. The import chooses it.

The best printing follows the `prefer:` keyword first, then the rules of the default printing:
English, released, available on paper, in a regular set, with a normal collector number, a modern frame, no special treatment and a high-resolution image.
`prefer:` accepts `oldest`, `newest`, `usd-low`, `usd-high`, `eur-low`, `eur-high`, `tix-low`, `tix-high`, `promo`, `default`, `atypical`, `universesbeyond` (or `ub`) and `notuniversesbeyond` (or `notub`).

### Languages

When the query has no `lang:` keyword, the search uses the printings in the default language.
A printing is in the default language when it is in English, or when its set has no English printing with the same collector number.
These are the printings of the Scryfall file `default_cards`.
The import computes the column `printings.default_lang`.

`lang:fr` uses the French printings only. `lang:any` uses the printings in all the languages.

A localized name gives a language to the result:

1. The search takes the name terms of the query that are not negated and not inside `or`: the words, the exact names and `name:`.
2. When all these terms match a localized name of the card, and no English name of the card matches all of them, the rule applies.
   The result then shows a printing in the language of this name.
3. When names in more than one language match, the search uses the first language in the order of the language codes.

For example, `foudre` shows a French printing of Lightning Bolt, and `bolt` shows an English printing.
`lang:` disables this rule.

### Hidden cards

The search hides some cards and printings by default, as Scryfall does:

- The extra cards: the tokens, emblems, art cards, planes, schemes and vanguards, and the cards that exist only in token or memorabilia sets.
- The cards with a content warning of Scryfall, for example Invoke Prejudice.
- The cards that are legal in no format and have a funny printing or only digital printings.
  Examples are the playtest cards, the holiday cards, the Heroes of the Realm cards and the specialized variants of Alchemy.
  The cards of the Un-sets (Unglued, Unhinged, Unstable, Unsanctioned and Unfinity) and the cards with an acorn stamp stay visible.
- The printings of the memorabilia sets, for example the gold-bordered World Championship printings.
- The printings that exist only for the Sega Dreamcast game.

The import computes the columns `oracle_cards.is_extra`, `oracle_cards.is_funny` and `oracle_cards.search_hidden`.
A card of a set that is not released yet is legal in no format, but the search shows it.

These terms show the hidden cards and printings, when they are not negated:

- `include:extras`
- a type of extra card: `t:token`, `t:emblem`, `t:scheme`, `t:plane`, `t:phenomenon`, `t:vanguard`, `t:dungeon` and others
- a set: `e:`, `s:`, `set:`, `b:` and `g:`
- an artist: `a:`
- the keyword `name:`
- a ban or a restriction: `banned:`, `restricted:`, and `is:reserved`
- `is:token`, `is:emblem`, `is:extra`, `is:funny` and `is:dfc`

The parity tests check these rules against Scryfall.

### Colors and mana

- `c:` uses the colors of the card. A split card or an Adventure has the colors of its two halves.
  Scryfall gives no colors to a double-faced card, only to its faces, so one face of a double-faced card must match the condition.
- `c:rg` means at least red and green. `c=rg` means exactly red and green. `c<=rg` means only red, green or both.
- `id:` means "at most these colors", so that `id:esper` finds the cards for an Esper commander.
- A number compares the number of colors: `c=2`.
- `m:` compares the mana cost of each face. A face matches when its cost contains all the symbols of the query.
  Generic mana counts as an amount, so `m:2WW` matches `{2}{W}{W}` and `{3}{W}{W}`.
- `devotion:` counts the symbols of a permanent that contain one of the colors of the query.
- `produces:` uses the colors that the card can produce, with `c` for colorless mana.

### Text

- `o:` searches the Oracle text without the reminder text, as Scryfall does. `fo:` searches the full Oracle text.
- In `o:` and `fo:`, `~` is the name of the card or its short name before a comma.
  It is also a word such as "this creature" or "this land".
  The new Oracle texts use these words in place of the card name.
- `o:`, `fo:`, `ft:` and `a:` match a part of a word.
- A word in `t:` matches the start of a word of the type line: `t:legend` finds "Legendary", and `t:fish` does not find "Jellyfish".
  A card type or a supertype must be a full word: `t:plane` does not find "Planet" or "Planeswalker", and `t:land` does not find "Lander".
  A phrase in quotation marks matches a part of the type line: `t:"legendary creature"`.

### Regular expressions

The search converts each regular expression of the Scryfall flavor into a PostgreSQL regular expression:

- The comparison ignores the case.
- `.` does not match a new line. `^` and `$` match at the start and the end of each line.
- `\b` is a word boundary. `{T}` matches the tap symbol, because a brace that does not start a count is a normal character.
- `\sm` (a mana symbol), `\sc`, `\ss`, `\smr`, `\smh`, `\smp`, `\spt`, `\spp` and `\smm` follow the Scryfall extensions.
- `~` in `o:` and `fo:` is the name of the card.

A regular expression that PostgreSQL cannot use gives the error `search_syntax_error` with the position of its term.

## Differences from Scryfall

| Area | Reliquary | Scryfall |
| --- | --- | --- |
| Names | A word matches the localized names and the alternate names. | Only the English names, unless the query has `lang:`. |
| `cube:` | Not supported. The search ignores the term with a warning. | Searches its cube lists. |
| `is:scryfallpreview` | Not supported. The search ignores the term with a warning. | The cards that Scryfall previewed. |
| `order:spoiled`, `order:review`, `order:imageupdated` | `spoiled` is `released`. `review` is `name`. `imageupdated` is not valid. | Specific orders. |
| `date:` with an unknown set code | No card matches. | Ignores the term with a warning. |
| Prices | The prices of the last import. | The prices of the day. |
| Shortcuts such as `is:fetchland` | Rules on the type line and the Oracle text. A new card cycle may need a new rule. | Lists that Scryfall maintains. |
| `is:commander`, `is:funny`, `is:frenchvanilla`, `is:manland` | Rules on the card data. The parity tests check `is:commander` and `is:funny`. | Internal rules. |

## Performance design

Card search must answer in less than 150 ms on about 100,000 printings.
The [search benchmark](../development/search-benchmark.md) measures it.

- The compiler gives one SQL statement for each search: the page of results, the total and the card faces.
  The statement uses only parameters for the values of the query.
- The statement runs in a transaction with `statement_timeout = 5000` and `jit = off`.
  A slow regular expression stops after 5 seconds, and short queries do not pay the compilation time of the JIT.
- Card terms use the table `oracle_cards` (about 39,000 rows), not the printings (up to 550,000 rows).
- The search chooses the printing only for the cards of the page.
- The migration `0002_card_search.sql` adds these objects:

| Object | Use |
| --- | --- |
| `oracle_cards.oracle_text_plain` (generated) with a trigram index | `o:` without the reminder text |
| `oracle_cards.keywords_normalized` (generated) with a GIN index | `kw:` |
| `card_faces.mana_symbols` and `card_faces.mana_generic` (generated) | `m:` compares the costs as multisets with `@>` and `<@` |
| `printings.default_lang` and a partial index on `oracle_id` | The printings in the default language |
| `printings.story_spotlight` and `printings.content_warning` | `is:spotlight` and the hidden cards |
| `oracle_cards.is_funny` and `oracle_cards.search_hidden` | `is:funny` and the hidden cards |
| GIN index on `printings.games` | `game:` and `in:arena` |
| Trigram index on `lower(printings.flavor_text)` | `ft:` |
| Statistics on `lower(type_line)`, on its first face and on `lower(oracle_text_plain)` | Good estimates for `t:`, `o:` and `is:permanent` |
| Functions `stat_value`, `mana_symbols`, `mana_generic`, `remove_reminder_text` and `lower_array` | The generated columns and the comparisons of power and toughness |

The table `0001_cards.sql` already has trigram indexes on the type line, the Oracle text, the names and the artist, and a GIN index on the legalities.

- The import computes the columns that depend on the printings when it inserts the rows.
  A second update of all the rows would double the size of `oracle_cards` after a first import, and a search would read twice as many pages.
- The import analyzes the tables after each change, so the planner has fresh statistics.
- The colors use the array operators `@>` and `<@`, because PostgreSQL has statistics for them.
  A condition on a bit mask has no statistics, and the planner then chooses slow plans.
- The tag keywords compute the list of the tagged cards once, with `= any (array(...))`.

## Parity with Scryfall

The tests compare the results of Reliquary with the results of Scryfall on the same data.

1. `tools/search-parity/queries.json` lists the queries: the 104 examples of the Scryfall syntax guide, 2 examples from its text, and other queries of Reliquary.
2. `tools/search-parity/record_expectations.py` asks the Scryfall search API for the printings that match each query.
   It keeps the Oracle cards that have a matching printing in [`data/demo`](../../data/demo/README.md), and writes `tests/Reliquary.Cards.Tests/SearchParity/expectations.json`.
3. The test `SearchParityTests` imports `data/demo`, runs each query and compares the Oracle cards.

The demo dataset contains at most 3 English printings and 1 French printing of each card.
So a query about all the printings of a card, for example `in:lea` or `sets>=20`, cannot find the same cards as on Scryfall.
The file `queries.json` gives the reason of each known difference, and the test checks that the difference still exists.

### Results on the demo dataset

The script recorded the expectations from Scryfall on 2026-10-26.

| Queries | Same cards as Scryfall | Known differences |
| --- | --- | --- |
| Examples of the syntax guide | 94 of 104 (90 %) | 10 |
| Examples from the text of the guide | 2 of 2 | 0 |
| Other queries of Reliquary | 39 of 40 | 1 |
| All | 135 of 146 | 11 |

| Query | Reason of the difference |
| --- | --- |
| `cube:vintage` | Reliquary has no cube lists. The search ignores the keyword and gives a warning. |
| `in:rare -rarity:rare` | The keyword `in:rare` uses all the printings of a card. The demo dataset does not have all the printings. |
| `in:lea in:m15` | The keyword `in:` uses all the printings of a card. |
| `t:legendary -in:booster` | The keyword `in:booster` uses all the printings of a card. |
| `-in:mtgo f:legacy` | The keyword `in:mtgo` uses all the printings of a card. |
| `in:leb game:paper prefer:newest` | The keyword `in:leb` uses all the printings of a card. |
| `in:paper -in:arena t:legendary` | The keyword `in:arena` uses all the printings of a card. |
| `sets>=20` | The keyword `sets:` counts all the printings of a card. |
| `in:ru in:zhs` | The demo dataset has no Russian and no Chinese printings. |
| `new:art -new:artist st:masters game:paper` | The keyword `new:` compares a printing with all the earlier printings. |
| `new:flavor e:m15 is:reprint` | The keyword `new:` compares a printing with all the earlier printings. |

### Results on the full data

`compare_api.py` with the option `--full` compares the full Scryfall data with the live results of Scryfall.
On 2026-10-26, 86 of the 146 queries found the same cards.
The data file was some weeks older than the live Scryfall data, so most of the other queries differ only by new cards.
These differences stay:

- The search does not support `cube:`.
- Reliquary and Scryfall do not hide the same cards in some cases.
  Examples are a few holiday cards, the "My Little Pony" cards of Secret Lair, the dungeons of the memorabilia sets and the meld result Mechtitan.
- `is:dfc` does not find some art series cards, and `is:commander` and `is:painland` have small differences.
- Some printings have different `is:promo`, `is:hires`, `game:arena` and release date values.

## Code

| Path | Content |
| --- | --- |
| `src/Components/Reliquary.Cards/Search/SearchLexer.cs` | Splits the query into tokens |
| `src/Components/Reliquary.Cards/Search/SearchParser.cs` | The recursive-descent parser. It gives the syntax tree or a syntax error with its position. |
| `src/Components/Reliquary.Cards/Search/SearchCompiler.cs`, `SearchCompiler.Is.cs` | Changes the syntax tree into SQL conditions, parameters and warnings |
| `src/Components/Reliquary.Cards/Search/RegexTranslator.cs` | Changes a Scryfall regular expression into a PostgreSQL regular expression |
| `src/Components/Reliquary.Cards/Search/SearchSql.cs` | Builds the SQL statement of a page |
| `src/Components/Reliquary.Cards/Search/CardSearch.cs` | Runs the search and builds the response |
| `tests/Reliquary.Cards.Tests/Search` | The tests of the parser, the compiler, the search, the endpoint and the parity with Scryfall |
| `tools/search-parity` | The queries of the parity tests and the scripts that record and compare the results of Scryfall |
| `bench/Reliquary.Cards.Benchmark` | The search benchmark |
