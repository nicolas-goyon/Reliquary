# Contributing to Reliquary

Thank you for your help.
This guide gives the rules for all contributions: code, documentation, translations and issues.

## Rules in short

- All changes go through a pull request. Nobody pushes directly to `main` or `develop`.
- Code, documentation, commit messages, issues and pull requests are in English.
- All English text follows ASD-STE100 Simplified Technical English. Read the [writing style guide](docs/contributing/writing-style.md).
- The interface is in French and in English. Each new interface text needs the two languages.
- Each pull request passes all the checks: build, tests, format, writing rules and Markdown rules.

## Branches

| Branch | Use |
| --- | --- |
| `main` | Released versions. Only release pull requests from `develop` go to `main`. |
| `develop` | Integration branch. All work branches start from `develop` and go back to `develop`. |
| `<type>/<short-name>` | Work branch, for example `feat/card-search-parser` or `docs/collection-rules`. |

## Make a change

1. Find the issue for the change. If no issue exists, create one with a form.
2. Create a branch from `develop`: `git switch --create feat/short-name develop`.
3. Make the change. Add tests for each new behavior.
4. Add the English text and the French text of each new interface text.
5. Run `just lint` and `just test`. Correct each problem.
6. Push the branch and open a pull request to `develop`.
7. Write the title of the pull request in the Conventional Commits format.
8. Link the issue in the description: `Closes #123`.
9. Correct the problems that the checks or the review find.
10. The maintainer merges the pull request with "Squash and merge".

## Commit messages and pull request titles

Use the [Conventional Commits](https://www.conventionalcommits.org) format: `<type>(<optional scope>): <subject>`.

| Type | Use |
| --- | --- |
| `feat` | A new feature for users |
| `fix` | A bug correction |
| `docs` | Documentation only |
| `refactor` | A code change that keeps the behavior |
| `perf` | A performance improvement |
| `test` | Tests only |
| `build` | Build system or dependencies |
| `ci` | CI configuration |
| `chore` | Other maintenance work |
| `revert` | The reversal of an earlier commit |

Write the subject in the imperative mood, in lowercase, with no full stop at the end.
The subject follows the writing rules.
Examples:

```text
feat(cards): add the parser for the Scryfall search syntax
fix(decks): keep the zone of each line in the text export
docs: add the rules for French typography
```

A CI check reads the title of each pull request.
The squash merge uses the title as the commit message on `develop`.

## Releases

1. Open a pull request from `develop` to `main` with the title `chore: release vX.Y.Z`.
2. Update `CHANGELOG.md` in this pull request.
3. Merge the pull request with "Create a merge commit".
4. Create the tag `vX.Y.Z` on `main`.

## Checks

| Check | Local command | CI job |
| --- | --- | --- |
| .NET build, tests and format | `just build`, `just test`, `just lint-code` | Backend |
| Web lint, type check, tests and build | `just lint-code`, `just test`, `just build` | Web |
| Writing rules and Markdown rules | `just lint-docs` | Documentation |
| Title of the pull request | — | Pull request |

The [getting started guide](docs/development/getting-started.md) tells how to install the tools.

## Documentation and decisions

- The [specification](docs/specification/README.md) says what Reliquary must do. Change it in the same pull request as the behavior.
- Record each architecture decision as an ADR in [docs/decisions](docs/decisions/README.md).
- Add each new project term to the [glossary](docs/glossary.md).

## Translations

English is the source language of the interface.
Each key exists in `web/src/locales/en` and in `web/src/locales/fr`, and a test checks this.
The [translation guide](docs/development/translations.md) gives the rules, also for French typography.

## AI agents

AI agents follow the rules in [AGENTS.md](AGENTS.md).
The author of a pull request is responsible for all its content, also for the content that an agent wrote.

## Code of conduct

All contributors follow the [code of conduct](CODE_OF_CONDUCT.md).

## License of contributions

Reliquary uses the AGPL-3.0 license.
When you contribute, you agree that your contribution uses the same license.
