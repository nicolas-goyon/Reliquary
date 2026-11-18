import { describe, expect, it } from 'vitest'
import { layoutKey, SlotRegistry, type PluginActionContribution, type PluginPanelContribution } from './registry'

function action(pluginId: string, id: string, order: number): PluginActionContribution {
  return { kind: 'plugin', id: `plugin.${pluginId}.${id}`, order, plugin: { id: pluginId, name: pluginId, development: false }, label: { en: id }, run: async () => undefined }
}

function setup() {
  const registry = new SlotRegistry()
  registry.register('deck.actions', { id: 'core.print', order: 10, render: () => null })
  registry.register('deck.actions', action('alpha', 'sync', 1000))
  registry.register('deck.actions', action('alpha', 'export', 1001))
  registry.register('deck.actions', action('beta', 'price', 1000))
  return registry
}

const ids = (registry: SlotRegistry, all = false) => (all ? registry.listAll('deck.actions') : registry.list('deck.actions')).map((item) => item.id)

describe('layout of the plugin contributions', () => {
  it('keeps the order of the contributions without a layout', () => {
    expect(ids(setup())).toEqual(['core.print', 'plugin.alpha.sync', 'plugin.beta.price', 'plugin.alpha.export'])
  })

  it('puts the contributions of the plugin with the smaller position first, after the contributions of the core', () => {
    const registry = setup()

    registry.setPluginLayouts({ beta: { position: -1, hidden: [] } })

    expect(ids(registry)).toEqual(['core.print', 'plugin.beta.price', 'plugin.alpha.sync', 'plugin.alpha.export'])
  })

  it('hides the contributions that the user hides, and keeps them in the full list', () => {
    const registry = setup()
    let changes = 0
    registry.subscribe(() => (changes += 1))

    registry.setPluginLayouts({ alpha: { position: 0, hidden: ['deck.actions:sync', 'app.menu:export'] } })

    expect(ids(registry)).toEqual(['core.print', 'plugin.beta.price', 'plugin.alpha.export'])
    expect(ids(registry, true)).toContain('plugin.alpha.sync')
    expect(changes).toBe(1)
  })

  it('gives no layout key to a contribution of the core, to a plugin page and to a plugin dialog', () => {
    const panel: PluginPanelContribution = {
      kind: 'plugin-panel',
      id: 'plugin.alpha.stats',
      plugin: { id: 'alpha', name: 'Alpha', development: false },
      panelId: 'stats',
      title: { en: 'Statistics' },
      maxHeight: 800,
      mount: () => ({ update: () => undefined, close: () => undefined }),
    }

    expect(layoutKey('analysis.tabs', panel)).toBe('analysis.tabs:stats')
    expect(layoutKey('plugin.page', panel)).toBeNull()
    expect(layoutKey('plugin.dialog', panel)).toBeNull()
    expect(layoutKey('deck.actions', { id: 'core.print', render: () => null })).toBeNull()
  })
})
