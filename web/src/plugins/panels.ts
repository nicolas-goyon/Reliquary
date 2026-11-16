import type { PanelHandle, PanelState, PluginPanelSlot } from '../slots/registry'
import { connectFrame, type HostPeer } from './connection'
import { createPluginFrame, type PluginVersion } from './frame'
import { errorCodes } from './protocol'
import { RpcError, type PeerOptions } from './rpc'
import { startWatchdog, type HeartbeatOptions } from './watchdog'

/** The maximum height of a panel frame in each slot, in CSS pixels. */
export const panelMaxHeights: Record<PluginPanelSlot, number> = {
  'analysis.tabs': 800,
  'card.page': 600,
  'plugin.page': 2000,
  'plugin.dialog': 600,
}

/** The height of a panel frame before the first `ui.resize`, and its minimum height. */
export const panelMinHeight = 48
const initialHeight = 160

/**
 * Gives the context of a panel slot to a plugin. The plugin gets IDs, and it reads more data with the public API.
 * The contexts follow the type SlotContexts of the plugin SDK.
 */
export function panelContext(slot: PluginPanelSlot, context: unknown): Record<string, unknown> {
  const value = (context ?? {}) as Record<string, unknown>
  switch (slot) {
    case 'analysis.tabs':
      return { deckId: value.deckId }
    case 'card.page':
      return { oracleId: value.oracleId }
    case 'plugin.page':
      return { page: value.page }
    case 'plugin.dialog':
      return { data: value.data ?? null }
  }
}

export interface MountPanelOptions extends PeerOptions, HeartbeatOptions {
  element: HTMLElement
  plugin: PluginVersion
  /** The address of the frame: the same frame as the main frame of the plugin. */
  src: string
  development: boolean
  panel: { id: string; slot: PluginPanelSlot; title: string }
  /** The context of the slot, as the core gives it. The panel gets only the IDs of `panelContext`. */
  context: unknown
  coreApi: string
  language(): string
  hostWindow?: Pick<Window, 'addEventListener' | 'removeEventListener'>
  connectTimeoutMs?: number
  /** Adds the methods that the core answers to the peer of the panel, for example `ui.openDialog`. */
  registerMethods?(peer: HostPeer): void
  onState(state: PanelState): void
}

/**
 * Creates the frame of a panel in the element and connects it. The frame gets the context of the slot in the connect message.
 * @returns The handle of the panel. `close` removes the frame.
 */
export function mountPanel(options: MountPanelOptions): PanelHandle {
  const { element, panel, plugin } = options
  const maxHeight = panelMaxHeights[panel.slot]
  const frame = createPluginFrame(element.ownerDocument, plugin, options.src, panel.title)
  frame.className = 'plugin-panel-frame'
  frame.style.height = `${Math.min(initialHeight, maxHeight)}px`
  element.append(frame)

  const abort = new AbortController()
  let peer: HostPeer | undefined
  let stopWatchdog: (() => void) | undefined
  let closed = false
  let sent = JSON.stringify(panelContext(panel.slot, options.context))
  const connectedWith = sent

  const fail = (): void => {
    if (!closed) {
      close()
      options.onState('failed')
    }
  }
  const close = (): void => {
    closed = true
    abort.abort()
    stopWatchdog?.()
    peer?.close()
    frame.remove()
  }

  const frameWindow = frame.contentWindow
  if (!frameWindow) {
    fail()
    return { update: () => undefined, close }
  }
  options.onState('loading')
  connectFrame({
    ...options,
    hostWindow: options.hostWindow ?? element.ownerDocument.defaultView ?? window,
    frameWindow: frameWindow as unknown as Parameters<typeof connectFrame>[0]['frameWindow'],
    connect: {
      coreApi: options.coreApi,
      plugin: { id: plugin.id, version: plugin.version },
      language: options.language(),
      development: options.development,
      panel: { id: panel.id, slot: panel.slot, context: JSON.parse(sent) as unknown },
    },
    signal: abort.signal,
  }).then(
    (connected) => {
      if (closed) {
        connected.close()
        return
      }
      peer = connected
      peer.handle('ui.resize', (params) => {
        const height = (params as { height?: unknown } | null)?.height
        if (typeof height !== 'number' || !Number.isFinite(height)) {
          throw new RpcError(errorCodes.invalidParams, 'The height must be a number of CSS pixels.')
        }
        frame.style.height = `${Math.round(Math.min(Math.max(height, panelMinHeight), maxHeight))}px`
        return null
      })
      options.registerMethods?.(peer)
      if (sent !== connectedWith) {
        // The context changed while the frame connected.
        peer.call('panel.update', { context: JSON.parse(sent) as unknown }).catch(() => undefined)
      }
      stopWatchdog = startWatchdog(peer, () => {
        console.warn(`The panel "${panel.id}" of the plugin "${plugin.id}" did not answer the heartbeats. The core removed its frame.`)
        fail()
      }, options)
      options.onState('ready')
    },
    (error: unknown) => {
      if (!closed) {
        console.warn(`The panel "${panel.id}" of the plugin "${plugin.id}" did not connect.`, error)
        fail()
      }
    },
  )

  return {
    update(context) {
      const next = JSON.stringify(panelContext(panel.slot, context))
      if (next === sent) {
        return
      }
      sent = next
      peer?.call('panel.update', { context: JSON.parse(next) as unknown }).catch(() => undefined)
    },
    close,
  }
}
