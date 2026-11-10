/**
 * The methods of the RPC protocol. This file is the public contract between the core and the plugins.
 * A change to this file follows the core API version.
 */

import type { ContributionData, SlotContexts, SlotName } from './slots.ts'

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

/** A request that the network relay of the core sends to a domain of the plugin manifest. */
export interface RelayFetchParams {
  url: string
  method?: string
  headers?: Record<string, string>
  /** The body as text. */
  body?: string
}

export interface RelayFetchResult {
  status: number
  headers: Record<string, string>
  body: string
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

/** The methods that the core answers. The plugin calls them. */
export interface HostMethods {
  [method: string]: { params: unknown; result: unknown }
  'api.fetch': { params: ApiFetchParams; result: ApiFetchResult }
  'net.relay': { params: RelayFetchParams; result: RelayFetchResult }
  'storage.get': { params: { key: string }; result: { value: unknown } }
  'storage.set': { params: { key: string; value: unknown }; result: null }
  'storage.remove': { params: { key: string }; result: null }
  'storage.keys': { params: null; result: { keys: string[] } }
  'accounts.list': { params: null; result: { accounts: LinkedAccount[] } }
  'ui.contribute': { params: { slot: SlotName; contribution: ContributionData }; result: null }
  'ui.withdraw': { params: { slot: SlotName; id: string }; result: null }
  'ui.notify': { params: { message: string; tone: 'info' | 'success' | 'error' }; result: null }
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
  /** The core asks the value of a cell of a column of the table mode. */
  'ui.cell': { params: { id: string; context: SlotContexts['table.columns'] }; result: { text: string | null } }
}
