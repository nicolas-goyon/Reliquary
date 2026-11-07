# Reliquary web app

This folder contains the web interface of Reliquary: React, Vite, TypeScript and i18next.
The interface is in French and in English.

## Commands

Run these commands in this folder, or use the `just` recipes from the repository root.

| Command | Effect |
| --- | --- |
| `npm ci` | Install the packages |
| `npm run dev` | Start the development server on <http://localhost:5173> |
| `npm run lint` | Check the code with oxlint |
| `npm run typecheck` | Check the TypeScript types |
| `npm test` | Run the tests with Vitest |
| `npm run build` | Build the app into `dist`, with the service worker `dist/sw.js` |
| `npm run icons` | Make the icons of the web app manifest again from `scripts/generate-icons.mjs` |

The development server sends the `/api` requests to the API on <http://localhost:5080>.
Set `RELIQUARY_API_URL` to use a different API address.

The [web app guide](../docs/development/web-app.md) tells how the installable web app, its service worker, the keyboard shortcuts, the themes and the accessibility checks work.

## Translations

The texts are in `src/locales/en` and `src/locales/fr`.
Read the [translation guide](../docs/development/translations.md) before you add a text.
