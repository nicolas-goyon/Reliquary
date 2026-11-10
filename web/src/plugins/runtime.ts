import { activePlugins, type ExtensionList } from '../api/extensions'
import { PluginHost, PluginLimitError, type PluginHostOptions } from './host'

/**
 * The entry point of the plugin runtime. The web app loads this module only when the user has an active plugin,
 * so the main bundle does not contain the runtime.
 */

export interface StartOptions extends Partial<Omit<PluginHostOptions, 'settings' | 'coreApi' | 'container'>> {
  document?: Document
}

/**
 * Creates the container of the plugin frames and starts the active plugins of the user.
 * @returns The host. Call `stopAll` to remove the frames.
 */
export function startPluginRuntime(list: ExtensionList, options: StartOptions = {}): PluginHost {
  const document = options.document ?? window.document
  const container = document.createElement('div')
  container.className = 'plugin-frames'
  container.hidden = true
  document.body.append(container)

  const host = new PluginHost({
    language: () => document.documentElement.lang || 'en',
    ...options,
    container,
    settings: list.plugins,
    coreApi: list.coreApi,
  })
  const stopAll = host.stopAll.bind(host)
  host.stopAll = async () => {
    await stopAll()
    container.remove()
  }

  for (const plugin of activePlugins(list)) {
    host.start({ id: plugin.id, version: plugin.version }).catch((error: unknown) => {
      if (error instanceof PluginLimitError) {
        console.warn(`The core did not start the plugin "${plugin.id}". ${error.message}`)
      } else {
        console.error(`The plugin "${plugin.id}" did not start.`, error)
      }
    })
  }
  return host
}
