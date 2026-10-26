# 0021. Card search with the Scryfall syntax in PostgreSQL

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

The specification asks for a card search with the full syntax of Scryfall, also by localized name.
Issue [#24](https://github.com/nicolas-goyon/reliquary/issues/24) asks for a translation of this syntax into SQL.
Issue [#25](https://github.com/nicolas-goyon/reliquary/issues/25) asks for an answer in less than 150 ms on about 100,000 printings.
The [technology stack](../architecture/tech-stack.md) gives a parser that converts the syntax to SQL, with Meilisearch as an optional complement.

These facts limit the options:

- The card data has about 39,000 Oracle cards, and 119,000 printings (`default_cards`) or 547,000 printings (`all_cards`).
- A Scryfall query mixes conditions on the Oracle card (type, text, colors, legality) and conditions on a printing (set, artist, rarity, price, language).
  Scryfall evaluates the query on each printing.
- Each instance runs on PostgreSQL. A second search engine is a new service for each administrator.
- The search must accept user text without SQL injection, and a slow regular expression must not block the database.

These are the options:

1. Convert the query into one SQL statement on the tables of the Cards component. This needs no new service. The indexes and the statistics of PostgreSQL decide the speed.
2. Send the cards to Meilisearch, and convert the query into its filter language. Meilisearch is fast for names, but it has no regular expressions and no conditions on two fields such as `pow>tou`.
3. Keep an index of the cards in the memory of the API. This is fast, but each replica of the API needs about 1 GB of memory, and the index must follow each import.

## Decision

- Reliquary converts each query into one parameterized SQL statement on PostgreSQL (option 1).
- A lexer and a recursive-descent parser give a syntax tree. They use no database, so the unit tests cover each construct.
- A compiler changes the tree into SQL conditions. The SQL text never contains a value of the query: each value is a parameter.
- A card matches when one of its printings matches the full condition, as on Scryfall.
  The conditions on the Oracle card stay outside the subquery on the printings, so a query without printing conditions reads only the Oracle cards.
- A syntax error gives HTTP 400 with the code `search_syntax_error` and the position of the error.
- The search ignores a term with an unknown keyword or a value that is not valid, and returns a warning, as Scryfall does.
  A query with no other condition gives the code `search_query_empty`.
- A word without a keyword matches all the names of the card: Oracle, face, localized and alternate names.
  When the name terms match only a localized name, the result shows a printing in the language of this name.
- Without `lang:`, the search uses the printings in English, and the printings whose set has no English printing with the same collector number.
- The search hides the extra cards and the memorabilia printings, unless a term targets them, as on Scryfall.
- The keywords `unique:`, `order:`, `dir:` and `prefer:` in the query win over the query parameters.
- The page uses an opaque cursor with the offset of the next page and a key of the query.
- The statement runs with a timeout of 5 seconds and without JIT compilation.
- The migration `0002_card_search.sql` adds generated columns, statistics and indexes for the search. The import computes the new columns that depend on other printings.
- Reliquary does not support `cube:`, because no source gives the cube lists. The search ignores the term with a warning.

## Consequences

- The search needs no new service. The benchmark on `default_cards` gives a 95th percentile under 150 ms on a computer with 2 processors (see the [search benchmark](../development/search-benchmark.md)).
- The parity tests run the examples of the Scryfall syntax guide on real card data and compare the results with Scryfall.
  The script `tools/search-parity/record_expectations.py` records the results of Scryfall again when the data changes.
- Some keywords use rules on the card data, for example the land shortcuts and `is:commander`. A new card cycle can need a new rule.
- The search by localized name finds more cards than Scryfall for some words. The documentation lists this difference.
- A first import after the migration fills the column `default_lang`. Until then, the search uses the printings in all the languages.
- Meilisearch stays an option for a later fuzzy search on names. It is not necessary for the 150 ms target.
