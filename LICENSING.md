# Licensing

The Reliquary repository uses two licenses.

| Folder | License | File |
| --- | --- | --- |
| `sdk/` | Apache License 2.0 | [sdk/LICENSE](sdk/LICENSE) |
| All other folders | GNU Affero General Public License v3.0 | [LICENSE](LICENSE) |

The folder `sdk/` contains the plugin SDK and the JSON Schema of the plugin manifest.
Each plugin includes the SDK in its bundle.
With the Apache License 2.0, the author of a plugin can select any license for the plugin, also a closed license.
[ADR 0048](docs/decisions/0048-plugin-sdk-license-and-packaging.md) gives the reasons for this decision.

A contribution to `sdk/` uses the Apache License 2.0.
A contribution to a different folder uses the AGPL-3.0.
