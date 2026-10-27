# 0030. Combos from the bulk file of Commander Spellbook

- Status: Accepted
- Date: 2026-10-28
- Deciders: @nicolas-goyon

## Context

[ADR 0012](0012-card-data-sources.md) chooses [Commander Spellbook](https://commanderspellbook.com/) for the combo detection.
Issue [#103](https://github.com/nicolas-goyon/reliquary/issues/103) asks for these features:

- The deck analysis finds the complete combos of a deck, and the combos that need one more card.
- The indicative bracket uses the two-card combos.
  [ADR 0027](0027-deck-analysis.md) keeps the code `two_card_combos` in `unchecked` until this issue.

Commander Spellbook calls a combo a "variant".
Reliquary uses the word "combo", because a variant is a different thing in the [glossary](../glossary.md).

### Facts about the source

The documentation of the backend, [docs/api.md](https://github.com/SpaceCowMedia/commander-spellbook-backend/blob/master/docs/api.md), gives these facts:

- Commander Spellbook publishes all its combos in one JSON file: `https://json.commanderspellbook.com/variants.json.gz` (also without `.gz`).
  The file has the members `timestamp`, `version`, `variants` and `aliases`.
  Each item of `variants` has the format of the endpoint `GET /variants/` of the API.
- The documentation says: "Do not use the HTTP API to export the whole dataset."
  It asks the clients to "Fetch it on a schedule of your own and read it locally".
- The paginated API has rate limits. The documentation gives 80 requests per minute as a safe rate, and asks each client to name its service in the `User-Agent` header.
- The documentation asks: "Please credit us and link back to commanderspellbook.com where applicable."

The source code of the backend and of the site has the MIT license.
The site says that Commander Spellbook is unofficial Fan Content under the Fan Content Policy of Wizards of the Coast.
The data has no other license.
The production job of Commander Spellbook writes the file every 2 hours (`kubernetes/app/jobs/prod/kubernetes.yaml` of the backend).

A check of the file on 2026-10-27 gave these results:

- The compressed file is 30.9 MB. It contains 116,378 combos with the status `OK` and 5,856 aliases.
- Each card has the Scryfall Oracle ID in `uses[].card.oracleId`. No card had an empty Oracle ID.
- 6,435 combos need a template, for example "Permanent Castable for {C}", in `requires`. A template is a group of cards, not one card.
- Each result (`produces[].feature`) has a name and a status: `S` (standalone), `C` (contextual) or `H` (helper).
- 4,022 combos use two cards and no template.

### Facts about the brackets

[Introducing Commander Brackets Beta](https://magic.wizards.com/en/news/announcements/introducing-commander-brackets-beta) (Wizards of the Coast, 11 February 2025) gives these rules for the two-card combos:

- Bracket 1: "No intentional two-card infinite combos, mass land denial, or extra-turn cards."
- Bracket 2: "No intentional two-card infinite combos or mass land denial."
- Bracket 3: "No intentional early-game two-card infinite combos."
  The text of bracket 3 describes these combos.
  They "can happen cheaply and in about the first six or so turns of the game".
- Bracket 4 has no limit other than the banned list.

The [update of 21 October 2025](https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-october-21-2025) removed the limits on tutors.
It adds an expectation for each bracket, for example "at least six turns before you win or lose" for bracket 3.
The [update of 9 February 2026](https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-february-9-2026) changed the Game Changers list only.
No update changed the rules of the combos.
The official texts do not define "early-game" with a number.

## Decision

### Source and schedule

- The import reads the bulk file `https://json.commanderspellbook.com/variants.json.gz`. It never reads the paginated API.
- The job `cards.import` imports the combos as its last step, `combos`, after the cards.
  It runs every 12 hours, with the other card data.
- The step skips the file when the header `Last-Modified` did not change since the last successful import.
  It runs also when the cards changed in the same run, or when an administrator forces the run.
- The setting `Cards:Import:SpellbookCombos` gives the address of the file. The setting `Cards:Import:IncludeCombos` disables the step.
- With `Cards:Import:Directory`, the step reads the local file `variants.json` or `variants.json.gz` and downloads nothing.
  The tests and the demo data use local files.

### Owner and storage

- The Cards component owns the combos, as reference data in the table `reference.combos` of the reference database.
- The other core components read the combos through the interface `IComboCatalog` of `Reliquary.BuildingBlocks.Cards`.
  The method `FindCombosAsync` gives each combo that uses at least one of the cards and needs one other distinct card at most.
- The import keeps a combo only when the two conditions are true:
  1. The combo needs no template.
  2. Each card is a known Oracle card that is not an extra card.
     The import finds the card by its Oracle ID. When the file gives no Oracle ID, it uses the Oracle name, if only one card has this name.
- The import does not keep the other combos, and the analysis does not check them. The log gives their number.
- Each row has the ID of Commander Spellbook and the Oracle IDs and quantities of the cards.
  It also has the names of the results, the mana needed and the popularity.
  The page of a combo is `https://commanderspellbook.com/combo/{id}/`.
- A combo is "game-ending" when a result is infinite or near-infinite, or when its status is `S`, for example "Win the game".
- The import writes the combos to a staging table with the binary COPY protocol.
  Then, in one transaction, it changes only the rows that changed.
- The import deletes the combos that the file does not contain, only when the file gives at least 90 % of the current combos.

### Failure mode

- When the download or the import of the combos fails, the import run records the error, and the old combos stay.
  The other steps of the job continue, and the job reports a failure at the end, as for the other sources.
- The instance "has combo data" when `reference.combos` has at least one row.
- Without combo data, the analysis gives `combos.available: false`, and the bracket keeps `two_card_combos` in `unchecked`.

### Analysis

- The analysis compares the combos with the commander, companion and main zones.
  A card counts when the deck has the quantity that the combo needs.
- A complete combo has all its cards in the deck. A near combo needs one more card.
  The response gives the 50 most popular near combos, and the number of all the near combos.
- A two-card combo uses two cards: two distinct cards, or two copies of one card.
- The mana value of a combo is the larger of two values.
  The first value is the mana value of the "mana needed" of Commander Spellbook.
  The second value is the sum of the mana values of the cards.
  The mana needed of Commander Spellbook can omit the cards that start on the battlefield.
- An early-game combo has a mana value of 6 or less. A player has about six mana on turn six. Reliquary chose this value.
- For the bracket, the analysis uses the complete two-card combos that are game-ending:
  - An early-game combo gives the reason `two_card_combos` and bracket 4.
  - Another combo gives the reason `late_two_card_combos` and bracket 3, because brackets 1 and 2 forbid all the two-card infinite combos.
- The web app shows the credit "Combos by Commander Spellbook" with a link to the site.
  Each combo has a link to its page on Commander Spellbook.

## Consequences

- The analysis finds the combos and checks the two-card combos with local data, with no request to Commander Spellbook.
- With all the cards of Scryfall, the import keeps about 110,000 combos. A test with these combos gave these results on a development computer:
  - The import takes about 13 seconds, and the table takes about 51 MB.
  - The combo query takes about 30 ms for a deck of 100 combo cards.
    It takes about 125 ms for the 100 cards with the most combos.
- The combos of the analysis are at most 12 hours older than the combos of Commander Spellbook.
- The analysis does not check the combos with a template.
  It ignores the condition "must be the commander" and the starting zones of the cards.
- The analysis cannot know if the player put a combo in the deck intentionally. The bracket is indicative.
- The value 6 for "early-game" is a choice of Reliquary. A change of the official rules needs a change of code.
- The names of the results are in English, because Commander Spellbook gives them in English only.
- The administrator of an instance must keep the credit to Commander Spellbook in the interface.
