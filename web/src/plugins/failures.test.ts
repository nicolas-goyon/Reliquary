import { beforeEach, describe, expect, it } from 'vitest'
import { failureLimits, PluginFailures } from './failures'

describe('plugin failures', () => {
  beforeEach(() => sessionStorage.clear())

  it('reaches the limit after 3 crashes, and keeps the count for the next page of the session', () => {
    const first = new PluginFailures()

    expect(first.record('deck-sync', 'crash')).toBe(false)
    expect(first.record('deck-sync', 'crash')).toBe(false)
    // A new load of the page makes a new counter with the same session storage.
    expect(new PluginFailures().record('deck-sync', 'crash')).toBe(true)
    expect(failureLimits.crash).toBe(3)
  })

  it('counts the crashes and the slow answers of each plugin apart', () => {
    const failures = new PluginFailures()
    for (let index = 0; index < failureLimits.slow - 1; index += 1) {
      expect(failures.record('deck-sync', 'slow')).toBe(false)
    }

    expect(failures.record('deck-sync', 'crash')).toBe(false)
    expect(failures.record('other', 'slow')).toBe(false)
    expect(failures.record('deck-sync', 'slow')).toBe(true)
  })

  it('starts again after a reset, and works when the storage contains other data', () => {
    sessionStorage.setItem('reliquary:plugin-failures', 'not json')
    const failures = new PluginFailures()
    failures.record('deck-sync', 'crash')
    failures.record('deck-sync', 'crash')

    failures.reset('deck-sync')

    expect(failures.record('deck-sync', 'crash')).toBe(false)
  })
})
