# Extensions

This file describes the two levels of extensions: plugins and modules.
It also gives the work that prepares the core for modules.
Terms follow the [glossary](../glossary.md).

## Two levels

Reliquary has two levels of extensions:

- **Plugins**: anyone can write a plugin.
  Plugins run only in the browser.
- **Modules**: modules are few and trusted.
  They run in the front end and on the server, and they can extend the data.

Plugins arrive in milestone M4.
Modules come later, but the core prepares for them from milestone M0.

| Aspect | Plugin | Module |
| --- | --- | --- |
| Where it runs | Only in the browser, in a sandbox | In the front end and on the server |
| Who can write it | Anyone | The internal team or selected partners |
| Who installs it | Each user, for their own account | The instance administrator |
| Access to data | The data of the user, through the public API, with the permissions that the user gives | Access on the server. A module can add its own tables and fields. |
| Acts without a connected user | No. A plugin acts only when the app is open. | Yes, for example with scheduled synchronizations and webhooks |
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

- The plugin manifest declares the external sites that the plugin connects to.
  For each site, it declares an authentication method and a synchronization method.
  - Authentication methods: OAuth 2 with PKCE, an API key, or a public account name.
  - Synchronization methods: manual, when the app opens, or periodic while the app is open.
- The account page of the user shows a **Linked accounts** panel.
  Reliquary builds this panel from the installed plugins.
  For each site, the panel shows the site, the plugin that uses the site, the connection state and the last synchronization.
  The panel also has buttons to link and unlink the account.
- The core encrypts the access tokens and keeps them in its vault.
  Each access token belongs to one user and one plugin.
  Only this plugin can read the access token again.
  When the user unlinks the account, the core deletes the access tokens.

### Interface slots

- The interface has named places for extensions: the **slots**.
  The slots include the action bar of a deck, menus, analysis tabs and the columns of the table mode.
  They also include the card page, the account page and the navigation entries.
- A plugin declares in its plugin manifest the slots that it uses.
  In these slots, it adds buttons, charts, interface elements or a new menu category.
- The core renders each contribution in its own isolated container: a sandboxed iframe, or an interface component that the SDK loads.
  A plugin cannot access the DOM of the core or the DOM of other plugins.
- The core keeps control of the layout:
  - Each slot has a maximum size.
  - The user can change the order of the contributions and hide them.
  - The core disables a plugin automatically if the plugin crashes or slows the interface.

### Network

- A plugin can reach only the domains that its plugin manifest declares.
  These domains can include `localhost`, for example for a scan application or a local tool.
- Some sites refuse calls from a browser (CORS).
  For these sites, the core offers a network relay.
  The relay accepts only the domains of the plugin manifest, and it has quotas.
  The relay does not log the content of the requests.

[ADR 0011](../decisions/0011-plugin-network-allowlist.md) gives the reasons for these rules.

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

### Life cycle and security

1. **Development mode**: anyone can load a plugin from a local folder or from a URL.
   The interface then shows a "not checked" banner.
2. **Plugin catalog**: each plugin of the catalog has a public Git repository and a reproducible build.
   An automatic analysis rejects a plugin that contains hard-coded keys or tokens, or domains that its manifest does not declare.
3. **Review and signature**: the project reviews each plugin, then signs it.
   By default, Reliquary offers only signed plugins.
   The other plugins need an explicit approval from the user.
4. **Permissions**: each permission has a scope, for example `decks:read` or `collection:write`.
   The user accepts the permissions when they install the plugin.
   Each new permission or new domain needs a new approval from the user.
5. **Revocation**: the project can revoke a compromised plugin version remotely.

## Modules

Modules add advanced features without changes to the code of the core.
Modules are rare and signed, and the instance administrator installs them.

### Capabilities

- **On the server**: a module can add endpoints (`/api/modules/<id>/…`), receive and publish domain events, run scheduled tasks and accept incoming webhooks.
- **Actions for a user who is not connected**: for example, a module can synchronize an external account each night.
  A module can also process an import that comes from a webhook.
  For these actions, the user must give explicit consent.
  The user can revoke the consent in the **Linked accounts** panel.
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
  Thus Reliquary can move the module data of a user to another database, with the other data of the user.
- When the administrator uninstalls a module, the core stays intact.
  The administrator can archive or delete the schema of the module.

### Execution

- **Preferred option**: each module is a **separate service** in its own container.
  The module communicates with the core through a versioned internal API and the event bus.
  An operator can deploy and scale a module separately from the core.
  If a module fails, the core continues to run.
  Authors can write a module in C# or in TypeScript.
- **Alternative**: an internal module with a close link to the core can be a .NET library.
  The core loads the library when it starts, behind the same interfaces.

## Prepare the core for modules (milestones M0 to M2)

The core applies to itself, from the start, the constraints that it will apply to modules.
Thus the arrival of modules needs no rewrite of the core.
The core prepares these seven items:

1. **Modular monolith**: the core has separate core components: Accounts, Cards, Decks, Collection and Playtest.
   They communicate only through interfaces and domain events, never through the tables of another core component.
2. **Versioned domain events**: the core components publish domain events through an outbox table from milestone M0.
   They publish the events even when no consumer exists yet.
3. **Migration tool for several schema sets**: one tool applies the migrations of the core and of the `mod_*` schemas.
   The tool applies them to all the databases of the shard directory.
4. **Interface slots**: the core uses slots for its own panels.
   Thus the project tests the slot system in real use before the slots open to extensions.
5. **Internal API**: a versioned internal API, separate from the public API, with an OpenAPI description.
6. **Service accounts and delegation**: a model of delegation tokens.
   A delegation token means "act for user X, with this scope", and the user can revoke it.
   The `users` schema contains this model from the start.
7. **Activation for each instance**: a register of the installed extensions, with activation flags and the minimum core version of each extension.

## Related decisions

- [ADR 0007](../decisions/0007-modular-monolith.md): modular monolith for the core
- [ADR 0010](../decisions/0010-plugins-and-modules.md): two extension levels, plugins and modules
- [ADR 0011](../decisions/0011-plugin-network-allowlist.md): plugin network access through a declared list of domains
- [ADR 0002](../decisions/0002-license-agpl-3.0.md): the license of the core and the open question about the license of the plugin SDK
