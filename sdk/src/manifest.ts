/**
 * The types of the plugin manifest. See docs/architecture/extensions.md.
 *
 * TODO(#281): make these types from sdk/schema/manifest.schema.json when the schema is in the repository.
 * The schema is the reference. These types follow the example of docs/architecture/extensions.md.
 */

import type { SlotName } from './slots.ts'

/** The authentication method of a linked account. */
export type AccountAuth = 'oauth2-pkce' | 'api-key' | 'public-username'

/** The synchronization method of a linked account. */
export type AccountSync =
  | { mode: 'manual' }
  | { mode: 'on-open' }
  /** A periodic sync while the web interface is open. The minimum interval is 15 minutes. */
  | { mode: 'periodic'; intervalMinutes: number }

/** An external site that the plugin uses as a linked account. */
export interface ManifestAccount {
  /** The domain of the site, for example "moxfield.com". */
  site: string
  auth: AccountAuth
  sync: AccountSync
}

/** The plugin manifest: the file `manifest.json` of a plugin. */
export interface PluginManifest {
  /** The ID of the plugin: 2 to 64 lowercase letters, digits and hyphens. */
  id: string
  /** The name that the interface shows. */
  name: string
  /** The version of the plugin, in the Semantic Versioning format. */
  version: string
  /** The versions of the core API that the plugin supports, in the range syntax of npm, for example "^1.0". */
  coreApi: string
  /** The scopes that the plugin asks for, for example "decks:read". */
  permissions?: string[]
  /** The slots that the plugin uses. */
  slots?: SlotName[]
  /** The origins that the plugin can reach, for example "https://api.moxfield.com". */
  network?: string[]
  /** The external sites of the linked accounts. */
  accounts?: ManifestAccount[]
  /** The path of the JavaScript entry point in the plugin files, for example "dist/index.js". */
  entry: string
}
