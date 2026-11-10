/**
 * The methods of the RPC protocol that the host runtime uses.
 * The plugin SDK has the full list in sdk/src/methods.ts.
 * The issues of milestone M4 add the methods that the core answers: contributions, storage, API, network relay and linked accounts.
 */

/** The methods that the core answers. The plugin calls them. */
export interface HostMethods {
  [method: string]: { params: unknown; result: unknown }
  /** Adds a contribution of the plugin to a slot. The core checks the data before it accepts it. */
  'ui.contribute': { params: unknown; result: null }
  /** Removes a contribution of the plugin. */
  'ui.withdraw': { params: unknown; result: null }
  /** Only from a panel frame: the height of the content. */
  'ui.resize': { params: unknown; result: null }
  'ui.openDialog': { params: unknown; result: null }
  'ui.closeDialog': { params: unknown; result: null }
  'ui.openPage': { params: unknown; result: null }
}

/** The methods that the plugin answers. The core calls them. */
export interface PluginMethods {
  [method: string]: { params: unknown; result: unknown }
  /** The heartbeat of the core. */
  'plugin.ping': { params: null; result: null }
  /** The core removes the frame after this call. */
  'plugin.deactivate': { params: null; result: null }
  /** Only in a panel frame: the context of the slot changed. */
  'panel.update': { params: { context: unknown }; result: null }
  /** The core asks the cells of many rows of a column of the table mode. */
  'ui.cells': { params: { id: string; rows: Record<string, unknown>[] }; result: unknown }
  /** The user selected a button or a menu item of the plugin. */
  'ui.action': { params: { slot: string; id: string; context: unknown }; result: null }
}
