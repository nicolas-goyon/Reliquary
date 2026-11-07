import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { Deck, DeckCard, DeckLine } from '../api/decks'
import { clickLine, emptySelection } from '../components/deck-table/deckTable'
import i18n from '../i18n'
import { SlotRegistry } from '../slots/registry'
import { registerCoreContributions } from '../slots/coreContributions'
import { mockApi, session } from '../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d1'

function card(name: string, manaValue: number): DeckCard {
  return { name, layout: 'normal', manaValue, curveManaValue: manaValue, manaCost: null, typeLine: 'Artifact', colorIdentity: [], cardTypes: ['Artifact'], isLand: false, defaultPrintingId: null, faces: [] }
}

/** A deck with 12 lines in the main deck: "Card 01" to "Card 12". */
function deck(overrides: Partial<Deck> = {}): Deck {
  const lines: DeckLine[] = Array.from({ length: 12 }, (_, index) => ({
    id: `line-${index + 1}`,
    oracleId: `oracle-${index + 1}`,
    printingId: null,
    finish: null,
    quantity: 1,
    zone: 'main',
    note: '',
    tagIds: [],
    categories: [],
  }))
  return {
    id: deckId,
    ownerId: 'owner',
    name: 'Artifacts',
    format: 'commander',
    visibility: 'private',
    folderId: null,
    description: '',
    sourceDeckId: null,
    pinned: false,
    archived: false,
    version: 7,
    createdAt: '2026-10-27T10:00:00Z',
    updatedAt: '2026-10-27T10:00:00Z',
    isOwner: true,
    counts: { commander: 0, companion: 0, main: 12, sideboard: 0, considering: 0, tokens: 0 },
    lines,
    cards: Object.fromEntries(lines.map((line, index) => [line.oracleId, card(`Card ${String(index + 1).padStart(2, '0')}`, 12 - index)])),
    printings: {},
    tags: {},
    ...overrides,
  }
}

const legal = { format: 'commander', legal: true, problems: [] }

function row(name: string): HTMLElement {
  return screen.getByRole('row', { name: new RegExp(name) })
}

/** Clicks a cell of a row that is not a link or a checkbox. */
async function clickRow(user: ReturnType<typeof userEvent.setup>, name: string) {
  await user.click(within(row(name)).getAllByRole('cell')[1])
}

async function openTable(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Table mode' }))
  return screen.findByRole('table', { name: /The lines of the deck/ })
}

describe('deck table mode', () => {
  beforeEach(async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('selects 10 lines with Shift+click and moves them to the considering zone in one request', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`POST /decks/${deckId}/lines/group`]: ({ body }) => {
        const { lineIds } = body as { lineIds: string[] }
        const moved = deck().lines.map((line) => (lineIds.includes(line.id) ? { ...line, zone: 'considering' as const } : line))
        return Response.json(deck({ version: 8, lines: moved, counts: { ...deck().counts, main: 2, considering: 10 } }))
      },
    })
    const user = userEvent.setup()
    render(<App />)
    await openTable(user)

    await clickRow(user, 'Card 01')
    await user.keyboard('{Shift>}')
    await clickRow(user, 'Card 10')
    await user.keyboard('{/Shift}')

    const toolbar = screen.getByRole('toolbar', { name: 'Group actions' })
    expect(within(toolbar).getByText('10 selected lines')).toBeInTheDocument()
    expect(row('Card 05')).toHaveAttribute('aria-selected', 'true')
    expect(row('Card 11')).toHaveAttribute('aria-selected', 'false')
    expect(within(toolbar).getByLabelText('New zone')).toHaveValue('considering')
    await user.click(within(toolbar).getByRole('button', { name: 'Move' }))

    await waitFor(() => expect(within(row('Card 03')).getByText('Considering')).toBeInTheDocument())
    const groupCalls = calls.filter((call) => call.key === `POST /decks/${deckId}/lines/group`)
    expect(groupCalls).toHaveLength(1)
    expect(groupCalls[0].body).toEqual({
      action: 'move',
      zone: 'considering',
      lineIds: Array.from({ length: 10 }, (_, index) => `line-${index + 1}`),
      expectedVersion: 7,
    })
    expect(within(row('Card 11')).getByText('Main deck')).toBeInTheDocument()
  })

  it('toggles a line with Ctrl+click and selects all the lines', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
    })
    const user = userEvent.setup()
    render(<App />)
    await openTable(user)
    const toolbar = screen.getByRole('toolbar', { name: 'Group actions' })
    expect(within(toolbar).getByRole('button', { name: 'Move' })).toBeDisabled()

    await clickRow(user, 'Card 02')
    await user.keyboard('{Control>}')
    await clickRow(user, 'Card 04')
    await clickRow(user, 'Card 02')
    await user.keyboard('{/Control}')

    expect(row('Card 02')).toHaveAttribute('aria-selected', 'false')
    expect(row('Card 04')).toHaveAttribute('aria-selected', 'true')
    expect(within(toolbar).getByText('1 selected line')).toBeInTheDocument()

    await user.click(screen.getByRole('checkbox', { name: 'Select Card 07' }))
    expect(within(toolbar).getByText('2 selected lines')).toBeInTheDocument()

    await user.click(screen.getByRole('checkbox', { name: 'Select all the lines' }))
    expect(within(toolbar).getByText('12 selected lines')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Card name'), 'Card 1')
    expect(screen.getAllByRole('row')).toHaveLength(4)
    expect(within(toolbar).getByText('3 selected lines')).toBeInTheDocument()
  })

  it('sorts the lines by mana value', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
    })
    const user = userEvent.setup()
    render(<App />)
    const table = await openTable(user)

    await user.click(within(table).getByRole('button', { name: 'Mana value' }))

    const names = within(table).getAllByRole('link').map((link) => link.textContent)
    expect(names[0]).toBe('Card 12')
    expect(within(table).getByRole('columnheader', { name: /Mana value/ })).toHaveAttribute('aria-sort', 'ascending')
  })

  it('shows the columns of the table.columns slot', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
    })
    const registry = new SlotRegistry()
    registerCoreContributions(registry)
    registry.register('table.columns', {
      id: 'test.price',
      render: (context) => (context.part === 'header' ? 'Price' : `${context.card?.name ?? ''}: ${context.line.quantity * 2} €`),
    })
    const user = userEvent.setup()
    render(<App registry={registry} />)
    const table = await openTable(user)

    expect(within(table).getByRole('columnheader', { name: 'Price' })).toBeInTheDocument()
    expect(within(row('Card 03')).getByRole('cell', { name: 'Card 03: 2 €' })).toBeInTheDocument()
  })
})

describe('selection of the table', () => {
  const order = ['a', 'b', 'c', 'd']

  it('selects one line, a range and toggles a line', () => {
    const one = clickLine(emptySelection, order, 'b', { range: false, toggle: false })
    expect([...one.ids]).toEqual(['b'])
    const range = clickLine(one, order, 'd', { range: true, toggle: false })
    expect([...range.ids]).toEqual(['b', 'c', 'd'])
    const backwards = clickLine(range, order, 'a', { range: true, toggle: false })
    expect([...backwards.ids]).toEqual(['a', 'b'])
    const toggled = clickLine(backwards, order, 'a', { range: false, toggle: true })
    expect([...toggled.ids]).toEqual(['b'])
  })
})
