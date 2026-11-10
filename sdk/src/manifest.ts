/**
 * The types of the plugin manifest. See docs/architecture/extensions.md.
 *
 * The reference is the JSON Schema sdk/schema/manifest.schema.json.
 * The test manifest.test.ts fails when these types and the schema do not have the same fields and values.
 * The schema also gives the patterns and the limits of the values.
 */

/** The scopes of the public API that a plugin can ask for. */
export const manifestPermissions = ['decks:read', 'decks:write', 'collection:read', 'collection:write', 'profile:read'] as const
export type ManifestPermission = (typeof manifestPermissions)[number]

/** The devices that a plugin can use. */
export const manifestDevices = ['camera', 'loopback-network'] as const
export type ManifestDevice = (typeof manifestDevices)[number]

/** The authentication methods of a linked account. */
export const accountAuthMethods = ['public-username', 'api-key', 'oauth2-pkce'] as const
export type AccountAuth = (typeof accountAuthMethods)[number]

/** The synchronization modes of a linked account. */
export const accountSyncModes = ['manual', 'on-open', 'periodic'] as const
export type AccountSyncMode = (typeof accountSyncModes)[number]

/** When the core starts a sync of a linked account. */
export interface AccountSync {
  /** manual: on a click of the user. on-open: when the web app opens. periodic: periodically while the web app is open. */
  mode: AccountSyncMode
  /** The interval of a periodic sync, in minutes: 15 to 1440. */
  intervalMinutes?: number
}

/** An external site where the user links an account for the plugin. */
export interface ManifestAccount {
  /** The host name of the site, for example "moxfield.com". */
  site: string
  auth: AccountAuth
  /** Without this field, the sync is manual. */
  sync?: AccountSync
}

/** The slots of a panel. */
export const panelSlots = ['analysis.tabs', 'card.page', 'plugin.page', 'plugin.dialog'] as const
export type PanelSlot = (typeof panelSlots)[number]

/** A panel of the plugin. Each panel renders in its own sandboxed iframe. */
export interface ManifestPanel {
  /** 1 to 32 lowercase letters, digits and hyphens. The first character is a letter. */
  id: string
  /** The slot of the panel. The field `slots` must also contain it. */
  slot: PanelSlot
  /** The title in English. The field `i18n` gives the translations with the key `panels.<id>.title`. */
  title: string
}

export const formatDirections = ['import', 'export'] as const
export type FormatDirection = (typeof formatDirections)[number]

export const formatTargets = ['deck', 'collection'] as const
export type FormatTarget = (typeof formatTargets)[number]

/** An import format or an export format of the plugin. */
export interface ManifestFormat {
  /** 1 to 32 lowercase letters, digits and hyphens. The first character is a letter. */
  id: string
  direction: FormatDirection
  target: FormatTarget
  /** The name in English. The field `i18n` gives the translations with the key `formats.<id>.title`. */
  title: string
  /** The file name extensions, for example ".dek". */
  extensions?: string[]
}

/** The plugin manifest: the file `manifest.json` of a plugin. */
export interface PluginManifest {
  /** The URL of the JSON Schema, for the editors. */
  $schema?: string
  /** The ID of the plugin: 2 to 64 lowercase letters, digits and hyphens. */
  id: string
  /** A plugin manifest can omit the kind. */
  kind?: 'plugin'
  /** The name in English. */
  name: string
  /** A short description in English. */
  description?: string
  /** The version, for example "1.0.0" or "1.0.0-beta.1". */
  version: string
  /** The versions of the core API that the plugin supports, in the range syntax of npm, for example "^1.0". */
  coreApi: string
  /** The path of the JavaScript module of the plugin, for example "dist/index.js". */
  entry: string
  /** The scopes of the public API that the plugin asks for. */
  permissions?: ManifestPermission[]
  /** The interface slots that the plugin uses, for example "deck.actions". */
  slots?: string[]
  /** The origins that the plugin can reach, for example "https://api.moxfield.com" or "http://localhost:8080". */
  network?: string[]
  devices?: ManifestDevice[]
  accounts?: ManifestAccount[]
  panels?: ManifestPanel[]
  formats?: ManifestFormat[]
  /** The translations for each language, for example `{ fr: { name: "…", "panels.stats.title": "…" } }`. */
  i18n?: Record<string, Record<string, string>>
  /** The default settings of the plugin for the instance. */
  settings?: Record<string, unknown>
}
