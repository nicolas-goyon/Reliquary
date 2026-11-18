# Write a plugin

A plugin adds functions to the web app of Reliquary. This page tells how to write a plugin, test it and submit it.
[Extensions](../architecture/extensions.md) gives the architecture, and the [README of the plugin SDK](https://github.com/nicolas-goyon/reliquary/blob/develop/sdk/README.md) lists the functions of the SDK.

## What a plugin can do

- Add buttons and menu items: the slots `deck.actions`, `app.menu` and `navigation`.
- Add panels: a tab of the deck analysis (`analysis.tabs`), a part of the card page (`card.page`), a page (`plugin.page`) and a dialog (`plugin.dialog`).
- Add columns to the table mode of a deck (`table.columns`).
- Add import formats and export formats for decks and for the collection.
- Read and change the data of the user through the public API, with the scopes that the user accepted.
- Call the sites that its plugin manifest declares.
- Keep small values for the user (`sdk.storage`, 64 KB) and use the linked accounts of the user.
- Use the camera and a program on the computer of the user. See [Devices of a plugin](devices.md).

A plugin runs only in the browser, while the web app is open. It runs in a plugin frame: a sandboxed iframe with an opaque origin.
Thus a plugin cannot read the page of the core, the cookies or the session of the user.

## Start from the template

The folder [`sdk/template`](https://github.com/nicolas-goyon/reliquary/tree/develop/sdk/template) of the repository is a plugin with one deck action and one analysis panel.
Its README gives the steps to copy it and to start its development server.

## The plugin manifest

The file `manifest.json` declares all that the plugin asks for. The user reads these requests in the consent dialog.

```json
{
  "id": "deck-size",
  "name": "Deck size",
  "version": "0.1.0",
  "coreApi": "^1.0",
  "entry": "index.js",
  "permissions": ["decks:read"],
  "slots": ["deck.actions", "analysis.tabs"],
  "panels": [{ "id": "size", "slot": "analysis.tabs", "title": "Deck size" }]
}
```

- Ask only for the scopes, the origins and the devices that the plugin uses. The review refuses a plugin that asks for more.
- `permissions` lists the scopes of the public API: `decks:read`, `decks:write`, `collection:read`, `collection:write` and `profile:read`.
- `network` lists the origins that the plugin calls, for example `https://api.example.com`. The CSP of the frame blocks all other origins.
- `i18n` gives the translations of the name, the description and the titles.
- The JSON Schema [`manifest.schema.json`](https://github.com/nicolas-goyon/reliquary/blob/develop/sdk/schema/manifest.schema.json) gives all the rules. [Plugin manifest](../architecture/extensions.md#plugin-manifest) explains each field.

## Test the plugin in the development mode

1. Start a local instance of Reliquary: [Local instance](../deployment/local-development.md). A local instance allows development plugins.
2. Start the development server of the plugin, for example `npm run dev` in the template.
   The server must accept requests with the header `Origin: null`. With Vite, set `server.cors` to `true`.
3. In Reliquary, open **Account**. In the panel **Development plugins**, add the address of the plugin manifest, for example `http://localhost:5173/manifest.json`.
4. Open a page with a slot of the plugin, for example a deck. Each element of the plugin shows the text "Not checked".

Rules of the development mode:

- The browser keeps the address of the plugin manifest. The instance keeps no data about a development plugin.
- `sdk.api.fetch` gets a plugin token with the scopes of the field `permissions`. Thus a request without the correct scope fails with the status 403, as for an installed plugin.
- The frame can call the origins of the field `network`, and it gets the devices of the field `devices`.
- A development plugin has no consent dialog and no linked account. Install the plugin on a test instance to test them.
- A deployed instance does not allow development plugins.

## Rules for the code

- Call `definePlugin` one time, in the entry point.
- Do not block the main thread for more than some seconds. The core removes a frame that does not answer 3 heartbeats.
  After 3 crashes or 5 slow answers in one browser session, the core deactivates the plugin for the user.
  Move a long task to a classic Worker from a `blob:` URL.
- Show the data of the user and of other sites as text, with `textContent`. Do not put data in HTML text.
- Give the texts in English, and in French when you can. Use `sdk.language`.
- Do not keep a secret in `sdk.storage` or in the code. Use a linked account: the vault of the core encrypts its secret.
- Use `sdk.net.fetch` for the sites of the field `network`. The site must send `Access-Control-Allow-Origin: *`, because the frame has an opaque origin.
- A panel must work with the height that the core gives: 800 pixels at most for an analysis tab.

## Submit the plugin to the catalog

The plugin catalog contains the plugins that the project reviewed and signed. An instance offers only these plugins by default.

1. Put the plugin in a public Git repository, with a license and the file `package-lock.json`.
2. Make sure that two builds of the same commit give the same files. Use no timestamps and no random names in the files.
3. Read the [review check list](review.md), and correct the plugin.
4. Open a pull request on the catalog repository `nicolas-goyon/reliquary-plugins` with the address of the repository, the commit and the version.

The catalog repository does not exist yet: issue [#299](https://github.com/nicolas-goyon/reliquary/issues/299) creates it with its CI.
Until then, an administrator can install a plugin on an instance from its plugin manifest and its files.
[ADR 0052](../decisions/0052-plugin-catalog-signature-and-revocation.md) gives the design of the catalog.
