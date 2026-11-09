# Shared vectors of the player views

The web tests in `web/src/playtest/views.test.ts` write these files.
The C# tests of the online games replay them, so that the C# views, hashes and patches agree with the TypeScript code.
Issue [#228](https://github.com/nicolas-goyon/reliquary/issues/228) and [ADR 0047](../../../docs/decisions/0047-trusted-server-for-online-games.md) give the design.

| File | Items | Check in C# |
| --- | --- | --- |
| `canonical-json.json` | `value`, `canonicalJson`, `hash` | The canonical JSON of `value` is equal to `canonicalJson`, byte for byte. Its hash is equal to `hash`. |
| `views.json` | `viewerId`, `view`, `canonicalJson`, `hash` | The canonical JSON of `view` is equal to `canonicalJson`. Its hash is equal to `hash`. |
| `projections.json` | `state`, `hashes` | For each viewer of `hashes`, the view of the full `state` has the hash of `hashes`. |
| `patches.json` | `viewerId`, `before`, `patch`, `after` (every 6th item), `afterHash` | The diff of `before` and `after` is equal to `patch`. `before` with `patch` applied has the hash `afterHash`. |

The C# tests are in `tests/Reliquary.Playtest.Tests/PlayerViewsTests.cs`.
`canonical-json.json` has a lone surrogate (`\udc00`). The JSON reader of .NET does not accept it, so the C# tests read the files with a small reader of their own.

The doc comments of `canonicalJson`, `fnv1a64` and `diffViews` in `web/src/playtest/views.ts` give the rules.
Each hash is the 64-bit FNV-1a hash of the UTF-8 bytes of the canonical JSON, as 16 lowercase hexadecimal digits.

To write the files again after a change of the rules, run `npx vitest run -u src/playtest/views.test.ts` in the `web` folder.
Without `-u`, the tests fail when a file is different.
