/**
 * The methods of the RPC protocol. This file is the public contract between the core and the plugins.
 * A change to this file follows the core API version.
 */

import type { CellValue, ContributionData, SlotContexts, SlotName } from './slots.ts'

/** A request to the public API. The core sends it with a plugin token. */
export interface ApiFetchParams {
  /** The path after `/api/v1`, for example "/decks". */
  path: string
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  /** The JSON body of the request. */
  body?: unknown
}

/** The response of the public API. */
export interface ApiFetchResult {
  status: number
  /** The JSON body of the response, or null for a response with no content. */
  body: unknown
}

/** A file that an export format gives. */
export interface ExportedFile {
  /** The name of the file, with its extension. 100 characters at most, with no "/" or "\\". */
  fileName: string
  /** For example "text/plain", "text/csv", "application/json" or "application/xml". The default is "text/plain". */
  contentType?: string
  content: string
}

/** A linked account of the user for this plugin. */
export interface LinkedAccount {
  site: string
  /** The public user name, when the authentication method gives one. */
  userName: string | null
  connected: boolean
  /** The time of the last synchronization, in the ISO 8601 format. */
  lastSyncAt: string | null
}

/** The linked account of a site with its secret. Only the plugin of the link gets it. */
export interface LinkedAccountSecret {
  site: string
  auth: 'public-username' | 'api-key' | 'oauth2-pkce'
  /** The name of the account on the site, or null. */
  userName: string | null
  /** The API key, or null for a public user name. */
  secret: string | null
}

/** The methods that the core answers. The plugin calls them. */
export interface HostMethods {
  [method: string]: { params: unknown; result: unknown }
  'api.fetch': { params: ApiFetchParams; result: ApiFetchResult }
  'storage.get': { params: { key: string }; result: { value: unknown } }
  'storage.set': { params: { key: string; value: unknown }; result: null }
  'storage.remove': { params: { key: string }; result: null }
  'storage.keys': { params: null; result: { keys: string[] } }
  'accounts.list': { params: null; result: { accounts: LinkedAccount[] } }
  /** Gives the linked account of a site with its secret. The core gets it from the vault with a plugin token of this plugin. */
  'accounts.secret': { params: { site: string }; result: LinkedAccountSecret }
  'ui.contribute': { params: { slot: SlotName; contribution: ContributionData }; result: null }
  'ui.withdraw': { params: { slot: SlotName; id: string }; result: null }
  'ui.notify': { params: { message: string; tone: 'info' | 'success' | 'error' }; result: null }
  /** Only in a panel frame: the height of the content, in CSS pixels. The core applies the maximum height of the slot. */
  'ui.resize': { params: { height: number }; result: null }
  /** Opens a panel of the slot "plugin.dialog" in a dialog of the core. The panel gets the data in its context. */
  'ui.openDialog': { params: { panel: string; data?: unknown }; result: null }
  /** Closes the dialog of the plugin. */
  'ui.closeDialog': { params: null; result: null }
  /** Opens a panel of the slot "plugin.page" at the address /plugins/<plugin ID>/<panel ID>. */
  'ui.openPage': { params: { page: string }; result: null }
}

/** The methods that the plugin answers. The core calls them. */
export interface PluginMethods {
  [method: string]: { params: unknown; result: unknown }
  /** The heartbeat of the core. The SDK answers it. */
  'plugin.ping': { params: null; result: null }
  /** The core removes the frame after this call. */
  'plugin.deactivate': { params: null; result: null }
  /** The user selected a button or a menu item of the plugin. */
  'ui.action': { params: { slot: SlotName; id: string; context: SlotContexts[SlotName] }; result: null }
  /** Only in a panel frame: the context of the slot changed, for example after a change of the deck. */
  'panel.update': { params: { context: unknown }; result: null }
  /**
   * The user imports a file with an import format of the plugin. The plugin gives the text that the core imports:
   * a deck list in the text format of Reliquary for a deck, or a CSV file of Reliquary for a collection.
   */
  'format.import': { params: { id: string; content: string; fileName: string | null }; result: { content: string } }
  /** The user exports with an export format of the plugin. The core gives the CSV file of Reliquary, and the plugin gives its file. */
  'format.export': { params: { id: string; content: string }; result: ExportedFile }
  /**
   * The core starts a sync of a linked account: on a click of the user, when the web app opens, or periodically.
   * The plugin throws an error when the sync fails. The core shows the message of the error to the user.
   */
  'account.sync': { params: { account: LinkedAccount }; result: null }
  /** The core asks the cells of many rows of a column of the table mode. The answer has one cell for each row, in the same order. */
  'ui.cells': { params: { id: string; rows: SlotContexts['table.columns'][] }; result: { cells: (CellValue | null)[] } }
}
