# Review check list of a plugin

A reviewer of the plugin catalog uses this check list for each new plugin and for each new version.
The review protects the users: an instance runs a plugin of the catalog with no other check.
[ADR 0049](../decisions/0049-plugin-sandbox.md) and [ADR 0052](../decisions/0052-plugin-catalog-signature-and-revocation.md) give the design of the sandbox and of the catalog.

Record the result of each item in the pull request of the catalog. Refuse the plugin when one necessary item fails.

## 1. Source and build

- [ ] The repository is public and has a license.
- [ ] The pull request gives one commit. The review reads this commit and no other commit.
- [ ] The repository has a lock file, and the build installs the packages from the lock file only.
- [ ] The two builds of the CI give the same file hashes.
- [ ] The built files contain no minified code without its source, and no file that the plugin does not use.
- [ ] A source map contains no secret.
- [ ] The secret scan of the CI finds no key and no access token.
- [ ] Check the provenance of the built files:

  ```sh
  gh attestation verify dist/index.js --repo nicolas-goyon/reliquary-plugins
  ```

  The command must name the workflow of the catalog and the commit of the pull request.

## 2. Plugin manifest

- [ ] The CI checks the plugin manifest against the JSON Schema of the SDK.
- [ ] The `id` is new, or the pull request comes from a maintainer of the plugin.
- [ ] The `name` and the `description` tell what the plugin does. They do not use the name of a different product in a way that misleads.
- [ ] Each scope of `permissions` is necessary. A plugin that only reads does not ask for a write scope.
- [ ] Each origin of `network` is necessary, and you know who operates it. Refuse a wildcard origin without a strong reason.
- [ ] Each device of `devices` is necessary.
- [ ] Each site of `accounts` agrees with an origin of `network`.

## 3. Difference with the last version

- [ ] Read the difference of the source code with the last accepted version.
- [ ] The pull request lists each new scope, origin, device and site, with the reason. The users must accept them again.
- [ ] A new maintainer of the plugin needs a second reviewer.

## 4. Data of the user

- [ ] Find each call of `sdk.api.fetch`. The plugin reads only the data that its function needs.
- [ ] Find each call of `sdk.net.fetch`, `fetch`, `XMLHttpRequest`, `WebSocket`, `EventSource` and `navigator.sendBeacon`. Each address is an origin of `network`.
- [ ] The plugin sends the data of the user to a site only when its description says so.
- [ ] The plugin keeps no secret in `sdk.storage`, in the code or in an address.
- [ ] The plugin does not ask the user for the password of Reliquary or of a different site.

## 5. Gaps of the sandbox

The CSP of the plugin frame does not control all the ways to send data. Search the source code and the built files for these items.

- [ ] **WebRTC**: no `RTCPeerConnection`, `RTCDataChannel` or `webkitRTCPeerConnection`. The CSP does not block WebRTC connections.
- [ ] **Navigation of the frame**: no `location.href =`, `location.assign`, `location.replace`, `window.open`, form `submit` or `<meta http-equiv="refresh">`.
  A frame can open its own address again with more `network` origins of its manifest. See the known limit in [Plugin frame and plugin files](../architecture/extensions.md#plugin-frame-and-plugin-files).
- [ ] **DNS and preloads**: no `<link rel="dns-prefetch">`, `preconnect`, `prefetch` or `prerender`.
- [ ] **Code from text**: no `eval`, `new Function`, `setTimeout` with a text, or `import()` of an address that is not a file of the plugin.
- [ ] **Workers**: only a classic Worker from a `blob:` URL, with code of the repository.
- [ ] **Images as a channel**: no image address that contains data of the user in its path or its query.
- [ ] **HTML from data**: no `innerHTML`, `outerHTML`, `insertAdjacentHTML` or `document.write` with data of the user or of a site.
- [ ] **Hidden text**: the code has no long encoded text (Base64, hexadecimal) without a clear reason.

## 6. Behavior

Install the built files on a test instance, activate the plugin, and use each of its functions.

- [ ] The consent dialog shows the access that the review accepted, and no more.
- [ ] The network panel of the browser shows requests only to the instance and to the origins of `network`.
- [ ] The plugin answers the heartbeats while it works. The core does not remove its frame.
- [ ] The plugin works in English and shows no broken text in French.
- [ ] The panels work with the keyboard and with a screen reader.
- [ ] A plugin with a device follows the [check list of the devices](devices.md#manual-check-list).

## 7. Decision

- [ ] Write the decision and its reasons in the pull request.
- [ ] After an approval, a maintainer approves the GitHub environment `signing`. The CI then signs and publishes the index.
- [ ] After a problem with a published version, add the version to the list `revoked` of the index, with a reason. The instances stop it after one sync.
