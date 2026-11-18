# Extensions

This file describes the two levels of extensions: plugins and modules.
It also gives the work that prepares the core for modules.
Terms follow the [glossary](../glossary.md).

## Two levels

Reliquary has two levels of extensions:

- **Plugins**: anyone can write a plugin.
  Plugins run only in the browser.
- **Modules**: a few trusted extensions.
  Modules run in the front end and on the server, and they can extend the data.

Plugins arrive in milestone M4.
Modules come later, but the core prepares for them from milestone M0.

| Aspect | Plugin | Module |
| --- | --- | --- |
| Where it runs | Only in the browser, in a sandbox | In the front end and on the server |
| Who can write it | Anyone | The internal team or selected partners |
| Who installs it | Each user, for their own account | The instance administrator |
| Access to data | The data of the user, through the public API, with the permissions that the user gives | Access on the server. A module can add its own tables and fields. |
| Acts without a connected user | No. A plugin acts only when the web interface is open. | Yes, for example with scheduled synchronizations and webhooks |
| Typical use | Show more information, automate an action, link an external account | An advanced feature that does not belong in the core |
| Expected number | Many | A few |

The core never runs plugin code on the server.
Only signed modules that the instance administrator installs run on the server.
The [architecture overview](./overview.md) shows where plugins and modules run.

## Plugins

### Purposes

Plugins have two purposes:

- **Show more information**: analysis panels, data from an external site on the page of a card or a deck, and more charts.
- **Create automation points**: for example, send a purchase list to a marketplace, or record the cards that leave the collection after a sale.
  A plugin can also import or synchronize decks and the collection from a scanner or from a Moxfield account.

### Linked accounts

- The plugin manifest declares each external site that the plugin needs.
  For each site, it declares an authentication method and a synchronization method.
  - Authentication methods: OAuth 2 with PKCE, an API key, or a public user name.
  - Synchronization methods: manual, when the web interface opens, or periodic while the web interface is open.
- The account page of the user shows a **Linked accounts** panel.
  Reliquary builds this panel from the installed plugins.
  For each site, the panel shows the site, the plugin that uses the site, the connection state and the last synchronization.
  The panel also has buttons to link and unlink the account.
- The core encrypts the secrets of linked accounts and keeps them in its vault.
  Each secret belongs to one user, one plugin and one site.
  Only this plugin can read the secret again, with a plugin token.
  When the user unlinks the account, the core deletes the secret.
- M4 delivers the public user name and the API key first.
  OAuth 2 with PKCE comes at the end of M4 only if a real plugin needs it.
- The core runs the sync. The minimum interval of a periodic sync is 15 minutes.

[ADR 0051](../decisions/0051-linked-accounts.md) gives the reasons for these rules.

#### Linked accounts API

The table `content.account_links` keeps one row for each user, plugin and site.
A row has the authentication method, the name of the external account, the encrypted secret and the key ID.
It also has the state and the time of the last sync.

| Endpoint | Credential | Effect |
| --- | --- | --- |
| `GET /api/v1/me/linked-accounts` | Web session | Lists the sites of the plugins that run for the user, with the link of each site. It also lists the links of the plugins that do not run, so the user can unlink them. The response never contains a secret. |
| `PUT /api/v1/me/extensions/{id}/accounts/{site}` | Web session | Links an account, or replaces the link. The body has `userName` for `public-username`, and `secret` with an optional `userName` for `api-key`. |
| `DELETE /api/v1/me/extensions/{id}/accounts/{site}` | Web session | Deletes the link and its secret. |
| `POST /api/v1/me/extensions/{id}/accounts/{site}/sync` | Web session | Records the result of a sync: `succeeded`, and `error` after a failed sync. The API keeps the time of the request and 200 characters of the message. |
| `GET /api/v1/me/extensions/{id}/accounts/{site}/secret` | Plugin token of the plugin `{id}` | Gives `site`, `auth`, `userName` and `secret`. The response has `Cache-Control: no-store`. |

- The plugin must run for the user, and its plugin manifest must declare the site. Else the API returns 404.
- The API refuses `oauth2-pkce` with the code `linked_account_method_not_supported` until OAuth arrives.
- A plugin token of a different plugin gets the status 403. A web session, an API key and a delegation token also get the status 403.
- `RequirePluginToken()` marks an endpoint that accepts only a plugin token. `ScopeFilter` lets a plugin token through to such an endpoint, with no scope.

#### Sync of linked accounts

The web app runs the sync. `web/src/linkedAccounts/sync.ts` loads with the plugin runtime.

- When a plugin frame starts, the core reads the linked accounts of the plugin:
  - `on-open`: the core runs a sync of each linked account.
  - `periodic`: the core runs a sync when the last sync is older than the interval, then checks again after each interval.
    The interval is `intervalMinutes`, 60 minutes when the manifest gives no interval, and 15 minutes at least.
    Before a periodic sync, the core reads the time of the last sync again, so two tabs do not run two syncs.
- The Linked accounts panel shows a **Sync** button when the plugin frame runs. A new link also gets a first sync.
- For a sync, the core calls `account.sync` on the plugin, with a time limit of 60 seconds.
  The plugin throws an error when the sync fails. The core sends the result with `POST .../sync`.
- The plugin gets the secret with `accounts.secret`. The core gets a plugin token, then calls the secret endpoint, and gives the answer to the frame of this plugin only.
  The plugin gets its linked accounts without secrets with `accounts.list`.

#### Vault

The vault encrypts each secret with AES-GCM ([ADR 0050](../decisions/0050-no-plugin-code-on-the-server.md)).

- The setting `Plugins:VaultKeys` gives the keys by key ID. Each key is 32 bytes in Base64.
- The key with the last key ID in ordinal order encrypts the new secrets. The vault reads a secret with the key of its key ID.
- The associated data is the owner ID, the plugin ID and the site. A secret of one user, plugin or site does not decrypt for a different user, plugin or site.
- When a plugin reads a secret of an old key, the API encrypts the secret again with the current key.
- The API does not start when a key ID or a key is not valid. The vault ignores a key with an empty value.
- Without a key, the API refuses a link with a secret with the status 503 and the code `vault_not_configured`.
- When the key of a secret is missing, the secret endpoint returns the status 409 with the code `vault_key_missing`.
  The list shows `secretReadable: false`, and the user must link the account again.

