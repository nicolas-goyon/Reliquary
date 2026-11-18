/**
 * The plugin frame: a sandboxed iframe with an opaque origin. See docs/decisions/0049-plugin-sandbox.md.
 */

export interface PluginVersion {
  id: string
  version: string
}

/** The network origins and the devices that the user accepted for a plugin. The CSP of the frame allows only these values. */
export interface FrameAccess {
  network: string[]
  devices: string[]
}

/**
 * Gets the address of the frame HTML of a plugin version.
 * The frame request has no credential, so the address gives the accepted network origins and devices to the API.
 * The API refuses a value that is not in the plugin manifest. Without a value, the frame gets no network origin and no device.
 * @param frameOrigin The origin of the frames from the setting `Plugins:FrameOrigin`, or null for the host of the instance.
 * @param access The network origins and the devices that the user accepted.
 */
export function frameUrl(plugin: PluginVersion, frameOrigin: string | null, access?: FrameAccess): string {
  const path = `/api/v1/plugins/${encodeURIComponent(plugin.id)}/${encodeURIComponent(plugin.version)}/frame`
  const query = new URLSearchParams()
  for (const origin of access?.network ?? []) {
    query.append('network', origin)
  }
  for (const device of access?.devices ?? []) {
    query.append('devices', device)
  }
  const search = query.size > 0 ? `?${query.toString()}` : ''
  return frameOrigin ? `${frameOrigin.replace(/\/+$/, '')}${path}${search}` : `${path}${search}`
}

/**
 * The features of the browser that each device of the plugin manifest gives to the frame, for the `allow` attribute.
 * Chrome first called the access to `localhost` "local-network-access", then "loopback-network". The API has the same list in `PluginFramePolicy`.
 */
const deviceFeatures: Record<string, string[]> = {
  camera: ['camera'],
  'loopback-network': ['loopback-network', 'local-network-access'],
}

/**
 * Gets the `allow` attribute of a frame from its address: the features of the devices in the query of the address.
 * The API makes the `Permissions-Policy` header from the same query, so the attribute and the header always agree.
 * An address with no device gives an empty attribute.
 */
export function frameAllow(src: string): string {
  let devices: string[]
  try {
    devices = new URL(src, 'https://reliquary.invalid').searchParams.getAll('devices')
  } catch {
    return ''
  }
  return [...new Set(devices.flatMap((device) => deviceFeatures[device] ?? []))].join('; ')
}

/**
 * Creates the iframe of a plugin. With a panel title, the frame is visible: it is the frame of a panel.
 * - `sandbox="allow-scripts"` only: the frame has an opaque origin, so it cannot read the cookies, the storage or the DOM of the core.
 * - The `allow` attribute gives only the devices that the user accepted. Without a device, it is empty.
 * - The frame sends no referrer.
 */
export function createPluginFrame(document: Document, plugin: PluginVersion, src: string, panelTitle?: string): HTMLIFrameElement {
  const frame = document.createElement('iframe')
  frame.setAttribute('sandbox', 'allow-scripts')
  frame.setAttribute('allow', frameAllow(src))
  frame.setAttribute('referrerpolicy', 'no-referrer')
  if (panelTitle === undefined) {
    // The main frame of a plugin has no interface.
    frame.setAttribute('aria-hidden', 'true')
    frame.tabIndex = -1
    frame.hidden = true
  }
  frame.title = panelTitle ?? plugin.id
  frame.dataset.plugin = plugin.id
  frame.src = src
  return frame
}
