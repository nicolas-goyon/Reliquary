import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import i18n from '../i18n'
import { testDeck } from '../test/fixtures'
import { SlotRegistry, SlotRegistryContext, slotNames } from './registry'
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
