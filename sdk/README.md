# Reliquary plugin SDK

The package `reliquary-plugin-sdk` gives the functions and the types to write a plugin for [Reliquary](https://github.com/nicolas-goyon/reliquary).
The package has no runtime dependency.
It uses the [Apache License 2.0](LICENSE).

## How a plugin runs

- The core loads each active plugin in a plugin frame: a sandboxed iframe with an opaque origin and a CSP for the plugin.
- The plugin cannot read the DOM, the cookies or the storage of the core.
- The plugin and the core send JSON-RPC 2.0 messages on a `MessageChannel`.
  The SDK makes these messages for you.
- Each call has a time limit of 5 seconds and a size limit of 1 MB.
- The core sends a heartbeat each 2 seconds.
  The SDK answers it.
  When the plugin does not answer 3 heartbeats, the core removes the frame.
  Do not block the main thread of the frame for a long time.

[ADR 0049](../docs/decisions/0049-plugin-sandbox.md) gives the design of the sandbox.

## Write a plugin

Install the package:

```sh
npm install reliquary-plugin-sdk
```

Call `definePlugin` one time, in the entry point of the plugin:

```ts
import { definePlugin } from 'reliquary-plugin-sdk'

definePlugin({
  async activate(sdk) {
    await sdk.ui.contribute('deck.actions', {
      id: 'sync',
      label: { en: 'Sync', fr: 'Synchroniser' },
      async onAction({ deckId }) {
        const response = await sdk.api.fetch(`/decks/${deckId}`)
        await sdk.ui.notify(`Status ${response.status}`)
      },
    })
  },
})
```

### Panels

A panel is an analysis tab, a part of the card page, a plugin page or a plugin dialog.
The core shows each open panel in its own frame.
Declare the panel in the field `panels` of the plugin manifest, then give its code to `definePlugin`:

```ts
definePlugin({
  activate() {},
  panels: {
    stats: {
      mount(element, context) {
        element.textContent = `Deck ${'deckId' in context ? context.deckId : ''}`
      },
    },
  },
})
```

In a panel frame, the SDK calls `mount` and not `activate`.
The SDK gives the height of the content to the core when it changes.

The plugin manifest must declare each slot, each permission and each network domain that the plugin uses.
The type `PluginManifest` gives the fields of the plugin manifest.

## Functions of the SDK

| Function | Use |
| --- | --- |
| `sdk.api.fetch(path, init)` | Send a request to the public API. The core adds a plugin token with the scopes that the user accepted. |
| `sdk.net.fetch(url, init)` | Send a request from the frame to a domain of the plugin manifest. |
| `sdk.net.relay(request)` | Send a request through the network relay of the core, for a site that refuses requests from a browser. |
| `sdk.storage` | Keep small values for the user: `get`, `set`, `remove` and `keys`. |
| `sdk.accounts.list()` | Get the linked accounts of the user for this plugin. |
| `sdk.ui.contribute(slot, contribution)` | Add a button, a menu item or a column to a slot. |
| `sdk.ui.notify(message, tone)` | Show a short message to the user. |
| `sdk.ui.openDialog(panel, data)` | Open a panel of the slot `plugin.dialog` in a dialog. |
| `sdk.ui.closeDialog()` | Close the dialog of the plugin. |
| `sdk.ui.openPage(page)` | Open a panel of the slot `plugin.page`. |
| `sdk.ui.resize(height)` | Give the height of the panel. The SDK calls it for you. |

The core gives these functions in milestone M4.
Until the core has a function, a call to it fails with the error code `-32601` (method not found).

## Errors

A call that fails throws an `RpcError`.
The property `code` gives the cause:

| Code | Cause |
| --- | --- |
| `-32000` | The core did not answer in the time limit. |
| `-32001` | The message is larger than 1 MB. |
| `-32002` | The user did not accept the permission that the call needs. |
| `-32003` | The core closed the connection, for example because it removed the frame. |
| `-32601` | The core does not have this method. |

## Development of the SDK

Run these commands in the folder `sdk/`:

```sh
npm ci
npm run typecheck
npm test
npm run build
```

The tests use the test runner of Node.js 22.18 or later.

## Publication

The workflow `.github/workflows/sdk-publish.yml` publishes the package to npm with provenance.
It runs only for a tag `sdk-vX.Y.Z`.
Release-please creates this tag when the release pull request of the SDK merges.
