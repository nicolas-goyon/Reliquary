# Instructions for AI agents

This file gives the rules for AI agents that work on Reliquary.
Human contributors follow the same rules in [CONTRIBUTING.md](CONTRIBUTING.md).

## Rules

1. Write all code, code comments, documentation, commit messages, issues and pull request text in English.
2. Write all English text in ASD-STE100 Simplified Technical English. Read [docs/contributing/writing-style.md](docs/contributing/writing-style.md).
3. Use the strict mode for procedures, error messages, log messages and instructions. Use the STE-flavored mode for explanations.
4. Use the terms of [docs/glossary.md](docs/glossary.md). Do not use a different word for a term of the glossary.
5. Give each interface text an English version in `web/src/locales/en` and a French version in `web/src/locales/fr`.
6. Do not put card names in the translation files. Card names come from the card data.
7. Do not push to `main` or `develop`. Create a branch from `develop` and open a pull request to `develop`.
8. Write commit messages and pull request titles in the Conventional Commits format.
9. Do not reference a core component from another core component. Use interfaces and domain events.
10. Record each architecture decision as an ADR in `docs/decisions`.
11. Before you open a pull request, run `just lint` and `just test`. All checks must pass.
12. Do not change `tools/ste-lint/ste-lint.py`. Put the project configuration in `tools/ste-lint/lint-docs.py`.

## Repository map

| Path | Content |
| --- | --- |
| `src/Reliquary.Api` | Host of the public API |
| `src/Reliquary.BuildingBlocks` | Shared contracts for the core components |
| `src/Components` | The core components: Accounts, Cards, Decks, Collection and Playtest |
| `tests` | .NET tests, with the architecture tests |
| `web` | Web app: React, Vite, TypeScript and i18next |
| `docs` | Specification, architecture, roadmap, decision records and guides |
| `tools/ste-lint` | Check of the writing rules |

## Commands

| Command | Effect |
| --- | --- |
| `just up` | Start PostgreSQL and Redis |
| `just build` | Build the backend and the web app |
| `just test` | Run all the tests |
| `just lint` | Check the code and the documentation |
| `just lint-docs` | Check the writing rules and the Markdown rules |
