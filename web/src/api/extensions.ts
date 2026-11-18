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
  /** The import formats and the export formats of the plugin manifest. */
  formats: ExtensionFormat[]
  /** The network origins that the plugin manifest asks for. */
  network: string[]
  /** The devices that the plugin manifest asks for: "camera" and "loopback-network". */
  devices: string[]
  /** The host names of the linked account sites of the plugin manifest. */
  sites: string[]
  /** The access that the user accepted for the plugin, or null. */
  consent: PluginConsent | null
  /** True when the user activated the plugin, but the installed version asks for access that the user did not accept. */
  paused: boolean
  /** The position of the plugin and the contributions that the user hides, or null when the user never activated the plugin. */
  layout?: PluginLayout | null
  /** The time when the web app deactivated the plugin because it failed too many times, or null. */
  disabledByCoreAt?: string | null
}

/** The layout of a plugin for the user. See issue #278. */
export interface PluginLayout {
  /** The position of the contributions of the plugin among the contributions of the other plugins. A smaller number comes first. */
  position: number
  /** The hidden contributions, as "slot:id", for example "deck.actions:sync". */
  hidden: string[]
}

/** The access that the user accepted for a plugin version. */
export interface PluginConsent {
  version: string
  scopes: string[]
  network: string[]
  devices: string[]
  sites: string[]
  acceptedAt: string | null
}

/** An import format or an export format of a plugin manifest. */
export interface ExtensionFormat {
  id: string
  direction: 'import' | 'export'
  target: 'deck' | 'collection'
  /** The name in English. The translations are in `i18n` with the key `formats.<id>.title`. */
  title: string
  /** The file name extensions, for example ".dek". */
  extensions: string[]
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
