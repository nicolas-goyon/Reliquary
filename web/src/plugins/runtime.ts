import { activePlugins, type ExtensionList } from '../api/extensions'
import type { SlotRegistry } from '../slots/registry'
import { registerContributionMethods } from './contributions'
import { developmentFrameUrl, loadDevelopmentManifest } from './development'
import { frameAccess } from './consent'
import { frameUrl } from './frame'
import type { FailureKind } from './failures'
import { registerPluginFormats } from './formats'
import { registerHostApi } from './hostApi'
import { removeNotices } from './notices'
import { PluginTokens, type SessionRequest } from './pluginTokens'
import { registerPluginUi, type PanelPlugin } from './pluginUi'
import { PluginHost, PluginLimitError, type PluginHostOptions, type StartOptions } from './host'
import type { PluginVersion } from './frame'
import type { HostPeer } from './connection'
import { errorCodes } from './protocol'
import { RpcError } from './rpc'

/**
 * The entry point of the plugin runtime. The web app loads this module only when the user has an active plugin
 * or a development plugin, so the main bundle does not contain the runtime.
 */

export interface RuntimeOptions extends Partial<Omit<PluginHostOptions, 'settings' | 'coreApi' | 'container'>> {
  document?: Document
  /** The manifest URLs of the development plugins. The runtime uses them only when the instance allows development plugins. */
  developmentUrls?: string[]
  fetcher?: typeof fetch
  /** Sends a request with the web session of the user. The runtime gets the plugin tokens with it. Without it, the plugins cannot call the public API. */
  request?: SessionRequest
  /** The plugin tokens of the running plugins. The default is a new store that uses `request`. */
  tokens?: PluginTokens
  /**
   * Receives each failure of a plugin: a crash (the frame did not connect, or it did not answer the heartbeats)
   * or a slow answer (a call of the core got no answer in the time limit). See issue #278.
   */
  onFailure?(plugin: PluginVersion, kind: FailureKind, development: boolean): void
  /** The slot registry of the web app. The runtime adds the contributions of the plugins to it. */
  registry?: SlotRegistry
}

/** The calls that do not count as slow answers: a missed heartbeat is a crash, and the core removes the frame after `plugin.deactivate`. */
const notCounted = ['plugin.ping', 'plugin.deactivate']

/** Tells the runtime when a call of the core to the main frame of a plugin gets no answer in the time limit. */
function watchSlowAnswers(peer: HostPeer, onSlow: () => void): void {
  const call = peer.call.bind(peer)
  peer.call = ((method, params, callOptions) =>
    call(method, params, callOptions).catch((error: unknown) => {
      if (error instanceof RpcError && error.code === errorCodes.timeout && !notCounted.includes(method)) {
        onSlow()
      }
      throw error
    })) as typeof peer.call
}

/**
 * Creates the container of the plugin frames and starts the active plugins of the user, then the development plugins.
 * @returns The host. Call `stopAll` to remove the frames.
 */
export function startPluginRuntime(list: ExtensionList, options: RuntimeOptions = {}): PluginHost {
  const document = options.document ?? window.document
  const container = document.createElement('div')
  container.className = 'plugin-frames'
  container.hidden = true
  document.body.append(container)

  // The data of each plugin for its contributions, and the functions that remove its contributions.
  const plugins = new Map<string, PanelPlugin>()
  const language = options.language ?? (() => document.documentElement.lang || 'en')
  const cleanups = new Map<string, () => void>()
  const tokens = options.tokens ?? (options.request ? new PluginTokens({ request: options.request }) : undefined)

  const host = new PluginHost({
    ...options,
    language,
    container,
    settings: list.plugins,
    coreApi: list.coreApi,
    registerMethods: (peer, plugin) => {
      options.registerMethods?.(peer, plugin)
      const data = plugins.get(plugin.id)
      watchSlowAnswers(peer, () => options.onFailure?.(plugin, 'slow', data?.development ?? false))
      // Each frame of the plugin gets the methods of the public API and of the notices: the main frame and the panel frames.
      const registerFrameMethods = (target: typeof peer): void => {
        if (data && tokens) {
          registerHostApi(target, data, { tokens })
        }
      }
      registerFrameMethods(peer)
      if (options.registry && data) {
        const removeContributions = registerContributionMethods(peer, data, options.registry)
        const removeUi = registerPluginUi(peer, data, options.registry, {
          coreApi: list.coreApi,
          language,
          hostWindow: options.hostWindow,
          connectTimeoutMs: options.connectTimeoutMs,
          intervalMs: options.intervalMs,
          maxMissed: options.maxMissed,
          timeoutMs: options.timeoutMs,
          maxMessageBytes: options.maxMessageBytes,
          registerFrameMethods,
        })
        const removeFormats = registerPluginFormats(peer, data, options.registry)
        cleanups.set(plugin.id, () => {
          removeContributions()
          removeUi()
          removeFormats()
        })
      }
    },
    onEvent: (event) => {
      if (event.type === 'stopped') {
        if (event.reason !== 'stopped') {
          options.onFailure?.(event.plugin, 'crash', plugins.get(event.plugin.id)?.development ?? false)
        }
        cleanups.get(event.plugin.id)?.()
        cleanups.delete(event.plugin.id)
        tokens?.forget(event.plugin.id)
        removeNotices(event.plugin.id)
      }
      options.onEvent?.(event)
    },
  })
  const stopAll = host.stopAll.bind(host)
  host.stopAll = async () => {
    await stopAll()
    container.remove()
  }

  const start = (plugin: PluginVersion, startOptions?: StartOptions): void => {
    host.start(plugin, startOptions).catch((error: unknown) => {
      if (error instanceof PluginLimitError) {
        console.warn(`The core did not start the plugin "${plugin.id}". ${error.message}`)
      } else {
        console.error(`The plugin "${plugin.id}" did not start.`, error)
      }
    })
  }

  // The layout of the user: the positions of the plugins and the hidden contributions.
  options.registry?.setPluginLayouts(Object.fromEntries(activePlugins(list).flatMap((plugin) => (plugin.layout ? [[plugin.id, plugin.layout]] : []))))

  for (const plugin of activePlugins(list)) {
    // The panel frames use the same address, so all the frames of the plugin get only the accepted access.
    const src = frameUrl(plugin, list.plugins.frameOrigin, frameAccess(plugin))
    plugins.set(plugin.id, {
      id: plugin.id,
      name: plugin.name,
      version: plugin.version,
      slots: plugin.slots,
      permissions: plugin.permissions,
      development: false,
      src,
      panels: plugin.panels ?? [],
      i18n: plugin.i18n ?? null,
      formats: plugin.formats ?? [],
    })
    start({ id: plugin.id, version: plugin.version }, { src })
  }

  if (list.plugins.developmentPlugins) {
    for (const url of options.developmentUrls ?? []) {
      loadDevelopmentManifest(url, options.fetcher)
        .then((plugin) => {
          if (host.running.includes(plugin.id)) {
            console.warn(`The development plugin "${plugin.id}" has the ID of a running plugin. The core did not start it.`)
            return
          }
          const src = developmentFrameUrl(plugin, list.plugins.frameOrigin)
          plugins.set(plugin.id, { ...plugin, development: true, src })
          start({ id: plugin.id, version: plugin.version }, { src, development: true })
        })
        .catch((error: unknown) => console.warn(`The development plugin at ${url} did not start.`, error))
    }
  }
  return host
}
