# 0052. Plugin catalog: a signed index, file hashes and revocation

- Status: Accepted
- Date: 2026-11-10
- Deciders: @nicolas-goyon

## Context

Epic [#62](https://github.com/nicolas-goyon/reliquary/issues/62) asks for a plugin catalog with a review, a signature and a remote revocation.
Each instance must check that a plugin file comes from the catalog and did not change.
The project has one maintainer, and the core must stay without new dependencies.

The options for the signature are:

| Option | Runtime dependency of the core | Cost |
| --- | --- | --- |
| Sigstore keyless signatures, with Fulcio and Rekor | A Sigstore library and network access to the transparency log | High |
| minisign or Ed25519 | .NET 10 has no Ed25519. The API needs a NuGet package. | Low |
| ECDSA P-256 on one index file | None. .NET has `ECDsa`, and `openssl` signs. | Lowest |

## Decision

- The catalog is the separate repository `nicolas-goyon/reliquary-plugins`.
  It has one folder for each plugin, with the source repository, the commit, the version and the maintainers.
- The CI of the catalog checks each submission:
  1. It builds the plugin two times in a pinned container and compares the hashes (reproducible build).
  2. It scans the files for secrets.
  3. It checks the plugin manifest against the JSON Schema of the SDK.
  4. It compares the URLs in the files with the `network` list. The CSP is the real limit, so this check is a second line.
- After the review, a maintainer approves the GitHub environment `signing`.
  Then the CI writes `index.json` and `index.json.sig` and publishes them with the plugin files.
- The CI adds GitHub artifact attestations for the reviewers. The instances do not check them.
- **Index**: `index.json` contains:
  - `serial`, a number that increases at each publication
  - `expires`, a date 30 days after the signature. A scheduled job signs the index again before this date.
  - `keyId`, the ID of the signing key
  - `plugins`, with the versions, the files and the SHA-384 hash of each file
  - `revoked`, the revoked versions with a reason
- **Signature**: one ECDSA P-256 signature covers the canonical bytes of `index.json`.
  The core trusts the public keys of the setting `Plugins:CatalogKeys`. The key of the project is the default value.
- **Instance**: a scheduled job of the worker ([ADR 0019](0019-hosts-and-scheduled-jobs.md)) gets the index each hour.
  1. It refuses an index with a bad signature, a smaller `serial` or a past `expires` date.
  2. It gets the new files and checks their hashes.
  3. It keeps the files in the database.
  4. It disables the revoked versions. The web app stops the frame of a revoked version at its next check.
- **Offer**: by default, a user sees only the signed plugins that the administrator enabled for the instance.
  A plugin from a URL needs the development mode, which only development instances enable.

Rejected alternatives:

- Sigstore at runtime: complex checks and an online dependency.
- minisign or Ed25519: one more NuGet package on the server.
- One signature for each file: the hashes in the signed index give the same protection with one signature.
- No signature, with HTTPS from GitHub only: the instance then trusts each person with write access to the repository.

## Consequences

- The signing key is a single point of failure.
  The key stays in a secret of the GitHub environment, with a required reviewer and an offline copy.
- An instance that cannot reach the catalog for 30 days refuses the index and keeps the plugins that it has.
- The review of all plugins is work for one maintainer.
- Some bundlers do not give reproducible builds. A plugin must have a lockfile and no timestamps in its files.

## Sources

- [The Update Framework specification](https://theupdateframework.io/specification)
- [dotnet/runtime#14741: Ed25519](https://github.com/dotnet/runtime/issues/14741)
- [GitHub artifact attestations](https://docs.github.com/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds)
- [Sigstore](https://docs.sigstore.dev/)
