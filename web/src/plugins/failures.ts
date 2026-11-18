/**
 * The counter of the failures of each plugin in the browser session. See issue #278.
 * The core deactivates a plugin that crashes or that answers too slowly too many times, so one plugin cannot spoil the interface.
 */

/** A crash: the frame did not connect, or it did not answer the heartbeats. A slow answer: a call of the core got no answer in the time limit. */
export type FailureKind = 'crash' | 'slow'

/** The number of failures of each kind in one browser session after which the core deactivates the plugin. */
export const failureLimits: Record<FailureKind, number> = { crash: 3, slow: 5 }

const storageKey = 'reliquary:plugin-failures'

type Counts = Record<string, Partial<Record<FailureKind, number>>>

function read(storage: Pick<Storage, 'getItem'>): Counts {
  try {
    const value: unknown = JSON.parse(storage.getItem(storageKey) ?? '{}')
    return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Counts) : {}
  } catch {
    return {}
  }
}

function write(storage: Pick<Storage, 'setItem'>, counts: Counts): void {
  try {
    storage.setItem(storageKey, JSON.stringify(counts))
  } catch {
    // The browser blocks the storage. The counter then starts again with each page.
  }
}

/** The failures of the plugins in the session storage, so a new load of the page keeps the counts. */
export class PluginFailures {
  private readonly storage: Pick<Storage, 'getItem' | 'setItem'>

  constructor(storage: Pick<Storage, 'getItem' | 'setItem'> = sessionStorage) {
    this.storage = storage
  }

  /**
   * Adds a failure of a plugin.
   * @returns True when the plugin now has the limit of failures of this kind: the core must deactivate it.
   */
  record(pluginId: string, kind: FailureKind): boolean {
    const counts = read(this.storage)
    const count = (counts[pluginId]?.[kind] ?? 0) + 1
    counts[pluginId] = { ...counts[pluginId], [kind]: count }
    write(this.storage, counts)
    return count >= failureLimits[kind]
  }

  /** Removes the counts of a plugin, for example after its deactivation. */
  reset(pluginId: string): void {
    const counts = read(this.storage)
    if (pluginId in counts) {
      delete counts[pluginId]
      write(this.storage, counts)
    }
  }
}
