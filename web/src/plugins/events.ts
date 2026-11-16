/** The event that the window receives when the plugins of the user change. The plugin runtime starts again. */
export const pluginsChangedEvent = 'reliquary:plugins-changed'

/**
 * Tells the plugin runtime that the plugins of the user changed, for example after `PUT /me/extensions/{id}`.
 * The runtime gets the extensions again, stops all the plugin frames, and starts the active plugins.
 */
export function notifyPluginsChanged(): void {
  window.dispatchEvent(new Event(pluginsChangedEvent))
}
