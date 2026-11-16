import { afterEach, describe, expect, it } from 'vitest'
import { isPluginContribution, SlotRegistry, type PluginActionContribution } from '../slots/registry'
import type { HostPeer } from './connection'
import { maxActionsPerSlot, registerContributionMethods, type ContributingPlugin } from './contributions'
import type { HostMethods, PluginMethods } from './methods'
import { errorCodes } from './protocol'
import { RpcPeer, type PortLike } from './rpc'

const peers: { close(): void }[] = []

/** Connects a host peer with the contribution methods to a fake plugin peer. */
function setup(overrides: Partial<ContributingPlugin> = {}) {
  const channel = new MessageChannel()
  const host: HostPeer = new RpcPeer<HostMethods, PluginMethods>(channel.port1 as unknown as PortLike)
  const plugin = new RpcPeer<PluginMethods, HostMethods>(channel.port2 as unknown as PortLike)
  peers.push(host, plugin)
  const registry = new SlotRegistry()
  const data: ContributingPlugin = { id: 'moxfield-sync', name: 'Moxfield sync', slots: ['deck.actions', 'app.menu'], development: false, ...overrides }
  const cleanup = registerContributionMethods(host, data, registry)
  return { host, plugin, registry, cleanup }
}

function onlyPluginItems(registry: SlotRegistry, slot: 'deck.actions' | 'app.menu' | 'navigation'): PluginActionContribution[] {
  return registry.list(slot).filter(isPluginContribution) as PluginActionContribution[]
}

afterEach(() => {
  for (const peer of peers.splice(0)) {
    peer.close()
  }
})

describe('plugin contributions', () => {
  it('adds a button as data after the buttons of the core, and sends the action with the IDs of the context', async () => {
    const { plugin, registry } = setup()
    registry.register('deck.actions', { id: 'core.test', order: 5, render: () => null })
    const actions: unknown[] = []
    plugin.handle('ui.action', (params) => {
      actions.push(params)
      return null
    })

    await plugin.call('ui.contribute', { slot: 'deck.actions', contribution: { id: 'sync', label: { en: 'Sync', fr: 'Synchroniser' }, icon: 'sync', order: 2 } })
    const [core, item] = registry.list('deck.actions')
    await (item as PluginActionContribution).run({ deckId: 'deck-1', deck: { name: 'Secret deck data' } })

    expect(core.id).toBe('core.test')
    expect(item).toMatchObject({ kind: 'plugin', id: 'plugin.moxfield-sync.sync', order: 1002, label: { en: 'Sync', fr: 'Synchroniser' }, icon: 'sync' })
    expect(item).toMatchObject({ plugin: { id: 'moxfield-sync', name: 'Moxfield sync', development: false } })
    expect(actions).toEqual([{ slot: 'deck.actions', id: 'sync', context: { deckId: 'deck-1' } }])
  })

  it('accepts a text as an English label, and replaces an item with the same ID', async () => {
    const { plugin, registry } = setup()

    await plugin.call('ui.contribute', { slot: 'app.menu', contribution: { id: 'open', label: 'Open' } })
    await plugin.call('ui.contribute', { slot: 'app.menu', contribution: { id: 'open', label: 'Open Moxfield' } })

    expect(onlyPluginItems(registry, 'app.menu').map((item) => item.label)).toEqual([{ en: 'Open Moxfield' }])
  })

  it.each([
    ['a slot that the manifest does not declare', { slot: 'navigation', contribution: { id: 'x', label: 'X' } }, errorCodes.permissionDenied],
    ['a slot with no plugin data', { slot: 'account.page', contribution: { id: 'x', label: 'X' } }, errorCodes.invalidParams],
    ['an incorrect ID', { slot: 'deck.actions', contribution: { id: 'Not An ID', label: 'X' } }, errorCodes.invalidParams],
    ['a label with no English text', { slot: 'deck.actions', contribution: { id: 'x', label: { fr: 'X' } } }, errorCodes.invalidParams],
    ['a label that is too long', { slot: 'deck.actions', contribution: { id: 'x', label: 'x'.repeat(41) } }, errorCodes.invalidParams],
    ['an empty label', { slot: 'deck.actions', contribution: { id: 'x', label: ' ' } }, errorCodes.invalidParams],
    ['an icon that is not in the set', { slot: 'deck.actions', contribution: { id: 'x', label: 'X', icon: '<svg>' } }, errorCodes.invalidParams],
    ['an order that is not a number', { slot: 'deck.actions', contribution: { id: 'x', label: 'X', order: 'first' } }, errorCodes.invalidParams],
  ])('refuses %s', async (_case, params, code) => {
    const { plugin, registry } = setup()

    await expect(plugin.call('ui.contribute', params)).rejects.toMatchObject({ code })
    expect(onlyPluginItems(registry, 'deck.actions')).toEqual([])
  })

  it(`refuses more than ${maxActionsPerSlot} items in one slot`, async () => {
    const { plugin, registry } = setup()
    for (const id of ['a', 'b', 'c']) {
      await plugin.call('ui.contribute', { slot: 'deck.actions', contribution: { id, label: id } })
    }

    await expect(plugin.call('ui.contribute', { slot: 'deck.actions', contribution: { id: 'd', label: 'd' } })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    expect(onlyPluginItems(registry, 'deck.actions')).toHaveLength(3)
  })

  it('removes one item, then all the items of the plugin', async () => {
    const { plugin, registry, cleanup } = setup()
    await plugin.call('ui.contribute', { slot: 'deck.actions', contribution: { id: 'a', label: 'A' } })
    await plugin.call('ui.contribute', { slot: 'deck.actions', contribution: { id: 'b', label: 'B' } })
    await plugin.call('ui.contribute', { slot: 'app.menu', contribution: { id: 'c', label: 'C' } })

    await plugin.call('ui.withdraw', { slot: 'deck.actions', id: 'a' })
    expect(onlyPluginItems(registry, 'deck.actions').map((item) => item.id)).toEqual(['plugin.moxfield-sync.b'])

    cleanup()
    expect(onlyPluginItems(registry, 'deck.actions')).toEqual([])
    expect(onlyPluginItems(registry, 'app.menu')).toEqual([])
  })
})
