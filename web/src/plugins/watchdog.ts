import { heartbeatIntervalMs, maxMissedHeartbeats } from './protocol'
import type { CallOptions } from './rpc'

export interface HeartbeatOptions {
  /** The interval between two heartbeats, in milliseconds. The default is 2000. */
  intervalMs?: number
  /** The number of missed heartbeats that stops the plugin. The default is 3. */
  maxMissed?: number
}

interface PingTarget {
  readonly isClosed: boolean
  call(method: 'plugin.ping', params: null, options?: CallOptions): Promise<unknown>
}

/**
 * Sends a heartbeat to a plugin frame at each interval.
 * Each heartbeat has the interval as its time limit, so a heartbeat without an answer is missed when the next heartbeat starts.
 * After `maxMissed` missed heartbeats in a row, the watchdog stops and calls `onUnresponsive`.
 * @returns A function that stops the watchdog.
 */
export function startWatchdog(peer: PingTarget, onUnresponsive: () => void, options: HeartbeatOptions = {}): () => void {
  const intervalMs = options.intervalMs ?? heartbeatIntervalMs
  const maxMissed = options.maxMissed ?? maxMissedHeartbeats
  let missed = 0
  let stopped = false
  const stop = (): void => {
    stopped = true
    clearInterval(timer)
  }
  const timer = setInterval(() => {
    if (stopped || peer.isClosed) {
      stop()
      return
    }
    peer.call('plugin.ping', null, { timeoutMs: intervalMs }).then(
      () => {
        missed = 0
      },
      () => {
        if (stopped || peer.isClosed) {
          return
        }
        missed += 1
        if (missed >= maxMissed) {
          stop()
          onUnresponsive()
        }
      },
    )
  }, intervalMs)
  return stop
}
