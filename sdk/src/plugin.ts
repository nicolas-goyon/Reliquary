import type { ApiFetchParams, ApiFetchResult, HostMethods, LinkedAccount, PluginMethods, RelayFetchParams, RelayFetchResult } from './methods.ts'
import { connectMessageType, coreApiVersion, errorCodes, readyMessageType, type ConnectMessage } from './protocol.ts'
import { RpcError, RpcPeer, type PeerOptions } from './rpc.ts'
import type { ContributionData, SlotContexts, SlotContributions, SlotName } from './slots.ts'

/** The functions that the SDK gives to a plugin. */
export interface PluginSdk {
  /** The ID and the version of the plugin, as the core installed it. */
  readonly plugin: { id: string; version: string }
  /** The core API version of the core. */
  readonly coreApi: string
  /** The language of the interface, for example "en" or "fr". */
  readonly language: string
  /** True when the plugin comes from a URL in the development mode. */
  readonly development: boolean
  /** The public API of the instance. The core sends each request with a plugin token. */
  readonly api: {
    fetch(path: string, init?: Omit<ApiFetchParams, 'path'>): Promise<ApiFetchResult>
  }
  /** The network. The frame can reach only the domains of the plugin manifest that the user accepted. */
  readonly net: {
    /** Sends a request from the frame. The site must accept requests with the header `Origin: null`. */
    fetch(input: string, init?: RequestInit): Promise<Response>
    /** Sends a request through the network relay of the core, for a site that refuses requests from a browser. */
    relay(params: RelayFetchParams): Promise<RelayFetchResult>
  }
  /** The storage of the plugin for the user. The core keeps it on the server. */
  readonly storage: {
    get<T = unknown>(key: string): Promise<T | undefined>
    set(key: string, value: unknown): Promise<void>
    remove(key: string): Promise<void>
    keys(): Promise<string[]>
  }
  /** The linked accounts of the user for this plugin. */
  readonly accounts: {
    list(): Promise<LinkedAccount[]>
  }
  /** The interface elements of the plugin. */
  readonly ui: {
    /**
     * Adds a contribution to a slot. The plugin manifest must declare the slot.
     * @returns A function that removes the contribution.
     */
    contribute<Slot extends SlotName>(slot: Slot, contribution: SlotContributions[Slot]): Promise<() => Promise<void>>
    /** Shows a short message to the user. */
    notify(message: string, tone?: 'info' | 'success' | 'error'): Promise<void>
  }
}

/** The definition of a plugin. */
export interface PluginDefinition {
  /** Runs one time, when the core connects the plugin frame. */
  activate(sdk: PluginSdk): void | Promise<void>
  /** Runs before the core removes the plugin frame. */
  deactivate?(): void | Promise<void>
}

/** The window of the plugin frame, as the SDK uses it. Tests give a different environment. */
export interface PluginEnvironment {
  /** The window of the core. */
  readonly parent: { postMessage(message: unknown, targetOrigin: string): void }
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void
  removeEventListener(type: 'message', listener: (event: MessageEvent) => void): void
}

export interface PluginHandle {
  /** Resolves after the call of `activate`. Rejects when the plugin is not in a plugin frame, or when `activate` fails. */
  readonly ready: Promise<PluginSdk>
}

export interface DefinePluginOptions extends PeerOptions {
  /** The window of the frame. The default is the global window. */
  environment?: PluginEnvironment
}

function defaultEnvironment(): PluginEnvironment | undefined {
  if (typeof window === 'undefined' || window.parent === window) {
    return undefined
  }
  return window
}

/**
 * Defines a plugin and connects it to the core.
 * Call it one time, in the entry point of the plugin.
 *
 * @example
 * definePlugin({
 *   async activate(sdk) {
 *     await sdk.ui.contribute('deck.actions', {
 *       id: 'sync',
 *       label: { en: 'Sync', fr: 'Synchroniser' },
 *       onAction: ({ deckId }) => sdk.ui.notify(`Deck ${deckId}`),
 *     })
 *   },
 * })
 */
