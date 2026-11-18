import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, it } from 'node:test'
import { plugin } from '../template/src/plugin.ts'
import { manifestPermissions, panelSlots } from './manifest.ts'
import type { HostMethods, PluginMethods } from './methods.ts'
import { definePlugin, type PluginEnvironment } from './plugin.ts'
import { connectMessageType, coreApiVersion, type ConnectMessage } from './protocol.ts'
import { RpcPeer, type PortLike } from './rpc.ts'

/**
 * The plugin template of sdk/template runs against a fake core, in each pull request.
 * The test proves that the SDK and the template agree: the contributions, the request to the public API, the notice and the panel.
 */

interface FakeElement {
  textContent: string
  children: FakeElement[]
  ownerDocument: { createElement(): FakeElement }
  append(child: FakeElement): void
}

function element(): FakeElement {
  const node: FakeElement = {
    textContent: '',
    children: [],
    ownerDocument: { createElement: element },
    append: (child) => void node.children.push(child),
  }
  return node
}

/** Starts the template in a fake plugin frame, and gives the peer of the core. */
function start(overrides: Partial<ConnectMessage> = {}) {
  const listeners = new Set<(event: MessageEvent) => void>()
  const body = element()
  const parent = { postMessage: () => undefined }
  const environment: PluginEnvironment = {
    parent,
    addEventListener: (_type, listener) => void listeners.add(listener),
    removeEventListener: (_type, listener) => void listeners.delete(listener),
    document: { body: body as unknown as HTMLElement, documentElement: { scrollHeight: 40 } as unknown as HTMLElement },
  }
  const handle = definePlugin(plugin, { environment })
  const channel = new MessageChannel()
  const core = new RpcPeer<HostMethods, PluginMethods>(channel.port1 as unknown as PortLike)
  const contributions: unknown[] = []
  const notices: unknown[] = []
  const requests: unknown[] = []
  core.handle('ui.contribute', (params) => {
    contributions.push(params)
    return null
  })
  core.handle('ui.notify', (params) => {
    notices.push(params)
    return null
  })
  core.handle('ui.resize', () => null)
  core.handle('api.fetch', (params) => {
    requests.push(params)
    return params.path === '/decks/deck-1'
      ? { status: 200, body: { name: 'Atraxa', lines: [{ quantity: 1 }, { quantity: 38 }, { quantity: 61 }] } }
      : { status: 403, body: { code: 'scope_missing' } }
  })
  const message: ConnectMessage = { type: connectMessageType, coreApi: coreApiVersion, plugin: { id: 'deck-size', version: '0.1.0' }, language: 'en', development: true, ...overrides }
  for (const listener of [...listeners]) {
    listener({ source: parent, data: message, ports: [channel.port2] } as unknown as MessageEvent)
  }
  return { handle, core, body, contributions, notices, requests }
}

describe('plugin template', () => {
  it('has a plugin manifest that agrees with its code', () => {
    const manifest = JSON.parse(readFileSync(new URL('../template/public/manifest.json', import.meta.url), 'utf8')) as {
      id: string
      permissions: string[]
      slots: string[]
      panels: { id: string; slot: string }[]
    }

    assert.match(manifest.id, /^[a-z0-9][a-z0-9-]{1,63}$/)
    assert.deepEqual(manifest.permissions.filter((scope) => !(manifestPermissions as readonly string[]).includes(scope)), [])
    assert.deepEqual(manifest.panels.map((panel) => panel.id), Object.keys(plugin.panels ?? {}))
    for (const panel of manifest.panels) {
      assert.ok((panelSlots as readonly string[]).includes(panel.slot) && manifest.slots.includes(panel.slot), panel.slot)
    }
    assert.ok(manifest.slots.includes('deck.actions'))
  })

  it('adds a deck action that reads the deck and shows the number of cards', async () => {
    const { handle, core, contributions, notices, requests } = start()
    await handle.ready

    await core.call('ui.action', { slot: 'deck.actions', id: 'count', context: { deckId: 'deck-1' } })
    await core.call('ui.action', { slot: 'deck.actions', id: 'count', context: { deckId: 'deck-2' } })
    core.close()

    assert.deepEqual(contributions, [{ slot: 'deck.actions', contribution: { id: 'count', label: { en: 'Count the cards', fr: 'Compter les cartes' }, icon: 'chart' } }])
    assert.deepEqual(requests, [{ path: '/decks/deck-1' }, { path: '/decks/deck-2' }])
    assert.deepEqual(notices, [
      { message: 'Atraxa: 100 cards.', tone: 'success' },
      { message: 'The plugin did not read the deck.', tone: 'error' },
    ])
  })

  it('renders the analysis panel in French, as text', async () => {
    const { handle, core, body, contributions } = start({ language: 'fr', panel: { id: 'size', slot: 'analysis.tabs', context: { deckId: 'deck-1' } } })
    await handle.ready
    core.close()

    // A panel frame does not run "activate", so it adds no deck action.
    assert.deepEqual(contributions, [])
    assert.equal(body.children[0].textContent, 'Atraxa : 100 cartes.')
  })
})
