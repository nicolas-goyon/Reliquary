/**
 * The extension registry, as `GET /extensions` gives it. See docs/architecture/extensions.md.
 */

/** An extension that the instance enables. */
export interface Extension {
  id: string
  kind: 'plugin' | 'module'
  name: string
  version: string
  coreApi: string
  permissions: string[]
  slots: string[]
  enabled: boolean
  /** True when the extension supports the current core API. */
  compatible: boolean
  /** True when the extension runs for the user. */
  active: boolean
  /** The panels of the plugin manifest. */
  panels: ExtensionPanel[]
  /** The translations of the plugin manifest, by language, or null. */
  i18n: Record<string, Record<string, string>> | null
}

/** A panel of a plugin manifest. The web app shows each open panel in its own sandboxed iframe. */
export interface ExtensionPanel {
  id: string
  /** "analysis.tabs", "card.page", "plugin.page" or "plugin.dialog". */
  slot: string
  /** The title in English. The translations are in `i18n` with the key `panels.<id>.title`. */
  title: string
}

/** The settings of the plugin runtime of the instance. */
export interface PluginRuntimeSettings {
  /** The maximum number of active plugins for each user. */
  maxActive: number
  /** The origin of the plugin frames, or null for the host of the instance. */
  frameOrigin: string | null
  /** True when a user can load a plugin from a URL: the development mode. */
  developmentPlugins: boolean
}

/** The response of `GET /extensions`. */
export interface ExtensionList {
  coreApi: string
  items: Extension[]
  plugins: PluginRuntimeSettings
}

/** Gets the plugins that run for the user. */
export function activePlugins(list: ExtensionList): Extension[] {
  return list.items.filter((item) => item.kind === 'plugin' && item.active && item.compatible)
}
