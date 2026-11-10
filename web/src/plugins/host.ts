import type { PluginRuntimeSettings } from '../api/extensions'
import { connectFrame, type FrameWindow, type HostPeer } from './connection'
import { createPluginFrame, frameUrl, type PluginVersion } from './frame'
import type { PeerOptions } from './rpc'
import { startWatchdog, type HeartbeatOptions } from './watchdog'

/** The reason why a plugin stops. */
export type StopReason = 'stopped' | 'unresponsive' | 'connect-failed'

/** An event of the plugin host, for the interface and for the automatic deactivation of plugins (issue #278). */
export type PluginHostEvent =
  | { type: 'started'; plugin: PluginVersion }
  | { type: 'stopped'; plugin: PluginVersion; reason: StopReason }

/** The user has the maximum number of active plugins. */
export class PluginLimitError extends Error {
  readonly limit: number

  constructor(limit: number) {
    super(`The user can have ${limit} active plugins at most.`)
    this.name = 'PluginLimitError'
    this.limit = limit
  }
}

export interface PluginHostOptions extends PeerOptions, HeartbeatOptions {
  /** The element that contains the plugin frames. */
  container: HTMLElement
  /** The settings of the instance: the limit of active plugins and the origin of the frames. */
  settings: PluginRuntimeSettings
  /** The current version of the core API. */
  coreApi: string
  /** Gives the language of the interface. */
  language(): string
  /** The window of the core. The default is the window of the container. */
  hostWindow?: Pick<Window, 'addEventListener' | 'removeEventListener'>
  /** The time that a frame has to send the ready message, in milliseconds. */
  connectTimeoutMs?: number
  /** Adds the methods that the core answers to a new peer. The issues of milestone M4 add these methods. */
  registerMethods?(peer: HostPeer, plugin: PluginVersion): void
  /** Receives the events of the host. */
  onEvent?(event: PluginHostEvent): void
}

interface RunningPlugin {
  plugin: PluginVersion
  frame: HTMLIFrameElement
  abort: AbortController
  peer?: HostPeer
  stopWatchdog?: () => void
}

/** The time that a plugin has to answer `plugin.deactivate`, in milliseconds. */
const deactivateTimeoutMs = 1000

/**
 * Creates and removes the plugin frames, connects them and watches them. See docs/decisions/0049-plugin-sandbox.md.
 */
export class PluginHost {
  private readonly options: PluginHostOptions
  private readonly plugins = new Map<string, RunningPlugin>()

  constructor(options: PluginHostOptions) {
    this.options = options
  }

  /** The IDs of the plugins that have a frame. */
  get running(): string[] {
    return [...this.plugins.keys()]
  }

  /**
   * Creates the frame of a plugin and connects it. A plugin that has a frame does not get a second frame.
   * @throws PluginLimitError when the user has the maximum number of active plugins.
   */
  async start(plugin: PluginVersion): Promise<void> {
    if (this.plugins.has(plugin.id)) {
      return
    }
    if (this.plugins.size >= this.options.settings.maxActive) {
      throw new PluginLimitError(this.options.settings.maxActive)
    }
    const { container } = this.options
    const frame = createPluginFrame(container.ownerDocument, plugin, frameUrl(plugin, this.options.settings.frameOrigin))
    const running: RunningPlugin = { plugin, frame, abort: new AbortController() }
    this.plugins.set(plugin.id, running)
    container.append(frame)

    const frameWindow = frame.contentWindow as unknown as FrameWindow | null
    if (!frameWindow) {
      this.remove(running, 'connect-failed')
      return
    }
    let peer: HostPeer
    try {
      peer = await connectFrame({
        ...this.options,
        hostWindow: this.options.hostWindow ?? container.ownerDocument.defaultView ?? window,
        frameWindow,
        connect: { coreApi: this.options.coreApi, plugin: { id: plugin.id, version: plugin.version }, language: this.options.language(), development: false },
        signal: running.abort.signal,
      })
    } catch (error) {
      if (this.plugins.get(plugin.id) === running) {
        console.warn(`The plugin "${plugin.id}" did not connect. The core removed its frame.`, error)
        this.remove(running, 'connect-failed')
      }
      return
    }
    if (this.plugins.get(plugin.id) !== running) {
      peer.close()
      return
    }
    running.peer = peer
    this.options.registerMethods?.(peer, plugin)
    running.stopWatchdog = startWatchdog(
      peer,
      () => {
        console.warn(`The plugin "${plugin.id}" did not answer ${this.options.maxMissed ?? 3} heartbeats. The core removed its frame.`)
        this.remove(running, 'unresponsive')
      },
      this.options,
    )
    this.options.onEvent?.({ type: 'started', plugin })
  }

  /** Asks the plugin to stop, then removes its frame. */
  async stop(id: string): Promise<void> {
    const running = this.plugins.get(id)
    if (!running) {
      return
    }
    running.stopWatchdog?.()
    if (running.peer && !running.peer.isClosed) {
      await running.peer.call('plugin.deactivate', null, { timeoutMs: deactivateTimeoutMs }).catch(() => undefined)
    }
    if (this.plugins.get(id) === running) {
      this.remove(running, 'stopped')
    }
  }

  /** Stops all the plugins. */
  async stopAll(): Promise<void> {
    await Promise.all(this.running.map((id) => this.stop(id)))
  }

  private remove(running: RunningPlugin, reason: StopReason): void {
    this.plugins.delete(running.plugin.id)
    running.abort.abort()
    running.stopWatchdog?.()
    running.peer?.close()
    running.frame.remove()
    this.options.onEvent?.({ type: 'stopped', plugin: running.plugin, reason })
  }
}
