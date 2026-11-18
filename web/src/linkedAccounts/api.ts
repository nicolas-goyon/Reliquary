/**
 * The linked accounts, as `GET /me/linked-accounts` gives them. See docs/decisions/0051-linked-accounts.md.
 */

/** The authentication methods of a site. */
export type LinkedAccountAuth = 'public-username' | 'api-key' | 'oauth2-pkce'

/** The sync modes of a site. */
export type LinkedAccountSyncMode = 'manual' | 'on-open' | 'periodic'

/** The states of a linked account. */
export type LinkedAccountState = 'linked' | 'synced' | 'failed'

/** A linked account, without its secret. */
export interface LinkedAccountLink {
  userName: string | null
  hasSecret: boolean
  /** False when the instance lost the vault key of the secret. The user must link the account again. */
  secretReadable: boolean
  state: LinkedAccountState
  linkedAt: string
  lastSyncAt: string | null
  lastSyncError: string | null
}

/** An external site of a plugin, with the linked account of the user, or no linked account. */
export interface LinkedAccountSite {
  pluginId: string
  /** The name of the plugin in English, or null when the instance does not have the plugin now. */
  pluginName: string | null
  /** False for a link of a plugin that does not run. The user can only unlink it. */
  pluginActive: boolean
  site: string
  auth: LinkedAccountAuth
  sync: { mode: LinkedAccountSyncMode; intervalMinutes: number | null } | null
  link: LinkedAccountLink | null
}

export interface LinkedAccountList {
  items: LinkedAccountSite[]
}

/** The body of a link. */
export interface LinkAccountBody {
  userName?: string
  secret?: string
}

/** Gets the path of the link of a site. */
export function linkPath(pluginId: string, site: string): string {
  return `/me/extensions/${encodeURIComponent(pluginId)}/accounts/${encodeURIComponent(site)}`
}

/** Gets a unique key for a site of a plugin. */
export function siteKey(item: Pick<LinkedAccountSite, 'pluginId' | 'site'>): string {
  return `${item.pluginId}/${item.site}`
}
