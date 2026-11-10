import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PanelState } from '../slots/registry'
import type { HostMethods, PluginMethods } from './methods'
import { mountPanel, type MountPanelOptions } from './panels'
import { connectMessageType, readyMessageType } from './protocol'
import { RpcPeer, type PortLike } from './rpc'

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))
const deckContext = { deckId: 'deck-1', deck: { name: 'Private data of the page' }, analysis: {} }

/** Mounts a panel, then plays the plugin in the frame: it sends the ready message and gets the port. */
async function openPanel(overrides: Partial<MountPanelOptions> = {}, answerPings = true) {
  const element = document.createElement('div')
  document.body.append(element)
  const hostWindow = new EventTarget() as unknown as Window
  const states: PanelState[] = []
  const handle = mountPanel({
    element,
    plugin: { id: 'card-scan', version: '1.0.0' },
    src: '/api/v1/plugins/card-scan/1.0.0/frame',
    development: false,
    panel: { id: 'stats', slot: 'analysis.tabs', title: 'Scan statistics' },
    context: deckContext,
    coreApi: '1.1.0',
    language: () => 'en',
    hostWindow,
    onState: (state) => states.push(state),
    ...overrides,
  })
  const frame = element.querySelector('iframe')!
  const posted: { message: unknown; port: MessagePort }[] = []
  vi.spyOn(frame.contentWindow!, 'postMessage').mockImplementation(((message: unknown, _origin: string, transfer: Transferable[]) => {
    posted.push({ message, port: transfer[0] as MessagePort })
  }) as typeof window.postMessage)
  hostWindow.dispatchEvent(Object.assign(new Event('message'), { source: frame.contentWindow, data: { type: readyMessageType, coreApi: '1.0.0' } }))
  await vi.waitFor(() => expect(posted).toHaveLength(1))
  const plugin = new RpcPeer<PluginMethods, HostMethods>(posted[0].port as unknown as PortLike)
  const updates: unknown[] = []
  if (answerPings) {
    plugin.handle('plugin.ping', () => null)
  }
  plugin.handle('panel.update', ({ context }) => {
    updates.push(context)
    return null
  })
  await vi.waitFor(() => expect(states).toContain('ready'))
  return { element, frame, handle, plugin, posted, states, updates }
}

describe('mountPanel', () => {
  beforeEach(() => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  })

  afterEach(() => {
    vi.restoreAllMocks()
    document.body.replaceChildren()
  })

  it('creates a visible sandboxed frame and sends the panel with the IDs of the context only', async () => {
    const { frame, posted, handle, plugin } = await openPanel()

    expect(frame.getAttribute('sandbox')).toBe('allow-scripts')
    expect(frame.getAttribute('allow')).toBe('')
    expect(frame.hidden).toBe(false)
    expect(frame.title).toBe('Scan statistics')
    expect(posted[0].message).toEqual({
      type: connectMessageType,
      coreApi: '1.1.0',
      plugin: { id: 'card-scan', version: '1.0.0' },
      language: 'en',
      development: false,
      panel: { id: 'stats', slot: 'analysis.tabs', context: { deckId: 'deck-1' } },
    })
    handle.close()
    plugin.close()
  })

  it('applies the height of the panel between the minimum and the maximum of the slot', async () => {
    const { frame, handle, plugin } = await openPanel()

    await plugin.call('ui.resize', { height: 320 })
    expect(frame.style.height).toBe('320px')
    await plugin.call('ui.resize', { height: 50_000 })
    expect(frame.style.height).toBe('800px')
    await plugin.call('ui.resize', { height: 1 })
    expect(frame.style.height).toBe('48px')
    await expect(plugin.call('ui.resize', { height: 'big' })).rejects.toMatchObject({ code: -32602 })
    handle.close()
    plugin.close()
  })

  it('sends a new context only when the IDs change', async () => {
    const { handle, plugin, updates } = await openPanel()

    handle.update({ ...deckContext, deck: { name: 'Other object, same deck' } })
    handle.update({ deckId: 'deck-2', deck: {}, analysis: {} })
    await vi.waitFor(() => expect(updates).toHaveLength(1))

    expect(updates).toEqual([{ deckId: 'deck-2' }])
    handle.close()
    plugin.close()
  })

  it('removes the frame when the panel closes', async () => {
    const { element, handle, plugin } = await openPanel()

    handle.close()

    expect(element.querySelector('iframe')).toBeNull()
    plugin.close()
  })

  it('removes the frame and gives the state "failed" when the panel does not answer the heartbeats', async () => {
    const { element, states, plugin } = await openPanel({ intervalMs: 20, maxMissed: 2 }, false)

    await wait(150)

    expect(states.at(-1)).toBe('failed')
    expect(element.querySelector('iframe')).toBeNull()
    plugin.close()
  })

  it('gives the state "failed" when the frame sends no ready message', async () => {
    const element = document.createElement('div')
    document.body.append(element)
    const states: PanelState[] = []

    mountPanel({
      element,
      plugin: { id: 'card-scan', version: '1.0.0' },
      src: '/frame',
      development: false,
      panel: { id: 'prices', slot: 'card.page', title: 'Prices' },
      context: { oracleId: 'x' },
      coreApi: '1.1.0',
      language: () => 'en',
      hostWindow: new EventTarget() as unknown as Window,
      connectTimeoutMs: 20,
      onState: (state) => states.push(state),
    })
    await wait(60)

    expect(states).toEqual(['loading', 'failed'])
  })
})