export function definePlugin(definition: PluginDefinition, options: DefinePluginOptions = {}): PluginHandle {
  const environment = options.environment ?? defaultEnvironment()
  if (!environment) {
    const ready = Promise.reject(new Error('The plugin is not in a plugin frame. The core loads the plugin in a plugin frame.'))
    // A plugin that does not wait for `ready` must not cause an unhandled rejection.
    ready.catch(() => undefined)
    return { ready }
  }
  const ready = new Promise<PluginSdk>((resolve, reject) => {
    const listener = (event: MessageEvent): void => {
      if (event.source !== environment.parent || !isConnectMessage(event.data) || !event.ports[0]) {
        return
      }
      // The core sends the port one time. The SDK ignores all other connect messages.
      environment.removeEventListener('message', listener)
      const message = event.data
      const peer = new RpcPeer<PluginMethods, HostMethods>(event.ports[0], options)
      const sdk = createSdk(peer, message)
      Promise.resolve()
        .then(() => definition.activate(sdk))
        .then(() => resolve(sdk), reject)
      peer.handle('plugin.deactivate', async () => {
        await definition.deactivate?.()
        return null
      })
    }
    environment.addEventListener('message', listener)
    environment.parent.postMessage({ type: readyMessageType, coreApi: coreApiVersion }, '*')
  })
  ready.catch(() => undefined)
  return { ready }
}

function isConnectMessage(data: unknown): data is ConnectMessage {
  if (typeof data !== 'object' || data === null) {
    return false
  }
  const message = data as Partial<ConnectMessage>
  return message.type === connectMessageType && typeof message.coreApi === 'string' && typeof message.plugin?.id === 'string'
}

function createSdk(peer: RpcPeer<PluginMethods, HostMethods>, message: ConnectMessage): PluginSdk {
  const actions = new Map<string, (context: SlotContexts[SlotName]) => void | Promise<void>>()
  const cells = new Map<string, (context: SlotContexts['table.columns']) => string | null | Promise<string | null>>()
  const key = (slot: SlotName, id: string): string => `${slot}\u0000${id}`

  peer.handle('plugin.ping', () => null)
  peer.handle('ui.action', async ({ slot, id, context }) => {
    const action = actions.get(key(slot, id))
    if (!action) {
      throw new RpcError(errorCodes.invalidParams, `The plugin has no action "${id}" in the slot "${slot}".`)
    }
    await action(context)
    return null
  })
  peer.handle('ui.cell', async ({ id, context }) => {
    const cell = cells.get(id)
    if (!cell) {
      throw new RpcError(errorCodes.invalidParams, `The plugin has no column "${id}".`)
    }
    return { text: await cell(context) }
  })

  return {
    plugin: { id: message.plugin.id, version: message.plugin.version },
    coreApi: message.coreApi,
    language: message.language,
    development: message.development,
    api: {
      fetch: (path, init = {}) => peer.call('api.fetch', { ...init, path }),
    },
    net: {
      fetch: (input, init) => globalThis.fetch(input, init),
      relay: (params) => peer.call('net.relay', params),
    },
    storage: {
      get: async <T>(storageKey: string) => {
        const { value } = await peer.call('storage.get', { key: storageKey })
        return (value ?? undefined) as T | undefined
      },
      set: async (storageKey, value) => {
        await peer.call('storage.set', { key: storageKey, value })
      },
      remove: async (storageKey) => {
        await peer.call('storage.remove', { key: storageKey })
      },
      keys: async () => (await peer.call('storage.keys', null)).keys,
    },
    accounts: {
      list: async () => (await peer.call('accounts.list', null)).accounts,
    },
    ui: {
      contribute: async (slot, contribution) => {
        const data: ContributionData = { id: contribution.id }
        if (contribution.order !== undefined) data.order = contribution.order
        if ('label' in contribution) data.label = contribution.label
        if ('icon' in contribution && contribution.icon) data.icon = contribution.icon
        if ('title' in contribution) data.title = contribution.title
        if ('panel' in contribution) data.panel = contribution.panel
        if ('onAction' in contribution && contribution.onAction) {
          actions.set(key(slot, contribution.id), contribution.onAction as (context: SlotContexts[SlotName]) => void | Promise<void>)
        }
        if ('cell' in contribution) {
          cells.set(contribution.id, contribution.cell)
        }
        await peer.call('ui.contribute', { slot, contribution: data })
        return async () => {
          actions.delete(key(slot, contribution.id))
          if (slot === 'table.columns') {
            cells.delete(contribution.id)
          }
          await peer.call('ui.withdraw', { slot, id: contribution.id })
        }
      },
      notify: async (text, tone = 'info') => {
        await peer.call('ui.notify', { message: text, tone })
      },
    },
  }
}
