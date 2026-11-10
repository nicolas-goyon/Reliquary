import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { tabTitle } from '../api/analysis'
import i18n from '../i18n'
import { PluginPage } from '../pages/PluginPage'
import { SlotRegistry, SlotRegistryContext, type PanelHandle, type PanelState, type PluginPanelContribution } from '../slots/registry'
import { Slot } from '../slots/Slot'
import { PluginDialogs } from './PluginDialogs'

/** A panel contribution with a fake frame: `mount` adds a marker to the element. */
function panel(overrides: Partial<PluginPanelContribution> = {}) {
  const handle = { update: vi.fn<(context: unknown) => void>(), close: vi.fn<() => void>() }
  let setState: (state: PanelState) => void = () => undefined
  const mount = vi.fn<PluginPanelContribution['mount']>((element, _context, onState) => {
    setState = onState
    element.append(Object.assign(document.createElement('iframe'), { title: 'Fake panel frame' }))
    return handle as PanelHandle
  })
  const contribution: PluginPanelContribution = {
    kind: 'plugin-panel',
    id: 'plugin.card-scan.prices',
    order: 1000,
    plugin: { id: 'card-scan', name: 'Card scan', development: false },
    panelId: 'prices',
    title: { en: 'Prices', fr: 'Prix' },
    maxHeight: 600,
    mount,
    ...overrides,
  }
  return { contribution, mount, handle, state: (state: PanelState) => act(() => setState(state)) }
}

function withRegistry(registry: SlotRegistry, children: React.ReactNode) {
  return <SlotRegistryContext.Provider value={registry}>{children}</SlotRegistryContext.Provider>
}

describe('plugin panels', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en')
  })

  it('creates the frame of a card page panel only when the user opens it, and removes it when the user closes it', async () => {
    const registry = new SlotRegistry()
    const { contribution, mount, handle } = panel()
    registry.register('card.page', contribution)
    render(withRegistry(registry, <Slot name="card.page" context={{ oracleId: 'oracle-1' }} />))

    const summary = await screen.findByText('Prices')
    expect(mount).not.toHaveBeenCalled()

    await userEvent.click(summary)
    expect(mount).toHaveBeenCalledWith(expect.any(HTMLElement), { oracleId: 'oracle-1' }, expect.any(Function))
    expect(screen.getByTitle('Fake panel frame')).toBeInTheDocument()

    await userEvent.click(summary)
    expect(handle.close).toHaveBeenCalledTimes(1)
  })

  it('sends the new context to the open panel, and shows the state of the frame', async () => {
    const registry = new SlotRegistry()
    const { contribution, handle, state } = panel()
    registry.register('plugin.page', contribution)
    const view = render(withRegistry(registry, <Slot name="plugin.page" context={{ pluginId: 'card-scan', page: 'prices' }} />))

    expect(await screen.findByRole('status')).toHaveTextContent('The plugin Card scan loads its panel.')
    state('ready')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()

    view.rerender(withRegistry(registry, <Slot name="plugin.page" context={{ pluginId: 'card-scan', page: 'other' }} />))
    expect(handle.update).toHaveBeenLastCalledWith({ pluginId: 'card-scan', page: 'other' })

    state('failed')
    expect(screen.getByRole('alert')).toHaveTextContent('The panel of the plugin Card scan did not answer.')
  })

  it('shows the "Not checked" banner above the panel of a development plugin', async () => {
    const registry = new SlotRegistry()
    registry.register('plugin.page', panel({ plugin: { id: 'dev', name: 'Dev plugin', development: true } }).contribution)
    render(withRegistry(registry, <Slot name="plugin.page" context={{ pluginId: 'dev', page: 'prices' }} />))

    expect(await screen.findByRole('note')).toHaveTextContent('Not checked. The plugin Dev plugin comes from a development URL.')
  })

  it('gives the translated title of a plugin panel to the analysis tabs', async () => {
    await i18n.changeLanguage('fr')

    expect(tabTitle(panel().contribution, {} as never)).toBe('Prix')
  })

  it('shows the plugin page with the panel of the plugin, or a message when the plugin has no such page', async () => {
    const registry = new SlotRegistry()
    const view = render(withRegistry(registry, <PluginPage pluginId="card-scan" page="prices" />))
    expect(screen.getByRole('status')).toHaveTextContent('This plugin page is not available.')

    act(() => {
      registry.register('plugin.page', panel().contribution)
    })
    view.rerender(withRegistry(registry, <PluginPage pluginId="card-scan" page="prices" />))

    expect(screen.getByRole('heading', { level: 1, name: 'Prices' })).toBeInTheDocument()
    expect(await screen.findByTitle('Fake panel frame')).toBeInTheDocument()
  })

  it('shows a dialog of a plugin, and closes it with the button or the Escape key', async () => {
    const registry = new SlotRegistry()
    const close = vi.fn<() => void>()
    registry.register('plugin.dialog', panel({ id: 'plugin.card-scan.confirm', title: { en: 'Confirm the import' }, close }).contribution)
    render(withRegistry(registry, <PluginDialogs />))

    const dialog = screen.getByRole('dialog', { name: 'Confirm the import' })
    expect(await screen.findByTitle('Fake panel frame')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    await userEvent.type(dialog, '{Escape}')

    expect(close).toHaveBeenCalledTimes(2)
  })
})
