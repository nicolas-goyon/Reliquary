import { afterEach, describe, expect, it } from 'vitest'
import { isPluginPanel, SlotRegistry, type PluginPanelContribution } from '../slots/registry'
import type { HostPeer } from './connection'
import type { HostMethods, PluginMethods } from './methods'
import { registerPluginUi, type PanelPlugin } from './pluginUi'
import { RpcPeer, type PortLike } from './rpc'

const peers: { close(): void }[] = []

const plugin: PanelPlugin = {
  id: 'card-scan',
  name: 'Card scan',
  version: '1.0.0',
  development: false,
  src: '/api/v1/plugins/card-scan/1.0.0/frame',
  slots: ['analysis.tabs', 'plugin.page', 'plugin.dialog'],
  panels: [
    { id: 'stats', slot: 'analysis.tabs', title: 'Scan statistics' },
    { id: 'prices', slot: 'card.page', title: 'Prices' },
    { id: 'settings', slot: 'plugin.page', title: 'Settings' },
    { id: 'confirm', slot: 'plugin.dialog', title: 'Confirm the import' },
  ],
  i18n: { fr: { 'panels.stats.title': 'Statistiques du scan', name: 'Scan de cartes' } },
}

function setup() {
  const channel = new MessageChannel()
  const host: HostPeer = new RpcPeer<HostMethods, PluginMethods>(channel.port1 as unknown as PortLike)
  const frame = new RpcPeer<PluginMethods, HostMethods>(channel.port2 as unknown as PortLike)
  peers.push(host, frame)
  const registry = new SlotRegistry()
  const cleanup = registerPluginUi(host, plugin, registry, { coreApi: '1.1.0', language: () => 'en' })
  return { frame, registry, cleanup }
}

function panels(registry: SlotRegistry, slot: 'analysis.tabs' | 'card.page' | 'plugin.page' | 'plugin.dialog'): PluginPanelContribution[] {
  return registry.list(slot).filter(isPluginPanel) as PluginPanelContribution[]
}

afterEach(() => {
  for (const peer of peers.splice(0)) {
    peer.close()
  }
  window.history.replaceState(null, '', '/')
})

describe('registerPluginUi', () => {
  it('adds the panels of the manifest to the slots that the manifest declares, with the translated titles', () => {
    const { registry } = setup()

    const [stats] = panels(registry, 'analysis.tabs')

    expect(stats).toMatchObject({ kind: 'plugin-panel', id: 'plugin.card-scan.stats', panelId: 'stats', maxHeight: 800 })
    expect(stats.title).toEqual({ en: 'Scan statistics', fr: 'Statistiques du scan' })
    expect(panels(registry, 'plugin.page').map((panel) => panel.panelId)).toEqual(['settings'])
    // The manifest does not declare the slot "card.page", and a dialog goes into its slot only when the plugin opens it.
    expect(panels(registry, 'card.page')).toEqual([])
    expect(panels(registry, 'plugin.dialog')).toEqual([])
  })

  it('opens and closes a dialog of the plugin', async () => {
    const { frame, registry } = setup()

    await frame.call('ui.openDialog', { panel: 'confirm', data: { count: 3 } })
    const [dialog] = panels(registry, 'plugin.dialog')
    expect(dialog).toMatchObject({ panelId: 'confirm', title: { en: 'Confirm the import' } })

    await frame.call('ui.openDialog', { panel: 'confirm', data: { count: 4 } })
    expect(panels(registry, 'plugin.dialog')).toHaveLength(1)
    dialog.close?.()
    expect(panels(registry, 'plugin.dialog')).toHaveLength(1)

    await frame.call('ui.closeDialog', null)
    expect(panels(registry, 'plugin.dialog')).toEqual([])
  })

  it('refuses a dialog or a page that the manifest does not declare', async () => {
    const { frame } = setup()

    await expect(frame.call('ui.openDialog', { panel: 'stats' })).rejects.toMatchObject({ code: -32602 })
    await expect(frame.call('ui.openPage', { page: 'missing' })).rejects.toMatchObject({ code: -32602 })
  })

  it('opens a page of the plugin', async () => {
    const { frame } = setup()

    await frame.call('ui.openPage', { page: 'settings' })

    expect(window.location.pathname).toBe('/plugins/card-scan/settings')
  })

  it('removes the panels and the dialog of the plugin', async () => {
    const { frame, registry, cleanup } = setup()
    await frame.call('ui.openDialog', { panel: 'confirm' })

    cleanup()

    expect(panels(registry, 'analysis.tabs')).toEqual([])
    expect(panels(registry, 'plugin.page')).toEqual([])
    expect(panels(registry, 'plugin.dialog')).toEqual([])
  })
})
