# 0050. No plugin code on the server, and a vault for the secrets of plugins

- Status: Accepted. Amends [0011](0011-plugin-network-allowlist.md).
- Date: 2026-11-10
- Deciders: @nicolas-goyon

## Context

[ADR 0010](0010-plugins-and-modules.md) says that plugins run only in the browser.
Some features of M4 need the server:

- The network relay for the sites that refuse calls from a browser ([ADR 0011](0011-plugin-network-allowlist.md)).
- The secrets of linked accounts ([ADR 0051](0051-linked-accounts.md)).
- The files of the plugins and the sync of the plugin catalog ([ADR 0052](0052-plugin-catalog-signature-and-revocation.md)).

A plugin runtime on the server can run code of each plugin for these needs.
The options are a WebAssembly host (Extism or wasmtime for .NET), a JavaScript engine in .NET, or one container for each plugin.
A WebAssembly host adds a native library and a package that follows its upstream project late.
One container for each plugin is the model of modules.

## Decision

- The server runs no plugin code. ADR 0010 stays.
- The server parts of M4 are core code that the plugin manifest controls:
  1. The store of plugin files and the frame endpoint ([ADR 0049](0049-plugin-sandbox.md)).
  2. The plugin token.
  3. The network relay.
  4. The vault.
  5. The catalog sync.
- **Network relay**: `POST /api/v1/plugins/{id}/relay` accepts only a plugin token.
  - The relay accepts only HTTPS URLs whose origin is in the `network` list that the user accepted.
  - The relay refuses private, loopback and link-local addresses. It checks the resolved address at connection time, to stop DNS rebinding.
  - The relay has a body limit of 1 MB, a timeout of 10 s and a quota in Redis.
  - The relay sends no cookies and logs no body.
  - The relay is off by default. The administrator enables it for the instance.
- **Vault**: the API encrypts each secret of a plugin with AES-GCM from .NET.
  - The setting `Plugins:VaultKeys` gives the keys. Each key has a key ID, so the operator can add a new key and keep the old keys for reading.
  - The associated data is the owner ID, the plugin ID and the site. A secret thus decrypts only for its owner, its plugin and its site.
  - Only a plugin token of the same plugin can read a secret. A web session cannot read it.

Rejected alternatives:

- A WebAssembly host or a JavaScript engine on the server: see the context.
- One container for each plugin: this is a module.
- ASP.NET Core Data Protection for the vault, with its keys in Redis: a loss of Redis data makes all secrets unreadable.
  A restore of the database does not help.
- Secrets in the browser: the frame has an opaque origin and no storage, and the secrets must work on all devices of the user.

## Consequences

- M4 has no server plugins.
  A feature that needs code on the server is a module.
- The operator must keep the vault keys. Without the keys, the secrets of linked accounts are lost.
  The backup of the database does not contain the keys.
- The relay is a surface for server-side request forgery. Its tests must cover each refused address.
- This ADR changes ADR 0011 on one point: the relay exists, but it is off by default.

## Sources

- [NuGet: Wasmtime](https://www.nuget.org/packages/Wasmtime)
- [.NET: `AesGcm` class](https://learn.microsoft.com/dotnet/api/system.security.cryptography.aesgcm)
- [OWASP: server-side request forgery prevention](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
