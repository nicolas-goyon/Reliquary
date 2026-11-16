import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import type { HostMethods, PluginMethods } from './methods.ts'
import { definePlugin, type PluginEnvironment, type PluginSdk } from './plugin.ts'
import { connectMessageType, coreApiVersion, errorCodes, readyMessageType, type ConnectMessage } from './protocol.ts'
import { RpcError, RpcPeer, type PortLike } from './rpc.ts'

/** A fake window of a plugin frame. The test plays the core. */
class FakeFrame implements PluginEnvironment {
  readonly sent: unknown[] = []
  readonly parent = { postMessage: (message: unknown) => this.sent.push(message) }
  readonly document = { body: { id: 'body' } as unknown as HTMLElement, documentElement: { scrollHeight: 120 } as unknown as HTMLElement }
  private readonly listeners = new Set<(event: MessageEvent) => void>()

  addEventListener(_type: 'message', listener: (event: MessageEvent) => void): void {
    this.listeners.add(listener)
  }

  removeEventListener(_type: 'message', listener: (event: MessageEvent) => void): void {
    this.listeners.delete(listener)
  }

  dispatch(source: unknown, data: unknown, ports: MessagePort[]): void {
    for (const listener of [...this.listeners]) {
      listener({ source, data, ports } as unknown as MessageEvent)
    }
  }

  /** Sends the connect message from the parent window, as the core does. */
  connect(overrides: Partial<ConnectMessage> = {}): RpcPeer<HostMethods, PluginMethods> {
    const channel = new MessageChannel()
    const message: ConnectMessage = {
      type: connectMessageType,
      coreApi: coreApiVersion,
      plugin: { id: 'moxfield-sync', version: '0.1.0' },
      language: 'fr',
      development: false,
      ...overrides,
    }
    this.dispatch(this.parent, message, [channel.port2])
    return new RpcPeer<HostMethods, PluginMethods>(channel.port1 as unknown as PortLike)
  }
}

