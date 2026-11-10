import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { errorCodes } from './protocol'
import { RpcError } from './rpc'
import { startWatchdog } from './watchdog'

/** A fake peer. Each heartbeat waits for the test to answer it, or ends at its time limit. */
class FakePeer {
  isClosed = false
  pings = 0
  answer = true

  call(_method: 'plugin.ping', _params: null, options: { timeoutMs?: number } = {}): Promise<unknown> {
    this.pings += 1
    if (this.answer) {
      return Promise.resolve(null)
    }
    return new Promise((_resolve, reject) => {
      setTimeout(() => reject(new RpcError(errorCodes.timeout, 'Timeout')), options.timeoutMs)
    })
  }
}

describe('startWatchdog', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('sends a heartbeat each 2 seconds', async () => {
    const peer = new FakePeer()
    const unresponsive = vi.fn<() => void>()
    const stop = startWatchdog(peer, unresponsive)

    await vi.advanceTimersByTimeAsync(10_000)

    expect(peer.pings).toBe(5)
    expect(unresponsive).not.toHaveBeenCalled()
    stop()
  })

  it('calls onUnresponsive after 3 missed heartbeats, and then stops', async () => {
    const peer = new FakePeer()
    peer.answer = false
    const unresponsive = vi.fn<() => void>()
    startWatchdog(peer, unresponsive)

    // Heartbeats at 2, 4 and 6 s. Each one ends at its time limit, 2 s later.
    await vi.advanceTimersByTimeAsync(7_999)
    expect(unresponsive).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(unresponsive).toHaveBeenCalledTimes(1)

    const pings = peer.pings
    await vi.advanceTimersByTimeAsync(10_000)
    expect(peer.pings).toBe(pings)
    expect(unresponsive).toHaveBeenCalledTimes(1)
  })

  it('counts only the missed heartbeats in a row', async () => {
    const peer = new FakePeer()
    const unresponsive = vi.fn<() => void>()
    const stop = startWatchdog(peer, unresponsive)

    // The heartbeats at 2 and 4 s get no answer. The heartbeats at 6 and 8 s get an answer.
    peer.answer = false
    await vi.advanceTimersByTimeAsync(4_001)
    peer.answer = true
    await vi.advanceTimersByTimeAsync(4_000)
    // The heartbeats at 10 and 12 s get no answer: 2 missed heartbeats in a row at 14 s.
    peer.answer = false
    await vi.advanceTimersByTimeAsync(7_000)

    expect(unresponsive).not.toHaveBeenCalled()
    stop()
  })

  it('stops when the peer is closed', async () => {
    const peer = new FakePeer()
    const unresponsive = vi.fn<() => void>()
    startWatchdog(peer, unresponsive)

    peer.isClosed = true
    await vi.advanceTimersByTimeAsync(10_000)

    expect(peer.pings).toBe(0)
    expect(unresponsive).not.toHaveBeenCalled()
  })
})
