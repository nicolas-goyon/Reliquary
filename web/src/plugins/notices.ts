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
  /** True for a message of the core about the plugin, for example after the core deactivated it. The message stays until the user closes it. */
  core?: boolean
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
  const message = notice.core ? notice.message : notice.message.slice(0, maxNoticeLength)
  publish([...notices.filter((item) => !dropped.has(item.key)), { ...notice, key, message }])
  if (!notice.core) {
    setTimeout(() => dismissNotice(key), noticeDurationMs)
  }
}

export function dismissNotice(key: number): void {
  if (notices.some((item) => item.key === key)) {
    publish(notices.filter((item) => item.key !== key))
  }
}

/** Removes the notices that a plugin sent, for example when its frame stops. The messages of the core about the plugin stay. */
export function removeNotices(pluginId: string, includeCore = false): void {
  const removed = (item: PluginNotice) => item.pluginId === pluginId && (includeCore || !item.core)
  if (notices.some(removed)) {
    publish(notices.filter((item) => !removed(item)))
  }
}
