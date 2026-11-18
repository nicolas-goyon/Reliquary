/**
 * The link between the Linked accounts panel and the sync of the plugin runtime.
 * This module is in the main bundle. The runtime module sets the controller when it starts.
 */

/** Runs the syncs of the linked accounts. The plugin runtime gives it. */
export interface SyncController {
  /** Tells whether the frame of the plugin runs, so the core can call its sync. */
  isRunning(pluginId: string): boolean
  /** Runs a sync of a linked account now, and records its result. */
  sync(pluginId: string, site: string): Promise<void>
}

let controller: SyncController | null = null
let version = 0
const listeners = new Set<() => void>()

function notify(): void {
  version += 1
  for (const listener of listeners) {
    listener()
  }
}

/** Sets the controller of the plugin runtime, or removes it with null. */
export function setSyncController(next: SyncController | null): void {
  controller = next
  notify()
}

/** Gets the controller of the plugin runtime, or null when no plugin runs. */
export function getSyncController(): SyncController | null {
  return controller
}

/** Tells the panel that the linked accounts or the running plugins changed, for example after a sync. */
export function notifyLinkedAccountsChanged(): void {
  notify()
}

/** For `useSyncExternalStore`: a number that changes after each change. */
export function syncStateVersion(): number {
  return version
}

/** For `useSyncExternalStore`: calls the listener after each change. */
export function subscribeSyncState(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}
