import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import i18n from '../i18n'
import { testDeck } from '../test/fixtures'
import { SlotRegistry, SlotRegistryContext, slotNames, type PluginActionContribution } from './registry'
import { Slot } from './Slot'

const context = { deckId: 'deck-1', deck: testDeck({ id: 'deck-1' }) }

function renderSlot(registry: SlotRegistry) {
  return render(
    <SlotRegistryContext.Provider value={registry}>
      <Slot name="deck.actions" context={context} />
    </SlotRegistryContext.Provider>,
  )
}

describe('slots', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('names the slots of the architecture', () => {
    expect(slotNames).toEqual(['navigation', 'app.menu', 'deck.actions', 'analysis.tabs', 'table.columns', 'card.page', 'account.page'])
  })

  it('renders each contribution in its own container, in order', () => {
    const registry = new SlotRegistry()
    registry.register('deck.actions', { id: 'b.second', order: 20, render: ({ deckId }) => <button type="button">Second {deckId}</button> })
    registry.register('deck.actions', { id: 'a.first', order: 10, render: () => <button type="button">First</button> })

    renderSlot(registry)

    const containers = document.querySelectorAll('[data-slot="deck.actions"] > .slot-contribution')
    expect([...containers].map((container) => container.getAttribute('data-contribution'))).toEqual(['a.first', 'b.second'])
    expect(screen.getByRole('button', { name: 'Second deck-1' })).toBeInTheDocument()
  })

  it('keeps the other contributions when one contribution fails', async () => {
    await i18n.changeLanguage('en')
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const registry = new SlotRegistry()
    registry.register('deck.actions', {
      id: 'broken',
      render: () => {
        throw new Error('Broken contribution')
      },
    })
    registry.register('deck.actions', { id: 'working', render: () => <p>Working</p> })

    renderSlot(registry)

    expect(screen.getByText('Working')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('This part of the page has an error.')
  })

  it('renders again when a contribution arrives or leaves', () => {
    const registry = new SlotRegistry()
    renderSlot(registry)

    let unregister = () => {}
    act(() => {
      unregister = registry.register('deck.actions', { id: 'late', render: () => <p>Late</p> })
    })
    expect(screen.getByText('Late')).toBeInTheDocument()

    act(() => unregister())
    expect(screen.queryByText('Late')).not.toBeInTheDocument()
  })

  it('refuses two contributions with the same ID in one slot', () => {
    const registry = new SlotRegistry()
    registry.register('deck.actions', { id: 'same', render: () => null })

    expect(() => registry.register('deck.actions', { id: 'same', render: () => null })).toThrow(/already has a contribution/)
  })
})

describe('plugin contributions in slots', () => {
  function pluginButton(overrides: Partial<PluginActionContribution> = {}): PluginActionContribution {
    return {
      kind: 'plugin',
      id: 'plugin.moxfield-sync.sync',
      order: 1000,
      plugin: { id: 'moxfield-sync', name: 'Moxfield sync', development: false },
      label: { en: 'Sync', fr: 'Synchroniser' },
      icon: 'sync',
      run: vi.fn<(context: unknown) => Promise<void>>(async () => undefined),
      ...overrides,
    }
  }

  afterEach(async () => {
    await i18n.changeLanguage('en')
    vi.restoreAllMocks()
  })

  it('renders the data of a plugin with a button of the core, in the language of the interface', async () => {
    await i18n.changeLanguage('fr')
    const registry = new SlotRegistry()
    const contribution = pluginButton()
    registry.register('deck.actions', contribution)
    renderSlot(registry)

    await userEvent.click(await screen.findByRole('button', { name: 'Synchroniser' }))

    expect(contribution.run).toHaveBeenCalledWith(context)
    expect(document.querySelector('[data-contribution="plugin.moxfield-sync.sync"] svg.plugin-icon')).not.toBeNull()
    expect(screen.queryByText('Non vérifié.')).not.toBeInTheDocument()
  })

  it('uses the English label when the plugin has no text for the language', async () => {
    await i18n.changeLanguage('fr')
    const registry = new SlotRegistry()
    registry.register('deck.actions', pluginButton({ label: { en: 'Sync' } }))

    renderSlot(registry)

    expect(await screen.findByRole('button', { name: 'Sync' })).toBeInTheDocument()
  })

  it('shows the "Not checked" badge on a contribution of a development plugin', async () => {
    const registry = new SlotRegistry()
    registry.register('deck.actions', pluginButton({ plugin: { id: 'dev', name: 'Dev plugin', development: true } }))

    renderSlot(registry)
    await screen.findByRole('button', { name: 'Sync' })

    const item = document.querySelector('[data-contribution="plugin.moxfield-sync.sync"]')!
    expect(item).toHaveTextContent('Not checked.')
    expect(item.querySelector('.plugin-not-checked-badge')).toHaveAttribute('title', expect.stringContaining('The plugin Dev plugin comes from a development URL.'))
  })

  it('shows an error when the plugin does not complete the action, and enables the button again', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const registry = new SlotRegistry()
    registry.register('deck.actions', pluginButton({ run: async () => Promise.reject(new Error('Timeout')) }))
    renderSlot(registry)

    await userEvent.click(await screen.findByRole('button', { name: 'Sync' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The plugin Moxfield sync did not complete the action.')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sync' })).toBeEnabled())
  })
})
