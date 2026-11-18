# Devices of a plugin: camera and localhost

A plugin can use the camera and call a program on the computer of the user.
This page gives the rules and the manual check list for the browsers.
[Extensions](../architecture/extensions.md) gives the architecture.

## Rules

- The plugin manifest declares each device in the field `devices`:
  - `camera`: the camera of the user, for example to scan cards.
  - `loopback-network`: requests to `localhost` and `127.0.0.1`, for example to a scan application or a local tool.
- For `loopback-network`, the field `network` must also contain the origin, for example `http://localhost:7000`.
  The CSP of the plugin frame allows only the origins of this field.
- The user accepts the devices in the consent dialog. A new version that asks for a new device stays paused until the user accepts it.
- The core gives the accepted devices to the plugin frame:
  - The `allow` attribute of the iframe contains `camera` for the device `camera`.
  - It contains `loopback-network` and the older name `local-network-access` for the device `loopback-network`.
  - The `Permissions-Policy` header of the frame turns off all the other features.
- The browser then asks the user for the permission, with its own prompt. The prompt shows the address of the instance.
- `sdk.net.fetch` adds the fetch option `targetAddressSpace: 'loopback'` to a request to `localhost`, `127.0.0.0/8` or `[::1]`.
  Chromium needs this option for a request from an HTTPS page to an HTTP address of the computer.
- The local program must answer the CORS preflight and send `Access-Control-Allow-Origin: *`, because the frame has an opaque origin (`Origin: null`).
- A development plugin can use all the devices of its plugin manifest.

## Manual check list

No automatic test opens the permission prompts of the browsers. Do this check list before a release that changes the plugin frame, and when a browser changes its rules.

Use the last stable version of Chrome, Firefox and Safari, on an instance with HTTPS.
Use a development plugin with this plugin manifest, and a local program that answers on `http://localhost:7000` with `Access-Control-Allow-Origin: *`:

```json
{
  "id": "device-check",
  "name": "Device check",
  "version": "0.1.0",
  "coreApi": "^1.0",
  "entry": "main.js",
  "slots": ["plugin.page"],
  "network": ["http://localhost:7000"],
  "devices": ["camera", "loopback-network"],
  "panels": [{ "id": "check", "slot": "plugin.page", "title": "Device check" }]
}
```

The panel calls `navigator.mediaDevices.getUserMedia({ video: true })` and `sdk.net.fetch('http://localhost:7000/ping')`, and shows the two results.

| Step | Expected result |
| --- | --- |
| 1. Open the panel. | The browser asks for the camera, with the address of the instance. |
| 2. Allow the camera. | The panel shows the video. |
| 3. Wait for the request to `localhost`. | A browser with the permission `loopback-network` asks for the access to the applications on the device. Other browsers show no prompt. |
| 4. Allow the access. | The panel shows the answer of `http://localhost:7000/ping`. |
| 5. Remove `camera` from `devices`, and load the plugin again. | `getUserMedia` fails with `NotAllowedError`, and the browser shows no prompt. |
| 6. Remove `loopback-network` from `devices`, and load the plugin again. | In a browser with the permission `loopback-network`, the request to `localhost` fails. |
| 7. Remove `http://localhost:7000` from `network`, and load the plugin again. | The request fails, and the console shows a CSP error. |
| 8. Block the camera in the settings of the site. | The panel shows an error, and the page of the core works. |

Notes:

- Chromium has the permission `loopback-network` (Local Network Access). In a browser without this permission, only the CSP limits the requests to `localhost`.
- A browser can block a request from an HTTPS page to `http://localhost` as mixed content.
  When a browser blocks step 4, write this limit in the description of each plugin that needs a local program.
- A browser that does not know a feature of the `allow` attribute ignores it.

Record the result of each step for each browser, the browser versions and the date in the pull request or in the release issue.
