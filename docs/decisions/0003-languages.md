# 0003. Languages: English for the project, French and English for the interface

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

Reliquary is an open-source project for an international public.
Many of the first users speak French, and the specification asks for a French and English interface from the start.
The maintainer wrote the first specification in French.

## Decision

- Code, code comments, documentation, commit messages, issues and pull requests are in English.
- The interface is in French and in English from milestone M0.
- English is the source language of the interface texts.
- Each interface text exists in the two languages. A test checks that the two languages have the same keys and the same variables.
- Card names come from the card data, in the language of each printing. The translation files never contain card names.

## Consequences

- Contributors from all countries can read the code and the documentation.
- A specification in French needs an English version in the repository.
- Each change to the interface needs an English text and a French text. The author of the change writes both, or asks for help in the pull request.
- The [glossary](../glossary.md) gives the French term of each English term, so that the two interfaces use the same concepts.
- The [translation guide](../development/translations.md) gives the rules for the interface texts.