describe('definePlugin', () => {
  it('sends the ready message to the parent window', () => {
    const frame = new FakeFrame()

    definePlugin({ activate: () => undefined }, { environment: frame })

    assert.deepEqual(frame.sent, [{ type: readyMessageType, coreApi: coreApiVersion }])
  })

  it('activates the plugin with the data of the connect message', async () => {
    const frame = new FakeFrame()
    let received: PluginSdk | undefined
    const handle = definePlugin({ activate: (sdk) => void (received = sdk) }, { environment: frame })

    const host = frame.connect()
    const sdk = await handle.ready

    assert.equal(sdk, received)
    assert.deepEqual(sdk.plugin, { id: 'moxfield-sync', version: '0.1.0' })
    assert.equal(sdk.language, 'fr')
    host.close()
  })

  it('ignores a connect message from a window that is not the parent window', async () => {
    const frame = new FakeFrame()
    let activations = 0
    const handle = definePlugin({ activate: () => void activations++ }, { environment: frame })

    const channel = new MessageChannel()
    frame.dispatch({}, { type: connectMessageType, coreApi: coreApiVersion, plugin: { id: 'x', version: '1.0.0' } }, [channel.port2])
    await new Promise((resolve) => setTimeout(resolve, 10))
    assert.equal(activations, 0)

    const host = frame.connect()
    await handle.ready
    assert.equal(activations, 1)
    host.close()
    channel.port1.close()
  })

  it('uses only the first port', async () => {
    const frame = new FakeFrame()
    let activations = 0
    const handle = definePlugin({ activate: () => void activations++ }, { environment: frame })

    const first = frame.connect()
    const second = frame.connect()
    await handle.ready

    assert.equal(activations, 1)
    assert.equal(await first.call('plugin.ping', null), null)
    await assert.rejects(second.call('plugin.ping', null, { timeoutMs: 30 }), (error: RpcError) => error.code === errorCodes.timeout)
    first.close()
    second.close()
  })

  it('sends the contributions as data and runs the action of a button', async () => {
    const frame = new FakeFrame()
    const actions: string[] = []
    const handle = definePlugin(
      {
        async activate(sdk) {
          await sdk.ui.contribute('deck.actions', {
            id: 'sync',
            label: { en: 'Sync', fr: 'Synchroniser' },
            icon: 'sync',
            order: 5,
            onAction: ({ deckId }) => void actions.push(deckId),
          })
        },
      },
      { environment: frame },
    )
    const host = frame.connect()
    const contributions: unknown[] = []
    host.handle('ui.contribute', (params) => {
      contributions.push(params)
      return null
    })

    await handle.ready
    await host.call('ui.action', { slot: 'deck.actions', id: 'sync', context: { deckId: 'deck-1' } })

    assert.deepEqual(contributions, [{ slot: 'deck.actions', contribution: { id: 'sync', order: 5, label: { en: 'Sync', fr: 'Synchroniser' }, icon: 'sync' } }])
    assert.deepEqual(actions, ['deck-1'])
    await assert.rejects(host.call('ui.action', { slot: 'deck.actions', id: 'other', context: { deckId: 'deck-1' } }), (error: RpcError) => error.code === errorCodes.invalidParams)
    host.close()
  })

  it('calls the public API and the storage through the core', async () => {
    const frame = new FakeFrame()
    const handle = definePlugin({ activate: () => undefined }, { environment: frame })
    const host = frame.connect()
    const store = new Map<string, unknown>()
    host.handle('api.fetch', ({ path, method }) => ({ status: 200, body: { path, method: method ?? 'GET' } }))
    host.handle('storage.set', ({ key, value }) => {
      store.set(key, value)
      return null
    })
    host.handle('storage.get', ({ key }) => ({ value: store.get(key) ?? null }))

    const sdk = await handle.ready

    assert.deepEqual(await sdk.api.fetch('/decks'), { status: 200, body: { path: '/decks', method: 'GET' } })
    await sdk.storage.set('last', { deckId: 'deck-1' })
    assert.deepEqual(await sdk.storage.get('last'), { deckId: 'deck-1' })
    assert.equal(await sdk.storage.get('missing'), undefined)
    host.close()
  })

  it('rejects "ready" when the plugin is not in a frame', async () => {
    const handle = definePlugin({ activate: () => undefined })

    await assert.rejects(handle.ready, /not in a plugin frame/)
  })

  it('mounts a panel in a panel frame, with no call of activate', async () => {
    const frame = new FakeFrame()
    const mounted: unknown[] = []
    const updates: unknown[] = []
    let activations = 0
    const handle = definePlugin(
      {
        activate: () => void activations++,
        panels: {
          stats: {
            mount: (element, context) => void mounted.push([element, context]),
            update: (context) => void updates.push(context),
          },
        },
      },
      { environment: frame },
    )

    const host = frame.connect({ panel: { id: 'stats', slot: 'analysis.tabs', context: { deckId: 'deck-1' } } })
    await handle.ready
    await host.call('panel.update', { context: { deckId: 'deck-2' } })

    assert.equal(activations, 0)
    assert.deepEqual(mounted, [[frame.document.body, { deckId: 'deck-1' }]])
    assert.deepEqual(updates, [{ deckId: 'deck-2' }])
    host.close()
  })

  it('sends the size, the dialogs and the pages to the core', async () => {
    const frame = new FakeFrame()
    const handle = definePlugin({ activate: () => undefined }, { environment: frame })
    const host = frame.connect()
    const calls: unknown[] = []
    for (const method of ['ui.resize', 'ui.openDialog', 'ui.closeDialog', 'ui.openPage'] as const) {
      host.handle(method, (params: unknown) => {
        calls.push([method, params])
        return null
      })
    }
    const sdk = await handle.ready

    await sdk.ui.resize(300)
    await sdk.ui.openDialog('confirm', { deckId: 'deck-1' })
    await sdk.ui.closeDialog()
    await sdk.ui.openPage('settings')

    assert.deepEqual(calls, [
      ['ui.resize', { height: 300 }],
      ['ui.openDialog', { panel: 'confirm', data: { deckId: 'deck-1' } }],
      ['ui.closeDialog', null],
      ['ui.openPage', { page: 'settings' }],
    ])
    host.close()
  })

  it('rejects "ready" when the plugin has no code for the panel', async () => {
    const frame = new FakeFrame()
    const handle = definePlugin({ activate: () => undefined }, { environment: frame })

    const host = frame.connect({ panel: { id: 'missing', slot: 'card.page', context: { oracleId: 'x' } } })

    await assert.rejects(handle.ready, /no code for the panel "missing"/)
    host.close()
  })
})
