/**
 * The methods of the RPC protocol that the host runtime uses.
 * The plugin SDK has the full list in sdk/src/methods.ts.
 * The issues of milestone M4 add the methods that the core answers: contributions, storage, API, network relay and linked accounts.
 */

/** The methods that the core answers. The plugin calls them. */
export interface HostMethods {
  [method: string]: { params: unknown; result: unknown }
}

/** The methods that the plugin answers. The core calls them. */
export interface PluginMethods {
  [method: string]: { params: unknown; result: unknown }
  /** The heartbeat of the core. */
  'plugin.ping': { params: null; result: null }
  /** The core removes the frame after this call. */
  'plugin.deactivate': { params: null; result: null }
}
