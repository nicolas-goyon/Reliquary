/**
 * The plugin frame: a sandboxed iframe with an opaque origin. See docs/decisions/0049-plugin-sandbox.md.
 */

export interface PluginVersion {
  id: string
  version: string
}

/**
 * Gets the address of the frame HTML of a plugin version.
 * @param frameOrigin The origin of the frames from the setting `Plugins:FrameOrigin`, or null for the host of the instance.
 */
export function frameUrl(plugin: PluginVersion, frameOrigin: string | null): string {
  const path = `/api/v1/plugins/${encodeURIComponent(plugin.id)}/${encodeURIComponent(plugin.version)}/frame`
  return frameOrigin ? `${frameOrigin.replace(/\/+$/, '')}${path}` : path
}

/**
 * Creates the iframe of a plugin. With a panel title, the frame is visible: it is the frame of a panel.
 * - `sandbox="allow-scripts"` only: the frame has an opaque origin, so it cannot read the cookies, the storage or the DOM of the core.
 * - The `allow` attribute is empty: the frame gets no device. Issue #290 adds the devices that the user accepts.
 * - The frame sends no referrer.
 */
export function createPluginFrame(document: Document, plugin: PluginVersion, src: string, panelTitle?: string): HTMLIFrameElement {
  const frame = document.createElement('iframe')
  frame.setAttribute('sandbox', 'allow-scripts')
  frame.setAttribute('allow', '')
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
