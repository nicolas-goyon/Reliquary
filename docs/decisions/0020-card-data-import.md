# 0020. Import of the card data from the bulk files

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

[ADR 0012](0012-card-data-sources.md) chooses Scryfall and MTGJSON as the sources of the card data.
The spike [#26](https://github.com/nicolas-goyon/reliquary/issues/26) had two questions:

1. Can Reliquary get the Oracle tags of Scryfall Tagger, which has no official API?
2. Can Reliquary use recommander.cards as an optional source of recommendations?

The spike found these facts on 2026-10-26:

- The bulk data API of Scryfall lists the files `oracle_tags` (4,588 tags and 237,835 links to cards) and `art_tags` (11,730 tags and 500,496 links to illustrations).
  Each tag has its parent tags and its aliases.
- Scryfall now gives each bulk file in the JSON Lines format, compressed with gzip (`jsonl_download_uri`).
- The file `all_cards` contains 546,771 printings in all the languages, about 400 MB compressed.
  The file `default_cards` contains 119,207 printings, about 80 MB compressed.
- MTGJSON gives the external IDs in a small CSV file (`csv/cardIdentifiers.csv.gz`, about 16 MB), with the Scryfall ID of each printing.
- The site is recommander.cards, with an "a". Its public API (`POST /api/decks/recommend/top`) needs no key.
  It has rate limits. Its terms allow personal, non-commercial use, and ask for a credit to the source.

## Decision

- The import reads the bulk files of Scryfall, not the search API. The `otag:` search uses the file `oracle_tags`.
- The scheduled job `cards.import` runs every 12 hours. Scryfall updates its bulk files every 12 hours.
- The job imports, in this order: the sets, the cards, the Oracle tags, the illustration tags, the rulings and the MTGJSON identifiers.
- The job skips a source file when its date did not change since the last successful import. The command `reliquary jobs run cards.import --force` imports all the files.
- The setting `Cards:Import:BulkType` chooses the card file: `all_cards` (default, all the languages) or `default_cards` (smaller).
- The setting `Cards:Import:Directory` makes the job read local files and download nothing. The demo data and the tests use it.
- The import of the cards writes to staging tables with the binary COPY protocol. Then, in one transaction, it changes only the rows whose content hash changed.
- The import deletes the printings that the file does not contain, but only when the file contains at least 90 % of the current printings. A truncated file cannot delete the data.
- The import never deletes an Oracle card, because user data can refer to it.
- Reliquary can use recommander.cards in milestone M1, as an option that the administrator enables. The interface must credit the source.

## Consequences

- The `otag:` search and the auto-tag work with local data, with no request to Scryfall.
- A full import of `all_cards` takes about 3 minutes on a development computer. An import with no change takes about 1.5 minutes.
- Two imports of the same files give the same data. The tests check it.
- The database of the reference data is about 3 GB with `all_cards`.
- The administrator of a commercial instance must ask recommander.cards for an agreement before the instance enables it.
