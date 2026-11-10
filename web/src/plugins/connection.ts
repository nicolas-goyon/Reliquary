import type { HostMethods, PluginMethods } from './methods'
import { connectMessageType, readyMessageType, type ConnectMessage } from './protocol'
import { RpcPeer, type PeerOptions, type PortLike } from './rpc'

/** The time that a plugin frame has to send the ready message, in milliseconds. */
export const defaultConnectTimeoutMs = 10_000

/** The window of a plugin frame, as the host runtime uses it. */
export interface FrameWindow {
  postMessage(message: unknown, targetOrigin: string, transfer: Transferable[]): void
}

export interface ConnectOptions extends PeerOptions {
  /** The window of the core. The runtime gets the ready message of the frame from it. */
  hostWindow: Pick<Window, 'addEventListener' | 'removeEventListener'>
  /** The window of the plugin frame. */
  frameWindow: FrameWindow
  /** The data that the core sends with the port. */
  connect: Omit<ConnectMessage, 'type'>
  /** The time that the frame has to send the ready message. The default is 10 seconds. */
  connectTimeoutMs?: number
  /** Stops the wait for the ready message. */
  signal?: AbortSignal
}

export type HostPeer = RpcPeer<HostMethods, PluginMethods>

/**
 * Waits for the ready message of a plugin frame, then sends the port of a new MessageChannel one time.
 * The runtime accepts the ready message only when its source is the window of the frame.
 * @returns The RPC peer of the core for this frame.
 */
export function connectFrame(options: ConnectOptions): Promise<HostPeer> {
  const { hostWindow, frameWindow, connectTimeoutMs = defaultConnectTimeoutMs, signal } = options
  return new Promise((resolve, reject) => {
    let timer: ReturnType<typeof setTimeout> | undefined
    const finish = (): void => {
      clearTimeout(timer)
      hostWindow.removeEventListener('message', listener)
      signal?.removeEventListener('abort', abort)
    }
    const listener = (event: Event): void => {
      const message = event as MessageEvent
      if (message.source !== (frameWindow as unknown) || !isReadyMessage(message.data)) {
        return
      }
      // The core sends the port one time. The listener stops here, so a second ready message has no effect.
      finish()
      const channel = new MessageChannel()
      const connect: ConnectMessage = { type: connectMessageType, ...options.connect }
      frameWindow.postMessage(connect, '*', [channel.port2])
      resolve(new RpcPeer<HostMethods, PluginMethods>(channel.port1 as unknown as PortLike, options))
    }
    const abort = (): void => {
      finish()
      reject(new DOMException('The connection to the plugin frame stopped.', 'AbortError'))
    }
    if (signal?.aborted) {
      reject(new DOMException('The connection to the plugin frame stopped.', 'AbortError'))
      return
    }
    hostWindow.addEventListener('message', listener)
    signal?.addEventListener('abort', abort)
    timer = setTimeout(() => {
      finish()
      reject(new Error(`The plugin frame did not send the ready message in ${connectTimeoutMs} ms.`))
    }, connectTimeoutMs)
  })
}

function isReadyMessage(data: unknown): boolean {
  return typeof data === 'object' && data !== null && (data as { type?: unknown }).type === readyMessageType
}