#### Personal data

The provider `PluginPersonalData` of the platform follows [ADR 0032](../decisions/0032-personal-data.md):

- The export of an account contains the folder `plugins`:
  - `plugins.json` lists the plugins that the user activated, with the consent of the user.
  - `linked-accounts.json` lists the linked accounts: the plugin, the site, the authentication method, the user name and the state of the last sync.
    The field `hasSecret` tells whether the link has a secret. The file never contains a secret.
  - `storage.json` lists the values of the [plugin storage](#plugin-storage), with the plugin and the key of each value.
- The deletion of an account deletes the plugin activations, the linked accounts with their secrets, and the plugin storage.

### Interface slots

- The interface contains named places for extensions: the **slots**.
  The slots include the action bar of a deck, menus, analysis tabs and the columns of the table mode.
  They also include the card page, the account page and the navigation items.
- A plugin declares in its plugin manifest the slots that it uses.
  In these slots, it adds buttons, charts, interface elements or a new group of menu items.
- The core isolates each plugin.
  Each active plugin runs in one plugin frame: a sandboxed iframe with an opaque origin and a CSP for this plugin.
  The plugin and the core communicate with JSON-RPC 2.0 on a `MessageChannel`.
  A plugin cannot access the DOM of the core or the DOM of other plugins.
- For buttons, menu items, navigation items, table columns, import formats and export formats, the plugin sends data.
  The core renders this data with its own components.
- The slots `deck.actions`, `app.menu` and `navigation` accept buttons and menu items from plugins:
  - The plugin calls `ui.contribute` with an ID, a label and an optional icon.
    The label has an English text and optional translations, with 40 characters at most.
    The icon comes from a fixed set of 12 icons.
  - The core checks the data and refuses a slot that the plugin manifest does not declare.
    A plugin can add 3 items at most to each slot.
  - The registry keeps the item as a contribution of the kind `plugin`, with data and no render function.
    `SlotItem` renders it with the component `PluginAction` of the core, after the items of the core.
  - When the user selects the item, the core calls `ui.action` with the IDs of the context, for example `{ deckId }`.
    The plugin does not get the objects of the page.
  - A contribution of a development plugin shows the badge "Not checked".
  - When the core removes the plugin frame, it also removes the items of the plugin.
- Plugin panels use the slots `analysis.tabs`, `card.page`, `plugin.page` and `plugin.dialog`:
  - The field `panels` of the plugin manifest declares each panel, with its slot and its title.
    The field `i18n` gives the translated titles with the key `panels.<id>.title`.
    `GET /api/v1/extensions` gives the panels and the translations to the web app.
  - The registry keeps each panel as a contribution of the kind `plugin-panel`, with no render function.
    `SlotItem` renders it with the component `PluginPanel` of the core.
  - The core creates the frame of a panel only when the user opens the panel, and removes it when the panel closes.
    An analysis tab opens when the user selects it, and a part of the card page opens when the user expands it.
  - A panel frame uses the same frame address as the plugin.
    The connect message has the field `panel` with the ID, the slot and the context.
    Thus the SDK calls the code of the panel and not `activate`.
  - The panel gets the IDs of the context only: `{ deckId }`, `{ oracleId }`, `{ page }` or `{ data }`.
    When the IDs change, the core calls `panel.update`.
  - The SDK gives the height of the content with `ui.resize`.
    The core applies a minimum of 48 pixels and the maximum of the slot: 800 for `analysis.tabs`, 600 for `card.page` and `plugin.dialog`, 2000 for `plugin.page`.
  - `ui.openPage` opens the page `/plugins/<plugin ID>/<panel ID>`.
    `ui.openDialog` opens a panel of the slot `plugin.dialog` in a dialog of the core, and `ui.closeDialog` closes it.
  - The watchdog also applies to each panel frame.
    A panel that does not answer shows an error, and the core removes its frame.
  - A panel of a development plugin shows the banner "Not checked".
- Plugin columns use the slot `table.columns`:
  - The plugin calls `ui.contribute` with an ID and a title. A plugin can add 2 columns at most.
  - The registry keeps the column as a contribution of the kind `plugin-column`, with no render function.
    `SlotItem` renders the header and the cells with the component `PluginColumn` of the core. A cell has no iframe.
  - The core collects the cells that the table asks in one render.
    Then it calls `ui.cells` one time with all the rows, in parts of 200 rows.
    Each row has the deck ID, the line ID, the oracle ID, the quantity and the zone.
  - The plugin gives one cell for each row: a text of 100 characters at most, an optional tone and an optional tooltip, or null.
    The tones are `neutral`, `positive`, `warning` and `negative`.
  - The core keeps the cells of the last version of each deck. A change of the deck increases its version, so the core asks the cells again.
  - A cell that the plugin does not give shows a dash, with the reason for screen readers.
  - The header of a column of a development plugin shows the badge "Not checked".
- Plugin formats use the slots `deck.import-formats`, `deck.export-formats`, `collection.import-formats` and `collection.export-formats`:
  - The field `formats` of the plugin manifest declares each format, with its direction, its target, its title and its file name extensions.
    `GET /api/v1/extensions` gives the formats to the web app.
  - The import and export forms show the plugin formats after the formats of the core.
    A format of a development plugin shows "not checked" after its name.
  - An import format calls `format.import` with the content and the name of the file.
    The plugin gives a deck list in the text format of Reliquary, or a collection CSV of Reliquary.
    The core then imports this text with its own endpoint and the session of the user, so the plugin needs no write scope.
  - An export format gets the CSV file of Reliquary from the export endpoint of the core.
    Then it calls `format.export`, and the plugin gives its file: a name, a media type and a text.
  - A conversion has a time limit of 30 seconds.
    The size limit of 1 MB of the RPC channel applies to the content in the two directions.
  - The core removes the folder part and the control characters of the file name.
    It accepts only text media types, JSON and XML, and uses `text/plain` for other types.
- Panels (analysis tabs, the card page, plugin pages and dialogs) render in their own sandboxed iframe.
- The plugin frame contains no credential of the user.
  The core calls the public API for the plugin with a plugin token, which has only the scopes that the user accepted.
- [ADR 0049](../decisions/0049-plugin-sandbox.md) gives the reasons for the sandbox.
- The web app has the slots from milestone M0, and the core uses them for its own panels.
  `web/src/slots/registry.ts` gives the names of the slots and the context of each slot.
  `web/src/slots/coreContributions.tsx` registers the panels of the core, for example the panels of the account page.
  Each contribution renders in its own container with its own error boundary, so a contribution that fails does not stop the page.
- The slot `deck.actions` adds buttons to the action bar of the deck page, for each user who can see the deck.
  The context is `{ deckId, deck }`. The core uses the slot for the button that prints proxies.
- Each contribution of the slot `analysis.tabs` is one tab of the deck analysis.
  The optional `title` of the contribution gives the name of the tab, and a `null` title hides the tab for a deck.
  The context gives the deck and its analysis, so an extension can add its own analysis panel.
- The slot `table.columns` adds columns to the table mode of the deck page.
  The table renders each contribution in the header with the context `{ deckId, part: "header" }`.
  It also renders the contribution in each row with the context `{ deckId, part: "cell", line, card }`.
  `line` is the deck line of the row, and `card` is the data of its card, or `null`.
  Each header and each cell has its own error boundary.
  The column comes after the columns of the core, in the order of the contributions.
- The core keeps control of the layout:
  - Each slot has a maximum size.
  - The user can change the order of the contributions and hide them.
  - The core disables a plugin automatically if the plugin crashes or slows the interface.
  The section [Layout and failures](#layout-and-failures) gives the rules.

### Plugin runtime

The folder `web/src/plugins` contains the plugin runtime of the web app.
[ADR 0049](../decisions/0049-plugin-sandbox.md) gives the design.

- The web app gets `GET /api/v1/extensions` after the sign-in.
  It loads the runtime module only when the user has an active plugin, so the main bundle does not contain the runtime.
- For each active plugin, the runtime creates a hidden plugin frame with `sandbox="allow-scripts"`, an empty `allow` attribute and no referrer.
  The address of the frame is `/api/v1/plugins/{id}/{version}/frame`.
- The connection has two window messages:
  1. The plugin frame sends `reliquary:ready` to its parent window.
  2. The runtime checks that `event.source` is the window of the frame.
     Then it sends `reliquary:connect` one time, with the port of a new `MessageChannel`.
- After the connection, the core and the plugin send JSON-RPC 2.0 messages on the port.
  Each message is one JSON string, with a size limit of 1 MB.
  Each call has a time limit of 5 seconds by default.
- The runtime sends the heartbeat `plugin.ping` each 2 seconds, with a time limit of 2 seconds.
  After 3 missed heartbeats in a row, the runtime removes the frame.
- The runtime removes a frame that does not send `reliquary:ready` in 10 seconds.
- The plugin SDK has the same protocol in `sdk/src`.
  The container build of the web app sees only the folder `web/`, so `web/src/plugins` keeps a copy of `protocol.ts` and `rpc.ts`.
  The test `web/src/plugins/sdkCopies.test.ts` fails when a copy is different from the file in `sdk/src`.
- After a change of the plugins of the user, for example an activation, the web app calls `notifyPluginsChanged()`.
  The runtime then gets `GET /api/v1/extensions` again, removes all the plugin frames, and starts the active plugins again.

The instance settings of the section `Plugins` are:

| Setting | Default | Effect |
| --- | --- | --- |
| `Plugins:MaxActivePlugins` | 8 | The maximum number of active plugins for each user. `PUT /api/v1/me/extensions/{id}` refuses one more plugin with the error `too_many_active_plugins`. |
| `Plugins:FrameOrigin` | none | A second host name for the plugin frames, for example `https://plugins.example.org`. Without a value, the frames use the host of the instance, with an opaque origin. |
| `Plugins:AllowDevelopmentPlugins` | `false` | Allows the development mode. `appsettings.Development.json` and the local instance of `compose.yaml` turn it on. A deployed instance keeps it off. |
| `Plugins:Catalog:Url` | none | The address of the catalog index, with HTTPS. Without a value, the worker does not sync a plugin catalog. See [Plugin catalog](#plugin-catalog). |
| `Plugins:Catalog:Keys` | the keys of the project | The public keys that can sign the catalog index. |
| `Plugins:Catalog:Interval` | `01:00:00` | The minimum time between two syncs of the catalog. |

`GET /api/v1/extensions` gives these settings to the web app in the field `plugins`.

#### Development mode

On a development instance, a user loads a plugin from the URL of its plugin manifest, for example `http://localhost:5173/manifest.json`.

1. The panel **Development plugins** of the account page shows only when `Plugins:AllowDevelopmentPlugins` is on.
2. The web app reads the plugin manifest from the URL.
   The server of the plugin must accept requests from the origin of the instance (CORS).
3. The browser keeps the URL in its local storage. The core keeps no data about development plugins.
4. The runtime loads the frame from `GET /api/v1/plugins/development/frame?entry=<URL>&network=<origin>`.
   The CSP of this frame allows the origin of the entry point for scripts, styles, fonts, images and connections.
   It also allows the WebSocket of this origin, so the server of the plugin can reload the plugin.
   The `network` origins of the plugin manifest get `connect-src` and `img-src`.
   `PluginFramePolicy.ForDevelopment` makes this CSP. The same class makes the CSP of the installed plugins, so the two policies have the same common rules.
5. The frame address also has the `devices` of the plugin manifest. The frame gets these devices, and the `Permissions-Policy` header turns off the other features.
6. The connect message has `development: true`, and the interface shows a "Not checked" banner for the plugin.
   Each contribution of a development plugin must show this banner.

The core does not check a development plugin, but the plugin runs in the same sandbox as other plugins.
The development plugins count in the limit of active plugins.

#### Scripts in a plugin frame

The frame has an opaque origin, so the browser sends the requests of the frame with the header `Origin: null`.
A test with Chromium 158 and Firefox 159 gave these results for the entry point of a plugin:

| Script | Without `Access-Control-Allow-Origin` | With `Access-Control-Allow-Origin: *` |
| --- | --- | --- |
| `<script type="module">` | Does not run | Runs |
| Classic script with `integrity` and `crossorigin` | Does not run | Runs |
| Classic script with no `integrity` | Runs | Runs |

Thus:

- The API must send `Access-Control-Allow-Origin: *` with the files of a plugin, because the frame loads them with an integrity hash.
- The default CORS rule of the Vite server accepts only `localhost` origins, not `null`.
  A plugin author sets `server.cors` to `true` in the Vite configuration of the plugin to use the development mode.

#### Workers in a plugin frame

A plugin can move long tasks to a Worker, so the main thread of the frame answers the heartbeats.
A test with Chromium 158 and Firefox 159 gave these results in a frame with an opaque origin and `default-src 'none'`:

| Worker | `worker-src blob:` | No `worker-src` |
| --- | --- | --- |
| Classic Worker from a `blob:` URL | Runs in Chromium and Firefox | Blocked |
| Module Worker from a `blob:` URL | Runs in Firefox, fails in Chromium | Blocked |
| Worker from a file of the plugin | Fails: the file does not have the opaque origin of the frame | Fails |
| Worker from a `data:` URL | Blocked | Blocked |

Thus the CSP of the plugin frame must contain `worker-src blob:`, and a plugin must use a classic Worker from a `blob:` URL.
A `MessagePort` can go to such a Worker.

### Network

- A plugin can reach only the domains that its plugin manifest declares.
  These domains can include `localhost`, for example for a scan application or a local tool.
- The CSP of the plugin frame contains only the domains of the plugin manifest that the user accepted.
  The section [Plugin frame and plugin files](#plugin-frame-and-plugin-files) gives the rules.
- Some sites refuse calls from a browser (CORS).
  For these sites, the core offers a network relay.
  The relay accepts only the domains of the plugin manifest, and it has quotas.
  The relay does not log the content of the requests.
  The relay is off by default, and the administrator enables it for the instance.
  The core does not have the relay yet, and the plugin SDK has no method for it.
  Issue [#289](https://github.com/nicolas-goyon/reliquary/issues/289) adds the two when a real plugin needs the relay.

[ADR 0011](../decisions/0011-plugin-network-allowlist.md) and [ADR 0050](../decisions/0050-no-plugin-code-on-the-server.md) give the reasons for these rules.

### Plugin manifest

This example shows the plugin manifest of a plugin that synchronizes decks with Moxfield:

```json
{
  "id": "moxfield-sync",
  "name": "Synchronisation Moxfield",
  "version": "0.1.0",
  "coreApi": "^1.0",
  "permissions": ["decks:read", "decks:write"],
  "slots": ["deck.actions", "account.linked"],
  "network": ["https://api.moxfield.com"],
  "accounts": [
    {
      "site": "moxfield.com",
      "auth": "public-username",
      "sync": { "mode": "on-open" }
    }
  ],
  "entry": "dist/index.js"
}
```

The JSON Schema `sdk/schema/manifest.schema.json` gives the rules of the plugin manifest.
The SDK, the plugin catalog and the API use this file.
The API applies the same rules with `PluginManifestCheck` in the platform, and a test compares the two.
When you change a rule, change the schema and `PluginManifestRules` in the same pull request.

| Field | Content |
| --- | --- |
| `id` | 2 to 64 lowercase letters, digits and hyphens. Necessary. |
| `kind` | The value must be `plugin`. The schema accepts a manifest without `kind`, but `PUT /api/v1/admin/extensions/{id}` needs it, because this endpoint also installs modules. |
| `name`, `description` | The texts in English. `name` is necessary. |
| `version` | `major.minor.patch` with an optional pre-release part. The version cannot have build metadata, because the version is part of the URLs of the plugin files. |
| `coreApi` | The range of core API versions, in the syntax of npm. Necessary. |
| `entry` | The relative path of the JavaScript module of the plugin. A path segment cannot start with a dot. Necessary. |
| `permissions` | The scopes of the public API. |
| `slots` | The slots that the plugin uses. |
| `network` | HTTPS origins, `http://localhost` and `http://127.0.0.1`, with an optional port. An origin can have a wildcard for one level only, for example `https://*.example.com`. |
| `devices` | `camera` and `loopback-network`. |
| `accounts` | The linked account sites: the host name, the authentication method and the sync. The interval of a periodic sync is 15 to 1440 minutes. |
| `panels` | The panels: an ID, a slot (`analysis.tabs`, `card.page`, `plugin.page` or `plugin.dialog`) and a title. The slot must also be in `slots`. |
| `formats` | The import formats and the export formats: an ID, the direction, the data (`deck` or `collection`), a title and the file name extensions. |
| `i18n` | The translated texts for each language. The keys `name`, `description`, `panels.<id>.title` and `formats.<id>.title` translate the texts of the manifest. |
| `settings` | The default settings of the instance, as a JSON object. |

The API refuses a plugin manifest with an unknown field.
The registry keeps all the fields of the plugin manifest.

### Plugin frame and plugin files

The table `users.extension_files` keeps the files of each plugin version: the path, the content, the media type and the SHA-384 hash.
The files of a version do not change.
The catalog sync adds the files of a new version with `PluginFileStore`.
The store accepts 200 files and 5 MB at most for each version.
It accepts only these file name extensions: `.js`, `.mjs`, `.css`, `.json`, `.map`, `.txt`, `.wasm`, `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.svg`, `.woff` and `.woff2`.
A plugin has no HTML file, because the API makes the HTML of the plugin frames.

Two endpoints send a plugin version to the browser:

- `GET /api/v1/plugins/{id}/{version}/frame` sends the HTML of the plugin frame.
  The HTML loads the entry of the manifest as a module, with its SHA-384 integrity hash.
- `GET /api/v1/plugins/{id}/{version}/files/{path}` sends a file of the plugin.

The two endpoints accept requests with no credential, because the browser sends no credential when it opens the frame.
They send only the installed version of a plugin that the instance enables, so a disabled plugin stops at the next start of its frame.
They do not send a version that the plugin catalog revoked.

The frame request has no credential, so the API does not know the user.
Thus the runtime of the web app puts the values that the user accepted in the query of the frame address:

```text
/api/v1/plugins/{id}/{version}/frame?network=https%3A%2F%2Fapi.example.com&devices=camera
```

- Each `network` value must be an origin of the `network` list of the manifest, with the same text.
  Each `devices` value must be a device of the manifest.
  The API refuses another value with the status 400 and the code `not_allowed`.
  Thus the query can only remove values of the manifest, and a wildcard is possible only when the manifest declares it.
- Without `network`, the frame can reach no network origin.
  Without `devices`, the frame gets no device.

The frame response has these headers:

- `Content-Security-Policy`. `PluginFramePolicy` makes the policy for all the plugin frames:
  - `default-src 'none'`
  - `script-src`, `style-src` and `font-src`: the folder of the files of the plugin version. `style-src` also has `'unsafe-inline'`.
  - `img-src`: the folder of the files, the accepted `network` origins, `data:` and `blob:`
  - `connect-src`: the folder of the files and the accepted `network` origins
  - `worker-src blob:`. In a frame with an opaque origin, only a classic Worker from a `blob:` URL works.
  - `base-uri 'none'` and `form-action 'none'`
- `Permissions-Policy`. The header turns off the powerful features of the browser, for example `camera`, `microphone`, `geolocation` and `usb`.
  It does not turn off the features of the accepted devices: `camera` for the device `camera`, and `loopback-network` and `local-network-access` for the device `loopback-network`.
  A header can only remove a permission.
  The `allow` attribute of the iframe gives the accepted devices to the frame, for example `allow="camera"`.
  The web app makes the attribute from the `devices` values of the frame address (`frameAllow` in `web/src/plugins/frame.ts`), so the attribute and the header agree.
  [Devices of a plugin](../plugins/devices.md) gives the rules for plugin authors and the manual check list for the browsers.
- `Cache-Control: no-store`, `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer`

Known limit: a sandboxed frame can open a new address in its own frame.
Thus a plugin can open its frame address again with all the `network` origins of its manifest, also the origins that the user did not accept.
The new document has no RPC port, so it cannot get more data from the core.
But the plugin can put data that it already has in the new address, and then send this data to an origin of its manifest.
Thus the limit of the accepted list is strong only for the origins that are not in the manifest.
The review of the plugin catalog checks each origin of the manifest.
A signed frame address from the API can remove this limit later.

A file response has these headers:

- `Access-Control-Allow-Origin: *`. The frame has an opaque origin, so its requests have the header `Origin: null`. A script with an integrity hash needs this header.
- `Content-Security-Policy: sandbox; default-src 'none'`. A browser that opens a file directly gives it an opaque origin, so a plugin file cannot run on the origin of the instance.
- `X-Content-Type-Options: nosniff`, the media type of the file, and an `ETag` with the SHA-384 hash

### Plugin tokens

The plugin frame contains no credential of the user.
The web app calls the public API for a plugin with a plugin token.
[ADR 0049](../decisions/0049-plugin-sandbox.md) gives the reasons.

- `POST /api/v1/me/extensions/{id}/token` gives a plugin token. Only a web session can call it.
  The plugin must run for the user: the instance enables it, it supports the core API, and the user activated it.
  The user must also accept all the access of the installed version. Else the API returns 404.
- The response gives `token`, `expiresAt`, `pluginId` and `scopes`.
  The token is valid for 10 minutes (`Auth:PluginTokenLifetime`).
- The scopes are the permissions of the installed version that the user accepted. The section [Consent](#consent) gives the rules.
- The web app sends the token in the header `Authorization: Bearer rlp_...`.
  `ScopeFilter` checks the scope of the endpoint, as for a delegation token. An endpoint without a scope refuses the token with the status 403 and the code `scope_missing`.
  The only exceptions are the endpoints with `RequirePluginToken()`, for example the secret endpoint of the linked accounts.
- The API asks the extension registry for each request whether the plugin still runs for the user.
  After a deactivation by the user or by the administrator, the token gives the status 401.

A plugin token is a JWT with the signing key of the access tokens.
Three properties keep it separate from the access token of a web session, in both directions:

| Property | Access token | Plugin token |
| --- | --- | --- |
| Start of the header value | None | `rlp_` |
| Audience (`aud`) | `Auth:Audience`, `reliquary` by default | `reliquary-plugin` |
| Token type (`typ` of the JWT header) | `JWT` | `rlp+jwt` |
| Method (`amr`) | `session` | `plugin` |

Each scheme checks all its properties, so a token with one property of the other kind fails.
The plugin token also contains `sub` (the user), `plg` (the plugin ID) and `scope` (the scopes, with a space between them).
A delegation token is a random secret with the start `rld_`, so it is never a valid JWT.
The policies `Session` and `Administrator` refuse a plugin token, so a plugin token cannot get a new plugin token.

#### Requests of a plugin to the public API

A plugin calls `sdk.api.fetch(path, init)`. The method `api.fetch` of the core sends the request. `web/src/plugins/hostApi.ts` contains the code.

- The main frame and each panel frame of a plugin can call the method.
- The path is a path after `/api/v1`, for example `/decks?limit=10`.
  The core refuses a path with `//`, a backslash, a `#` or a dot segment, also when the path encodes them. Thus a request cannot leave `/api/v1`.
- The methods are `GET`, `POST`, `PUT`, `PATCH` and `DELETE`. The body is JSON.
- The core sends the request with the plugin token and with no cookie.
  Thus a plugin cannot use the refresh cookie of the web session.
- The core keeps one plugin token for each running plugin, until one minute before the token expires.
  After the status 401, the core gets a new token and tries the request one time.
- The plugin gets the status and the JSON body of the response. An error status of the API is a response for the plugin, not a failure of the call.

A development plugin has no manifest on the instance. On an instance with `Plugins:AllowDevelopmentPlugins`, the web app asks the token with the ID `dev:<plugin ID>` and the scopes of the plugin manifest:
`POST /api/v1/me/extensions/dev:deck-stats/token` with `{ "scopes": ["decks:read"] }`.
The token gets only these scopes. A plugin ID cannot contain a colon, so this ID is never the ID of an installed plugin.
Other instances return the status 404.

#### Plugin storage

The plugin frame has an opaque origin, so it has no storage of the browser.
A plugin keeps small values for the user with `sdk.storage`: `get`, `set`, `remove` and `keys`.

- The table `content.plugin_storage` keeps one JSON value for each owner, each plugin and each key.
- A key has 1 to 128 characters. The keys and the values of one plugin and one user have 64 KB at most.
  A write that goes over this limit changes nothing. The API returns the status 413 with the code `plugin_storage_full`, and the plugin gets the error code `-32004`.
- The endpoints accept only a plugin token of the plugin `{id}`. A web session and the token of a different plugin get the status 403.
  - `GET /api/v1/me/extensions/{id}/storage` lists the keys.
  - `GET /api/v1/me/extensions/{id}/storage?key=<key>` gives `{ "value": ... }`, or the status 404.
  - `PUT /api/v1/me/extensions/{id}/storage?key=<key>` with `{ "value": ... }` keeps the value.
  - `DELETE /api/v1/me/extensions/{id}/storage?key=<key>` deletes the value.
- The storage keeps no `null` value: `sdk.storage.set(key, null)` removes the key.
- A plugin must not keep a secret in the storage. The vault keeps the secrets of the linked accounts.
- The storage of a development plugin uses the ID `dev:<plugin ID>`.

#### Notices

A plugin calls `sdk.ui.notify(message, tone)` to show a short message. The tones are `info`, `success` and `error`.
The component `PluginNotices` of the core shows the message as text, with the name of the plugin, for 8 seconds.
The core cuts a message after 200 characters, and it shows 3 notices at most for each plugin.

### Plugin catalog

The plugin catalog publishes two files: the catalog index `index.json` and its signature `index.json.sig`.
[ADR 0052](../decisions/0052-plugin-catalog-signature-and-revocation.md) gives the reasons for the design.

```json
{
  "format": 1,
  "serial": 42,
  "expires": "2026-12-16T00:00:00Z",
  "keyId": "reliquary-2026",
  "plugins": [
    {
      "id": "deck-stats",
      "versions": [
        {
          "version": "1.2.0",
          "files": [
            { "path": "manifest.json", "integrity": "sha384-...", "size": 412 },
            { "path": "dist/index.js", "integrity": "sha384-...", "size": 18230 }
          ]
        }
      ]
    }
  ],
  "revoked": [
    { "id": "deck-stats", "version": "1.1.0", "reason": "The version sends the deck lists to a site that the manifest does not declare." }
  ]
}
```

| Field | Content |
| --- | --- |
| `format` | The version of the format. The core reads only the format 1. |
| `serial` | A number that increases at each publication, 1 or more. |
| `expires` | The end of the validity, in the ISO 8601 format. The catalog signs the index again before this date. |
| `keyId` | The ID of the key that signed the index. |
| `plugins` | The plugins, with their versions. Each version has 1 to 200 files: the path, the SHA-384 integrity value and the size in bytes. The file `manifest.json` is the plugin manifest of the version, and it is necessary. The rules of the paths and of the sizes are the rules of the store of plugin files. |
| `revoked` | The revoked versions: the plugin ID, the version and a reason of 500 characters or fewer. |

`CatalogIndexCheck` of the platform checks an index in this order:

1. The index has 1 MB or less, and the signature has 256 bytes or less.
2. A trusted key made the signature.
   The signature is a DER ECDSA P-256 signature with SHA-256 of the exact bytes of `index.json`.
   Thus the check does not serialize the JSON again, and `openssl dgst -sha256 -sign` makes a valid signature.
3. The JSON follows the format.
   The check refuses an unknown field, a field two times, a missing field and a `null` value.
   The field `keyId` must name the key that made the signature.
4. The serial is not smaller than the serial of the last index of the instance.
   An index with the same serial must have the same bytes.
5. The date `expires` is in the future.

The setting `Plugins:Catalog:Keys` gives the trusted keys.
Each key has an `Id` and a `PublicKey`: a PEM block or the Base64 text of a DER SubjectPublicKeyInfo structure.
To change the signing key, the operator adds the new key, waits for an index from the new key, then removes the old key.
Without a value, the instance trusts the keys of the project.
The project catalog has no key yet, so an instance must set `Plugins:Catalog:Keys` until the project adds its key to the code.

#### Catalog sync

The worker job `plugins.catalog` syncs the catalog each hour.
The worker has this job only when `Plugins:Catalog:Url` has a value.
An administrator can start it with `reliquary jobs run plugins.catalog`.

1. The job gets the index and the signature from `Plugins:Catalog:Url` and from the same address with `.sig` at the end.
2. It checks them with `CatalogIndexCheck`.
   When the check refuses the index, the job fails and the instance keeps its last index.
3. The table `users.plugin_catalog` keeps the last index that the instance accepted: the serial, the hash, the bytes of the index and the signature.
4. For each version that is not revoked and not in the store of plugin files, the job gets each file from `<folder of the index>/<plugin ID>/<version>/<path>`.
   For example, the address of `dist/index.js` of `deck-stats` 1.2.0 is `https://example.org/catalog/deck-stats/1.2.0/dist/index.js`.
5. The job checks the size and the SHA-384 hash of each file.
   It also checks the plugin manifest of the version with `PluginManifestCheck`, its ID, its version and its entry file.
6. `PluginFileStore` keeps the files of each version that passes the checks.
   A version with an incorrect file stays out of the store.
   The job then fails, and the next sync tries the version again.

The job does not install a plugin in the extension registry.
The administrator installs a plugin of the catalog on the page **Plugins** of the administrators.

#### Catalog page of the administrators

The page `/admin/plugins` of the web app shows the plugins of the catalog and the installed plugins. Only an administrator can use its endpoints.

- `GET /api/v1/admin/plugin-catalog` gives the state of the last catalog index (`serial`, `expiresAt`, `checkedAt`) and the plugins whose files the instance has.
  Each plugin has its name, its description, its versions and `latestVersion`: the last version that the catalog did not revoke.
- `POST /api/v1/admin/plugin-catalog/sync` runs the catalog sync now.
  Without `Plugins:Catalog:Url`, it returns the status 409 with the code `catalog_not_configured`. A sync that fails returns the status 502 with the code `catalog_sync_failed`.
- `POST /api/v1/admin/plugin-catalog/{id}/{version}/install` installs a version from its `manifest.json` in `users.extension_files`.
  It applies the rules of `PUT /api/v1/admin/extensions/{id}`: a new plugin is disabled, and a revoked version returns `version_revoked`.
- The page uses `PATCH` and `DELETE /api/v1/admin/extensions/{id}` to enable, disable and uninstall a plugin.
- The buttons of the page are: **Install and enable**, **Update to** the last version, **Enable**, **Disable** and **Uninstall**.
- Then each user activates the plugin in the panel **Plugins** of the account page, with the consent dialog.

#### Revocation

The field `revoked` of the catalog index stops a plugin version on all instances after one sync.

1. The catalog sync adds each revocation to the table `users.extension_revocations`, in the transaction that keeps the index.
   A revocation is permanent.
   A later index without the revocation does not enable the version again, so a fixed plugin needs a new version.
2. The catalog sync disables each installed plugin whose version the catalog revoked.
   The sync does not get the files of a revoked version.
3. The API then refuses the revoked version:
   - `GET /api/v1/extensions` does not list it.
   - The frame endpoint and the file endpoint return the status 404.
   - `POST /api/v1/me/extensions/{id}/token` and `PUT /api/v1/me/extensions/{id}` return the status 404.
   - A plugin token of the version gets the status 401 at its next request.
   - `PATCH /api/v1/admin/extensions/{id}` cannot enable it, and `PUT /api/v1/admin/extensions/{id}` cannot install it. The error is `version_revoked`.
   - `GET /api/v1/admin/extensions` gives the reason in the field `revocationReason`.
4. While a plugin runs, the web app gets `GET /api/v1/extensions` each 10 minutes, and when the page becomes visible again.
   When the active plugins or their versions changed, the web app removes all the plugin frames and starts the active plugins again.
   Thus a revoked version stops in the browser 10 minutes after the sync at most.
   Before this check, the frame can run, but its plugin token gets the status 401.

### Consent

The user accepts the access of a plugin version before the plugin runs: the scopes, the network origins, the devices and the linked account sites.

- The **Plugins** panel of the account page lists the plugins that the instance enables.
  For each plugin, it shows the state for the user: active, not active or paused.
  The panel does not show when the instance enables no plugin.
- The consent dialog shows the access in plain words, in English and in French:
  - Each scope has the same text as in the **Delegations** panel.
  - A network origin shows as a site, or as all the sites of a domain for a wildcard.
    `localhost` and `127.0.0.1` show as a program on the computer of the user.
    The dialog also shows the origin of the manifest.
  - Each device and each linked account site has its own text.
- The table `content.plugin_activations` keeps the consent of the user: `accepted_version`, `accepted_scopes`, `accepted_network`, `accepted_devices`, `accepted_sites` and `accepted_at`.
- `PUT /api/v1/me/extensions/{id}` with `{ "enabled": true, "consent": { "version", "scopes", "network", "devices", "sites" } }` activates the plugin and keeps the consent.
  - The consent must give the installed version, and it must contain all the access of this version.
    Else the API refuses it with the status 409 and the code `consent_outdated`, because the dialog showed an older manifest.
  - Without `consent`, the API activates the plugin only when the last consent contains all the access of the installed version.
    Else it refuses the request with the status 409 and the code `consent_required`.
  - A deactivation keeps the consent.
- `GET /api/v1/extensions` gives for each plugin the access of the manifest (`permissions`, `network`, `devices` and `sites`), the last consent (`consent`) and the field `paused`.
- **New version**: the user activated a plugin, but the installed version asks for access that the last consent does not contain.
  The plugin then stays paused.
  - The comparison uses the same text. Thus a new wildcard origin needs a new consent, also when the user accepted a site of this domain.
  - A paused plugin does not run, and the API gives it no plugin token.
    The last consent does not change until the user accepts the new access.
  - The dialog then shows only the new access.
  - A new version that asks for the same access or for less access runs with no new consent.
- The accepted list controls the sandbox:
  - The runtime puts the accepted `network` origins and `devices` in the frame address.
    Thus the CSP of the frame allows only these values.
  - The plugin token has only the scopes that the user accepted.

### Layout and failures

The user controls where the contributions of the plugins show, and the core stops a plugin that fails.

- The column `settings` of the table `content.plugin_activations` keeps a JSON object for each user and each plugin:
  `position`, `hidden` and `disabledByCoreAt`.
- **Layout**: the panel **Plugins** of the account page has the form **Layout** for each active plugin.
  - `position` is a number from -1000 to 1000, with the default 0.
    In each slot, the contributions of the core come first. Then the contributions of the plugin with the smaller position come first.
  - `hidden` lists the contributions that the user hides, as `slot:id`, for example `deck.actions:sync` or `analysis.tabs:stats`. The list has 50 entries at most.
  - The user cannot hide a plugin page or a dialog of a plugin: a page is an address, and the plugin opens its dialogs.
  - `PUT /api/v1/me/extensions/{id}/layout` with `{ "position": 2, "hidden": [...] }` keeps the layout. The user must have activated the plugin one time.
  - `GET /api/v1/extensions` gives the layout in the field `layout`. The runtime gives it to the slot registry with `setPluginLayouts`.
    `SlotRegistry.list` applies it, and `SlotRegistry.listAll` also gives the hidden contributions for the form.
- **Failures**: `web/src/plugins/failures.ts` counts the failures of each plugin in the session storage of the browser.
  - A crash: the frame did not connect, or it did not answer 3 heartbeats.
  - A slow answer: a call of the core to the main frame got no answer in the time limit, for example `ui.action` or `ui.cells`.
  - After 3 crashes or 5 slow answers in one browser session, the web app deactivates the plugin:
    `PUT /api/v1/me/extensions/{id}` with `{ "enabled": false, "failed": true }`.
    The API keeps the time in `disabledByCoreAt`, and the next activation removes it.
  - The web app shows a notice that stays until the user closes it. The panel **Plugins** shows the date and the button **Activate**.
  - The failures of a development plugin do not count, because its author changes it all the time.

### Life cycle and security

1. **Development mode**: on a development instance, a user can load a plugin from a URL.
   The interface then shows a "not checked" banner.
   Deployed instances disable the development mode by default.
2. **Plugin catalog**: each plugin of the catalog has a public Git repository and a reproducible build.
   An automatic analysis rejects a plugin that contains hard-coded keys or access tokens.
   It also rejects a plugin that uses domains that its plugin manifest does not declare.
3. **Review and signature**: the project reviews each plugin, then signs the catalog index.
   The catalog index contains the SHA-384 hash of each plugin file.
   By default, Reliquary offers only the signed plugins that the administrator enabled for the instance.
4. **Permissions**: each permission has a scope, for example `decks:read` or `collection:write`.
   The user accepts the permissions when they install the plugin.
   Each new permission, new domain, new device or new linked account site needs a new approval from the user.
   The section [Consent](#consent) gives the rules.
5. **Revocation**: the project can revoke a compromised plugin version remotely.
   The worker gets the catalog index each hour and disables the revoked versions.

[ADR 0052](../decisions/0052-plugin-catalog-signature-and-revocation.md) gives the reasons for the catalog rules.

## Modules

Modules add advanced features without changes to the code of the core.
Modules are rare and signed, and the instance administrator installs them.

### Capabilities

- **On the server**: a module can add endpoints (`/api/modules/<id>/…`), receive and publish domain events, run scheduled tasks and accept incoming webhooks.
- **Actions for a user who is not connected**: for example, a module can synchronize an external account each night.
  A module can also process an import that comes from a webhook.
  For these actions, the user must give explicit consent: a delegation to the service account of the module.
  The user can revoke the consent in the **Delegations** panel of the account page.
- **In the front end**: a module can use the same slots as plugins, and it can add full pages and settings panels.
- **Services for plugins**: a module can give services to plugins.
  For example, a plugin can show data that a module calculates.

### Data

- Each module has its **own schema** (`mod_<id>`) with its **own SQL migrations**.
  The migration tool of the core also applies the migrations of the modules.
- A module never changes the tables of the core.
  It extends them with **extension tables**, which use the ID of the core entity as a link.
  Example: `mod_x.deck_extra(deck_id, …)`.
- The tables of a module follow the [split rules](./database.md): an `owner_id` on all user data, and UUIDv7 identifiers.
  Because of these rules, Reliquary can move the module data of a user to another database, with the other data of the user.
- When the administrator uninstalls a module, the core stays intact.
  The administrator can archive or delete the schema of the module.

### Execution

- **Preferred option**: each module is a **separate service** in its own container.
  The module communicates with the core through a versioned internal API and the event bus.
  The instance administrator can deploy and scale a module separately from the core.
  If a module fails, the core continues to run.
  Authors can write a module in C# or in TypeScript.
- **Alternative**: a module of the internal team with a close link to the core can be a .NET library.
  The core loads the library when it starts, behind the same interfaces.

## Prepare the core for modules (milestones 0 to 2)

The core applies to itself, from the start, the constraints that it will apply to modules.
With these constraints, the arrival of modules needs no rewrite of the core.
The core prepares these seven items:

1. **Modular monolith**: the core contains five core components (Accounts, Cards, Decks, Collection and Playtest).
   They communicate only through interfaces and domain events, never through the tables of another core component.
2. **Versioned domain events**: the core components publish domain events through an outbox table from milestone M0.
   They publish the events even when no consumer exists yet.
3. **Migration tool for several schema sets**: one tool applies the migrations of the core and of the `mod_*` schemas.
   The tool applies them to all the databases of the shard directory.
4. **Interface slots**: the core uses slots for its own panels.
   This use tests the slot system before the slots open to plugins and modules.
5. **Internal API**: a versioned internal API, separate from the public API, with an OpenAPI description.
   The [internal API](./internal-api.md) page describes it.
6. **Service accounts and delegation**: a model of delegation tokens.
   A delegation token means "act for user X, with this scope", and the user can revoke it.
   The `users` schema includes this model.
   The [service accounts](./service-accounts.md) page describes it.
7. **Activation for each instance**: a register of the installed extensions.
   For each extension, the register gives an activation flag and the minimum core version.

## Extension registry

The extension registry is ready from milestone M0, before any extension can run.
[ADR 0023](../decisions/0023-extension-registry.md) gives the reasons for the design.

- The administrator installs an extension from its manifest with `PUT /api/v1/admin/extensions/{id}`.
  The registry does not enable a new extension.
- The administrator enables or disables an extension, or changes its settings, with `PATCH /api/v1/admin/extensions/{id}`.
- The field `coreApi` of the manifest gives the versions of the core API that the extension supports, in the syntax of npm, for example `^1.0`.
  The API refuses an extension that does not support the current version, with the error `core_version_not_supported`.
  After an update of the core, an incompatible extension stays in the registry, but it does not run.
- `GET /api/v1/extensions` lists the enabled extensions and the current version of the core API.
  For an authenticated user, the field `active` tells if the extension runs for the user.
- A module runs when the instance enables it.
  A plugin runs when the instance enables it and the user activates it with `PUT /api/v1/me/extensions/{id}`.
- `GET /api/v1/status` also gives the version of the core API.

## Related decisions

- [ADR 0002](../decisions/0002-license-agpl-3.0.md): the license of the core
- [ADR 0007](../decisions/0007-modular-monolith.md): modular monolith for the core
- [ADR 0010](../decisions/0010-plugins-and-modules.md): two extension levels, plugins and modules
- [ADR 0011](../decisions/0011-plugin-network-allowlist.md): plugin network access through a declared list of domains
- [ADR 0023](../decisions/0023-extension-registry.md): the extension registry and the version of the core API
- [ADR 0036](../decisions/0036-internal-api.md): the internal API for the modules
- [ADR 0037](../decisions/0037-service-accounts-and-delegation-tokens.md): the service accounts and the delegation tokens
- [ADR 0048](../decisions/0048-plugin-sdk-license-and-packaging.md): the license and the place of the plugin SDK
- [ADR 0049](../decisions/0049-plugin-sandbox.md): the plugin sandbox and the plugin token
- [ADR 0050](../decisions/0050-no-plugin-code-on-the-server.md): no plugin code on the server, the vault and the network relay
- [ADR 0051](../decisions/0051-linked-accounts.md): linked accounts
- [ADR 0052](../decisions/0052-plugin-catalog-signature-and-revocation.md): the plugin catalog, its signature and the revocation
