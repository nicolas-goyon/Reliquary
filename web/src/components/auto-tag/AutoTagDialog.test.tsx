import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../../App'
import type { AutoTagLine, AutoTagResponse } from '../../api/autoTag'
import type { Deck } from '../../api/decks'
import i18n from '../../i18n'
import { mockApi, session } from '../../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d2'
const solRing = '0190a000-0000-7000-8000-0000000000c1'
const swords = '0190a000-0000-7000-8000-0000000000c3'
const forest = '0190a000-0000-7000-8000-0000000000c4'

function card(name: string, typeLine: string) {
  return { name, layout: 'normal', manaValue: 1, curveManaValue: 1, manaCost: '{1}', typeLine, colorIdentity: [], cardTypes: ['Artifact'], isLand: false, defaultPrintingId: null, faces: [] }
}

function deck(overrides: Partial<Deck> = {}): Deck {
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
    version: 3,
    createdAt: '2026-10-27T10:00:00Z',
    updatedAt: '2026-10-27T10:00:00Z',
    isOwner: true,
    counts: { commander: 0, companion: 0, main: 3, sideboard: 0, considering: 0, tokens: 0 },
    lines: [
      { id: 'l1', oracleId: solRing, printingId: null, finish: null, quantity: 1, zone: 'main', note: '', tagIds: [], categories: [] },
      { id: 'l2', oracleId: swords, printingId: null, finish: null, quantity: 1, zone: 'main', note: '', tagIds: [], categories: ['targeted_interaction'] },
      { id: 'l3', oracleId: forest, printingId: null, finish: null, quantity: 1, zone: 'main', note: '', tagIds: [], categories: [] },
    ],
    cards: {
      [solRing]: card('Sol Ring', 'Artifact'),
      [swords]: card('Swords to Plowshares', 'Instant'),
      [forest]: card('Forest', 'Basic Land — Forest'),
    },
    printings: {},
    tags: {},
    ...overrides,
  }
}

function line(overrides: Partial<AutoTagLine> & Pick<AutoTagLine, 'lineId' | 'oracleId' | 'name'>): AutoTagLine {
  return { zone: 'main', categories: [], suggested: [], source: 'none', reasons: [], changes: false, ...overrides }
}

const suggestions: AutoTagResponse = {
  deckId,
  version: 3,
  lines: [
    line({ lineId: 'l1', oracleId: solRing, name: 'Sol Ring', suggested: ['ramp'], source: 'oracle_tag', reasons: [{ category: 'ramp', source: 'oracle_tag', detail: 'mana-rock' }], changes: true }),
    line({ lineId: 'l2', oracleId: swords, name: 'Swords to Plowshares', categories: ['targeted_interaction'], suggested: ['targeted_interaction'], source: 'oracle_tag', reasons: [{ category: 'targeted_interaction', source: 'oracle_tag', detail: 'spot-removal' }] }),
    line({ lineId: 'l3', oracleId: forest, name: 'Forest', suggested: ['land'], source: 'text_rule', reasons: [{ category: 'land', source: 'text_rule', detail: 'land_type' }], changes: true }),
  ],
}

function handlers(owner = true) {
  return {
    'POST /auth/refresh': () => session(),
    [`GET /decks/${deckId}`]: () => Response.json(deck({ isOwner: owner })),
    [`GET /decks/${deckId}/legality`]: () => Response.json({ format: 'commander', legal: true, problems: [] }),
    [`GET /decks/${deckId}/analysis`]: () => Response.json({ status: 500, code: 'internal_error', detail: 'x' }, { status: 500 }),
    [`GET /decks/${deckId}/auto-tag`]: () => Response.json(suggestions),
  }
}

