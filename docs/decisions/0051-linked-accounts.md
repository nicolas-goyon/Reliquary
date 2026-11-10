# 0051. Linked accounts: user name and API key first, OAuth later

- Status: Accepted
- Date: 2026-11-10
- Deciders: @nicolas-goyon

## Context

Epic [#61](https://github.com/nicolas-goyon/reliquary/issues/61) asks for linked accounts.
A plugin manifest declares external sites, each with an authentication method and a sync method.
The [extensions](../architecture/extensions.md) page gives three authentication methods: OAuth 2 with PKCE, an API key and a public user name.
A linked account is not a sign-in to Reliquary. The OAuth login of the Accounts component does this.

The Magic sites limit the choice:

- Moxfield has no public API ([ADR 0041](0041-deck-import-from-a-link.md)).
- Archidekt has an open API for public decks, but no OAuth.

Thus the first plugins need a public user name or an API key.
OAuth 2 with PKCE is the most work, and each self-hosted instance has its own redirect URI.

## Decision

- The table `content.account_links` keeps each linked account.
  Its columns are the owner, the plugin, the site, the authentication method, the encrypted secret and the key ID.
  More columns give the state, the last sync and the name of the external account.
- The vault of [ADR 0050](0050-no-plugin-code-on-the-server.md) encrypts the secrets.
- `GET /api/v1/me/extensions/{id}/accounts/{site}/secret` accepts only a plugin token of the plugin `{id}`.
  The interface of the core never reads the secret.
- The account page shows a **Linked accounts** panel. The core builds this panel from the `accounts` list of the active plugin manifests.
- M4 delivers the public user name and the API key first.
- OAuth 2 with PKCE comes at the end of M4 only if a real plugin needs it. Otherwise it moves to Later. When it comes:
  - The core opens the sign-in window, not the plugin.
  - The redirect URI is `/plugins/oauth/callback` on the instance.
  - The administrator gives the client ID of the instance in the settings of the extension.
- The core runs the sync, not the plugin.
  The core calls `account.sync(site)` on the plugin when the web app opens, on a click, or periodically while the web app is open.
  The minimum interval is 15 minutes. The core sends the state and the time of the sync to the API.
- The export of personal data ([ADR 0032](0032-personal-data.md)) lists the linked accounts without their secrets.
  An unlink or the deletion of the account deletes the secret.

Rejected alternatives:

- The plugin keeps the secret: the frame has no storage, and the secret must work on all devices of the user.
- One OAuth application for all instances: each instance has its own redirect URI.
- A sync on the server while the user is not connected: this needs a module ([ADR 0010](0010-plugins-and-modules.md)).

## Consequences

- A Moxfield plugin cannot sync private data. It can read only what the site makes public.
- The operator must keep the vault keys. The production guide and the Helm chart must tell this.
- OAuth stays possible later with no change to the table.

## Sources

- [RFC 7636: Proof Key for Code Exchange](https://www.rfc-editor.org/rfc/rfc7636)
- [RFC 9700: best current practice for OAuth 2.0 security](https://www.rfc-editor.org/rfc/rfc9700)
