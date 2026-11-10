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
5. The connect message has `development: true`, and the interface shows a "Not checked" banner for the plugin.
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
- Some sites refuse calls from a browser (CORS).
  For these sites, the core offers a network relay.
  The relay accepts only the domains of the plugin manifest, and it has quotas.
  The relay does not log the content of the requests.
  The relay is off by default, and the administrator enables it for the instance.

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

The frame response has these headers:

- `Content-Security-Policy`. `PluginFramePolicy` makes the policy for all the plugin frames:
  - `default-src 'none'`
  - `script-src`, `style-src` and `font-src`: the folder of the files of the plugin version. `style-src` also has `'unsafe-inline'`.
  - `img-src`: the folder of the files, the `network` list, `data:` and `blob:`
  - `connect-src`: the folder of the files and the `network` list
  - `worker-src blob:`. In a frame with an opaque origin, only a classic Worker from a `blob:` URL works.
  - `base-uri 'none'` and `form-action 'none'`
- `Cache-Control: no-store`, `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer`

A file response has these headers:

- `Access-Control-Allow-Origin: *`. The frame has an opaque origin, so its requests have the header `Origin: null`. A script with an integrity hash needs this header.
- `Content-Security-Policy: sandbox; default-src 'none'`. A browser that opens a file directly gives it an opaque origin, so a plugin file cannot run on the origin of the instance.
- `X-Content-Type-Options: nosniff`, the media type of the file, and an `ETag` with the SHA-384 hash

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
   Each new permission or new domain needs a new approval from the user.
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
