# 0048. Plugin SDK under Apache 2.0, in the folder `sdk/` of the main repository

- Status: Accepted. Answers the open question of [0002](0002-license-agpl-3.0.md).
- Date: 2026-11-10
- Deciders: @nicolas-goyon

## Context

Issue [#58](https://github.com/nicolas-goyon/reliquary/issues/58) asks for the license of the plugin SDK.
The core uses AGPL-3.0 ([ADR 0002](0002-license-agpl-3.0.md)).
Each plugin includes the SDK in its bundle.
With an AGPL-3.0 SDK, each plugin becomes a derived work, and its author cannot choose a license.

The two options of the issue are MIT and Apache 2.0:

- Apache 2.0 gives an explicit patent license, and it stops this license for a party that starts a patent lawsuit.
- MIT is shorter and more common in the JavaScript ecosystem, but it gives no explicit patent license.
- Both licenses are compatible with AGPL-3.0, so the core can use the SDK.

The SDK, the RPC protocol of the sandbox and the core API version change together during M4.
A separate repository makes a difference between these three parts likely.

## Decision

- The plugin SDK uses the Apache License 2.0.
- The SDK is in the folder `sdk/` of the main repository.
  The file `sdk/LICENSE` contains the Apache 2.0 text.
  The file `LICENSING.md` at the root tells which folders use which license.
- The JSON Schema of the plugin manifest is part of the SDK, with the same license.
- A contribution to `sdk/` uses the Apache 2.0 license.
- The CI of the main repository publishes the SDK to npm as `reliquary-plugin-sdk`.
  release-please gives its version ([ADR 0043](0043-releases-with-release-please.md)).
- The SDK has no runtime dependency.

Rejected alternatives:

- MIT: it gives no explicit patent license.
- AGPL-3.0 for the SDK: each plugin must then use AGPL-3.0.
- A separate repository now: the protocol, the SDK and the core change together in one pull request during M4.
  The project can move the SDK to its own repository after version 1.0.

## Consequences

- Plugin authors can use any license, including a closed license.
- The main repository has two licenses. `LICENSING.md` and the contributing guide must tell the limit.
- The release workflow publishes one more package.

## Sources

- [Apache 2.0 and GPL compatibility](https://www.apache.org/licenses/GPL-compatibility.html)
- [Grafana `LICENSING.md`](https://github.com/grafana/grafana/blob/main/LICENSING.md): an AGPL repository with Apache 2.0 packages
