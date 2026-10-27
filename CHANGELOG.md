# Changelog

This file lists the notable changes of each release.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow [Semantic Versioning](https://semver.org).

## Unreleased

### Added

- Repository rules, the English documentation with checks for ASD-STE100, and the backlog of the roadmap.
- .NET 10 solution skeleton with the five core components and the architecture tests.
- Web app skeleton with a French and English interface.
- Local infrastructure with PostgreSQL and Redis, and CI for the backend, the web app and the documentation.
- Database platform: connections through the shard directory, SQL migrations with DbUp, domain events through an outbox, and scheduled jobs.
- Worker process and the command line tool `reliquary`, with the command `migrate`.
- API conventions: problem details with stable error codes, paging with cursors, and JSON settings.
- OpenTelemetry traces, metrics and logs, Prometheus endpoints, readiness checks for PostgreSQL and Redis, and an optional Grafana service.
- Card reference data: sets, Oracle cards with all their faces and names, printings, tags, rulings and MTGJSON identifiers.
  A scheduled job imports them every 12 hours.
- Card endpoints: a card by its identifier or by any name, its printings and its rulings, a printing, and the sets.
- The CLI commands `jobs list` and `jobs run`.
- Accounts: registration with an email check, sign-in with a password or with Google, GitHub and Discord, and password reset.
  Web sessions use a short access token and a refresh cookie.
- Personal API keys with daily quotas for each key and each account. The quotas use Redis, so all the API replicas share them.
- Emails in English and French, and a Mailpit service in the local infrastructure.
- Web app: sign-in, registration, email check, password reset and OAuth sign-in, in French and English.
  An account page shows the profile, the password and the API keys.
- Card search page with the Scryfall syntax, the autocomplete of the keywords and of the card names, and the position of the syntax errors.
- Card page: the faces, the legalities, the other names, the rulings and the printings.
- Interface slots: the core renders its navigation and its account panels through named slots.
- Container images of the API, the worker, the CLI and the web app. `just up` builds and starts a full local instance with the demo data.
- The CLI command `demo load` loads the demo cards and the demo accounts.
- Extension registry: the administrator installs, enables and uninstalls extensions. The API refuses an extension for another version of the core API.
- The autocomplete of card names in all the languages: `GET /api/v1/cards/autocomplete`.
- The card catalog: the other core components get the card facts, resolve card names and find printings through `ICardCatalog`.
- The analysis rules of the cards with more than one face (ADR 0025).
- Decks: create, read, change and delete a deck and its lines, with the zones, the visibility and the version of the deck (ADR 0026).
- The deck text format: quantities, printings, finishes, tags, notes and zone sections, with the bulk edit syntax of Moxfield.
- Global tags, local tags and the predefined categories of the deck lines. Rename, merge and delete a tag.
- Read and replace the lines of a deck as text, with the error of each line.
- The legality check of a deck: deck size, copies, card legalities, commanders, pairs and color identity.
- Deck import and export in the formats of Moxfield, MTG Arena, Magic Online and Archidekt, and in the Reliquary text. A test proves the Moxfield round trip of the M1 gate.
- The copy of a deck: one of the decks of the user, or an unlisted or public deck of another user.
- Card search with the Scryfall syntax: `GET /api/v1/cards/search`, with all the names of a card, paging, warnings and errors with a position.
- Parity tests of the card search against the examples of the Scryfall syntax guide, on the demo dataset in `data/demo`.
- The search benchmark in `bench/Reliquary.Cards.Benchmark` and its GitHub workflow.
