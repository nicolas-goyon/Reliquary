# 0015. Front end: React, Vite and i18next

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

The specification proposes React with Vite, and lists SvelteKit as the alternative.
The choice was an open question of the specification ([#5](https://github.com/nicolas-goyon/reliquary/issues/5)).
The front end has these constraints:

- The interface is in French and in English from milestone M0.
- The interface is a PWA.
- The web interface uses the same public API as plugins and third-party clients.
- Plugins render in isolated containers and use a TypeScript SDK.

## Decision

- The web app uses React, Vite and TypeScript.
- i18next with react-i18next manages the translations. The English resources give the TypeScript types of the translation keys.
- Vitest and Testing Library run the tests.
- oxlint checks the code. It is the linter of the Vite template.
- SvelteKit is not chosen.

## Consequences

- A call with an unknown translation key is a type error.
- A test checks that the French and English files have the same keys and the same variables.
- The plugin SDK can give React components to plugin authors, in addition to isolated iframes.
- The PWA support comes from a Vite plugin in milestone M2.
- The [translation guide](../development/translations.md) gives the rules for the interface texts.
