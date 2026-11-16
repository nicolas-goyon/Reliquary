# 0049. Plugin sandbox: sandboxed iframes, a CSP for each plugin and an RPC channel

- Status: Accepted
- Date: 2026-11-10
- Deciders: @nicolas-goyon

## Context

Epic [#59](https://github.com/nicolas-goyon/reliquary/issues/59) asks for plugins that change the interface without effects on the core or on other plugins.
Anyone can write a plugin ([ADR 0010](0010-plugins-and-modules.md)), so the core must treat plugin code as code that it does not trust.
A plugin can reach only the domains of its plugin manifest ([ADR 0011](0011-plugin-network-allowlist.md)).
A plugin must not get the access token or the refresh cookie of the user.

The options for the isolation of code in a browser are:

| Option | Boundary | Interface |
| --- | --- | --- |
| A membrane in the same JavaScript realm | Weak. Figma stopped this method in 2019 after sandbox escapes. | Direct |
| ShadowRealm | No confidentiality. The proposal is still at stage 2.7. | None |
| A JavaScript engine in WebAssembly, for example QuickJS | Strong | None. It needs a frame for the interface, and about 1 MB of WebAssembly. |
| A Web Worker that the core creates | None. The worker has the origin of the core. | None |
| A sandboxed iframe with an opaque origin, a CSP and messages | Strong. The browser enforces the origin boundary. | Yes |

Figma, VS Code webviews and MCP Apps use the last option.

## Decision

- **Plugin frame**: each active plugin runs in one iframe with `sandbox="allow-scripts"` only.
  The iframe has an opaque origin, so the plugin cannot read the cookies, the storage or the DOM of the core or of other plugins.
- **Panels**: each visible panel of a plugin has its own sandboxed iframe.
  The core creates a panel iframe when the user opens the panel.
- **Frame endpoint**: the API sends the HTML of the frame from `GET /api/v1/plugins/{id}/{version}/frame`, with a CSP header for this plugin.
  The core does not use `srcdoc`, because a `srcdoc` frame gets the CSP of the core page.
- **CSP of a plugin**: `default-src 'none'`.
  `script-src` contains only the files of the plugin, and the frame loads them with a SHA-384 integrity hash.
  `connect-src` and `img-src` contain only the `network` list that the user accepted.
- **Permissions Policy**: the `allow` attribute of the iframe is empty.
  It contains `camera` or `loopback-network` only when the plugin manifest declares the device and the user accepted it.
- **RPC**: the core and the plugin use JSON-RPC 2.0 on a `MessageChannel`.
  The core sends the port one time and checks `event.source`.
  Each call has a timeout (5 s by default) and a size limit (1 MB).
  The protocol version is the core API version ([ADR 0023](0023-extension-registry.md)).
- **Small slots are data**: for buttons, menu items, navigation items, table columns, import formats and export formats, the plugin sends data over RPC.
  The core renders this data with its own components.
  Only panels use iframes: analysis tabs, the card page, plugin pages and dialogs.
- **Plugin token**: the frame contains no credential of the user.
  The plugin calls the public API through the core.
  The core gets a plugin token from `POST /api/v1/me/extensions/{id}/token` and sends the request.
  - A plugin token is a JWT for 10 minutes, with the user, the plugin ID and the scopes that the user accepted.
  - The API checks the scope of the endpoint, as for a delegation token ([ADR 0037](0037-service-accounts-and-delegation-tokens.md)).
    An endpoint without a scope refuses a plugin token.
  - The API refuses a plugin token when the user deactivated the plugin.
- **Consent**: the user accepts the scopes, the domains and the devices of a plugin version.
  A new version that asks for more stays paused until the user accepts the difference.
- **Watchdog**: the core sends a heartbeat each 2 s and removes a frame after 3 missed heartbeats.
  The core disables a plugin that crashes or blocks the interface too often, and tells the user.
  A user can have 8 active plugins at most. An instance setting can change this limit.
- **Frame origin**: by default, the frames use the host of the instance with an opaque origin.
  The optional setting `Plugins:FrameOrigin` gives a second host name for the frames.

Rejected alternatives:

- A membrane in the same realm, ShadowRealm and QuickJS: see the context.
- React components of the plugin in the page of the core: no boundary.
- Delegation tokens for plugins: a plugin is not a service account, and a delegation token needs a secret in the database.
- An iframe in each table cell: too many frames. The core renders cells from data.

## Consequences

- The RPC protocol is a public contract. Each change follows the core API version.
- Requests from a frame have the header `Origin: null`.
  A site that does not send `Access-Control-Allow-Origin: *` needs the network relay ([ADR 0050](0050-no-plugin-code-on-the-server.md)).
- CSP does not control WebRTC. The review of the plugin catalog must check this.
- Chromium 132 and later runs each sandboxed frame in its own process.
  Firefox and Safari can run the frame in the process of the core, so a plugin with an endless loop can block the page.
  The SDK runs long tasks in a Worker, and the watchdog removes a frame that does not answer.
- Each plugin frame uses memory. The limit of active plugins controls this cost.
- The core needs no new dependency.

## Sources

- [Figma: how we built the Figma plugin system](https://www.figma.com/blog/how-we-built-the-figma-plugin-system/)
- [Evan Wallace: an update on plugin security](https://madebyevan.com/figma/an-update-on-plugin-security/)
- [Grafana: plugin frontend sandbox](https://grafana.com/docs/grafana/latest/administration/plugin-management/plugin-frontend-sandbox/)
- [TC39 ShadowRealm proposal](https://github.com/tc39/proposal-realms)
- [MCP SEP-1865: MCP Apps](https://modelcontextprotocol.io/seps/1865-mcp-apps-interactive-user-interfaces-for-mcp)
- [WebView2: isolated sandboxed iframes from version 132](https://github.com/MicrosoftEdge/WebView2Announcements/issues/99)
- [Chrome: Local Network Access](https://developer.chrome.com/docs/modern-web-guidance/guides/security/local-network-access)
