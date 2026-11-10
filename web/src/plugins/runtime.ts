import { activePlugins, type ExtensionList } from '../api/extensions'
import type { SlotRegistry } from '../slots/registry'
import { registerContributionMethods, type ContributingPlugin } from './contributions'
import { developmentFrameUrl, loadDevelopmentManifest } from './development'
import { PluginHost, PluginLimitError, type PluginHostOptions, type StartOptions } from './host'
import type { PluginVersion } from './frame'

/**
 * The entry point of the plugin runtime. The web app loads this module only when the user has an active plugin
 * or a development plugin, so the main bundle does not contain the runtime.
 */

export interface RuntimeOptions extends Partial<Omit<PluginHostOptions, 'settings' | 'coreApi' | 'container'>> {
  document?: Document
  /** The manifest URLs of the development plugins. The runtime uses them only when the instance allows development plugins. */
  developmentUrls?: string[]
  fetcher?: typeof fetch
  /** The slot registry of the web app. The runtime adds the contributions of the plugins to it. */
  registry?: SlotRegistry
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
  const plugins = new Map<string, ContributingPlugin>()
  const cleanups = new Map<string, () => void>()

  const host = new PluginHost({
    language: () => document.documentElement.lang || 'en',
    ...options,
    container,
    settings: list.plugins,
    coreApi: list.coreApi,
    registerMethods: (peer, plugin) => {
      options.registerMethods?.(peer, plugin)
      const data = plugins.get(plugin.id)
      if (options.registry && data) {
        cleanups.set(plugin.id, registerContributionMethods(peer, data, options.registry))
      }
    },
    onEvent: (event) => {
      if (event.type === 'stopped') {
        cleanups.get(event.plugin.id)?.()
        cleanups.delete(event.plugin.id)
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

  for (const plugin of activePlugins(list)) {
    plugins.set(plugin.id, { id: plugin.id, name: plugin.name, slots: plugin.slots, development: false })
    start({ id: plugin.id, version: plugin.version })
  }

  if (list.plugins.developmentPlugins) {
    for (const url of options.developmentUrls ?? []) {
      loadDevelopmentManifest(url, options.fetcher)
        .then((plugin) => {
          if (host.running.includes(plugin.id)) {
            console.warn(`The development plugin "${plugin.id}" has the ID of a running plugin. The core did not start it.`)
            return
          }
          plugins.set(plugin.id, { id: plugin.id, name: plugin.name, slots: plugin.slots, development: true })
          start({ id: plugin.id, version: plugin.version }, { src: developmentFrameUrl(plugin, list.plugins.frameOrigin), development: true })
        })
        .catch((error: unknown) => console.warn(`The development plugin at ${url} did not start.`, error))
    }
  }
  return host
}
