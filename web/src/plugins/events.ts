import { activePlugins, type ExtensionList } from '../api/extensions'

/** The event that the window receives when the plugins of the user change. The plugin runtime starts again. */
export const pluginsChangedEvent = 'reliquary:plugins-changed'

/**
 * Tells the plugin runtime that the plugins of the user changed, for example after `PUT /me/extensions/{id}`.
 * The runtime gets the extensions again, stops all the plugin frames, and starts the active plugins.
 */
export function notifyPluginsChanged(): void {
  window.dispatchEvent(new Event(pluginsChangedEvent))
}

/** The time between two checks of the active plugins: 10 minutes. */
export const pluginCheckIntervalMs = 10 * 60 * 1000

/** Gives a text that changes when the active plugins or their versions change. */
export function runningPluginsKey(list: ExtensionList): string {
  return activePlugins(list)
    .map((plugin) => `${plugin.id}@${plugin.version}`)
    .sort()
    .join(',')
}