describe('auto-tag', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', `/decks/${deckId}`)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the suggestions, changes the categories of a line and applies the selection', async () => {
    const applied = deck({
      version: 4,
      lines: deck().lines.map((current) => (current.id === 'l1' ? { ...current, categories: ['ramp', 'synergy'] } : current.id === 'l3' ? { ...current, categories: ['land'] } : current)),
    })
    const calls = mockApi({ ...handlers(), [`POST /decks/${deckId}/auto-tag`]: () => Response.json(applied) })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Auto-tag' }))
    const dialog = await screen.findByRole('dialog', { name: 'Auto-tag' })
    const table = await within(dialog).findByRole('table', { name: 'Suggested categories of the cards' })

    // The lines with no change are hidden at first.
    expect(within(table).queryByRole('rowheader', { name: 'Swords to Plowshares' })).not.toBeInTheDocument()
    const solRingRow = within(table).getByRole('row', { name: /Sol Ring/ })
    expect(solRingRow).toHaveTextContent('Scryfall tag: mana-rock')
    expect(within(table).getByRole('row', { name: /Forest/ })).toHaveTextContent('Card text')
    expect(within(dialog).getByRole('checkbox', { name: 'Apply the suggestion for Sol Ring' })).toBeChecked()

    await user.click(within(dialog).getByRole('checkbox', { name: 'Show only the changes' }))
    expect(within(table).getByRole('rowheader', { name: 'Swords to Plowshares' })).toBeInTheDocument()
    expect(within(dialog).getByRole('checkbox', { name: 'Apply the suggestion for Swords to Plowshares' })).not.toBeChecked()

    await user.click(within(dialog).getByRole('button', { name: 'Change the categories of Sol Ring' }))
    const editor = within(dialog).getByRole('group', { name: 'Categories of Sol Ring' })
    expect(within(editor).getByRole('checkbox', { name: 'Ramp' })).toBeChecked()
    await user.click(within(editor).getByRole('checkbox', { name: 'Synergy' }))

    await user.click(within(dialog).getByRole('button', { name: 'Apply 2 changes' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Auto-tag' })).not.toBeInTheDocument())
    const post = calls.find((call) => call.key === `POST /decks/${deckId}/auto-tag`)
    expect(post?.body).toEqual({
      expectedVersion: 3,
      lines: [
        { lineId: 'l1', categories: ['ramp', 'synergy'] },
        { lineId: 'l3', categories: ['land'] },
      ],
    })
    expect(screen.getAllByText('Synergy').length).toBeGreaterThan(0)
  })

  it('clears the selection and disables the apply button', async () => {
    mockApi(handlers())
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Auto-tag' }))
    const dialog = await screen.findByRole('dialog', { name: 'Auto-tag' })
    await within(dialog).findByRole('table')

    await user.click(within(dialog).getByRole('button', { name: 'Clear the selection' }))

    expect(within(dialog).getByRole('button', { name: 'Apply 0 changes' })).toBeDisabled()
    await user.click(within(dialog).getByRole('button', { name: 'Select all the changes' }))
    expect(within(dialog).getByRole('button', { name: 'Apply 2 changes' })).toBeEnabled()
  })

  it('tells the user to open auto-tag again after a change of the deck', async () => {
    mockApi({
      ...handlers(),
      [`POST /decks/${deckId}/auto-tag`]: () => Response.json({ status: 409, code: 'deck_version_conflict', detail: 'x' }, { status: 409 }),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Auto-tag' }))
    const dialog = await screen.findByRole('dialog', { name: 'Auto-tag' })
    await within(dialog).findByRole('table')
    await user.click(within(dialog).getByRole('button', { name: 'Apply 2 changes' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('The deck changed after auto-tag read it. Close auto-tag and open it again.')
  })

  it('shows no auto-tag button to a user who does not own the deck', async () => {
    mockApi(handlers(false))
    render(<App />)

    await screen.findByRole('button', { name: 'Copy the deck' })
    expect(screen.queryByRole('button', { name: 'Auto-tag' })).not.toBeInTheDocument()
  })
})
