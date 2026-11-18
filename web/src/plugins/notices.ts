/**
 * The notices that the plugins show with `ui.notify`. The component `PluginNotices` renders them with the components of the core.
 */

export type NoticeTone = 'info' | 'success' | 'error'

export interface PluginNotice {
  key: number
  pluginId: string
  pluginName: string
  development: boolean
  message: string
  tone: NoticeTone
}

/** The maximum length of the message of a notice. The core cuts a longer message. */
export const maxNoticeLength = 200

/** The maximum number of visible notices for each plugin. The core removes the oldest notice of the plugin. */
export const maxNoticesPerPlugin = 3

/** The time that a notice stays visible, in milliseconds. */
export const noticeDurationMs = 8000

let notices: PluginNotice[] = []
let nextKey = 1
const listeners = new Set<() => void>()

function publish(next: PluginNotice[]): void {
  notices = next
  for (const listener of listeners) {
    listener()
  }
}

export function subscribeNotices(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function currentNotices(): PluginNotice[] {
  return notices
}

/** Adds a notice of a plugin. The notice goes away after `noticeDurationMs`. */
export function addNotice(notice: Omit<PluginNotice, 'key'>): void {
  const key = nextKey++
  const ofPlugin = notices.filter((item) => item.pluginId === notice.pluginId)
  const dropped = new Set(ofPlugin.slice(0, Math.max(0, ofPlugin.length - maxNoticesPerPlugin + 1)).map((item) => item.key))
  publish([...notices.filter((item) => !dropped.has(item.key)), { ...notice, key, message: notice.message.slice(0, maxNoticeLength) }])
  setTimeout(() => dismissNotice(key), noticeDurationMs)
}

export function dismissNotice(key: number): void {
  if (notices.some((item) => item.key === key)) {
    publish(notices.filter((item) => item.key !== key))
  }
}

/** Removes the notices of a plugin, for example when its frame stops. */
export function removeNotices(pluginId: string): void {
  if (notices.some((item) => item.pluginId === pluginId)) {
    publish(notices.filter((item) => item.pluginId !== pluginId))
  }
}
