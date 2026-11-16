import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { Deck, DeckLine } from '../api/decks'
import i18n from '../i18n'
import { mockApi, problem, session } from '../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d1'
const solRing = '0190a000-0000-7000-8000-0000000000c1'
const forest = '0190a000-0000-7000-8000-0000000000c3'

function card(name: string) {
  return { name, layout: 'normal', manaValue: 1, curveManaValue: 1, manaCost: '{1}', typeLine: 'Artifact', colorIdentity: [], cardTypes: ['Artifact'], isLand: false, defaultPrintingId: null, faces: [] }
}

function line(id: string, oracleId: string, note: string): DeckLine {
  return { id, oracleId, printingId: null, finish: null, quantity: 1, zone: 'main', note, tagIds: [], categories: [] }
}

function deck(overrides: Partial<Deck> = {}): Deck {
  return {
    id: deckId,
    ownerId: 'owner',
    name: 'Notes',
    format: 'commander',
    visibility: 'public',
    folderId: null,
    description: '',
    sourceDeckId: null,
    pinned: false,
    archived: false,
    version: 3,
    createdAt: '2026-10-27T10:00:00Z',
    updatedAt: '2026-10-27T10:00:00Z',
    isOwner: true,
    counts: { commander: 0, companion: 0, main: 2, sideboard: 0, considering: 0, tokens: 0 },
    lines: [line('l1', solRing, 'Fast mana'), line('l2', forest, '')],
    cards: { [solRing]: card('Sol Ring'), [forest]: card('Forest') },
    printings: {},
    tags: {},
    ...overrides,
  }
}

const legal = { format: 'commander', legal: true, problems: [] }

/** A PATCH handler that keeps the note of the request on line l1. */
function patchNote({ body }: { body: unknown }) {
  const { note } = body as { note: string }
  return Response.json(deck({ version: 4, lines: [line('l1', solRing, note), line('l2', forest, '')] }))
}

describe('notes of the deck lines', () => {
  beforeEach(async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the note on the deck page and the owner changes it', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`PATCH /decks/${deckId}/lines/l1`]: patchNote,
    })
    const user = userEvent.setup()
    render(<App />)

    const main = await screen.findByRole('region', { name: /Main deck/ })
    expect(within(main).getByText('Fast mana')).toBeInTheDocument()
    expect(within(main).getByRole('button', { name: 'Add a note for Forest' })).toBeInTheDocument()

    await user.click(within(main).getByRole('button', { name: 'Edit the note for Sol Ring' }))
    const field = within(main).getByLabelText('Note for Sol Ring')
    expect(field).toHaveValue('Fast mana')
    await user.clear(field)
    await user.type(field, '  Turn one ramp  {enter}')

    expect(await within(main).findByText('Turn one ramp')).toBeInTheDocument()
    expect(within(main).queryByLabelText('Note for Sol Ring')).not.toBeInTheDocument()
    expect(calls.filter((call) => call.key === `PATCH /decks/${deckId}/lines/l1`).map((call) => call.body)).toEqual([{ note: 'Turn one ramp' }])
  })

  it('cancels the change with Escape and sends no request', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Edit the note for Sol Ring' }))
    await user.type(screen.getByLabelText('Note for Sol Ring'), ' and more{Escape}')

    expect(screen.queryByLabelText('Note for Sol Ring')).not.toBeInTheDocument()
    expect(screen.getByText('Fast mana')).toBeInTheDocument()
    expect(calls.some((call) => call.key.startsWith('PATCH'))).toBe(false)
  })

  it('shows the note in read-only mode to the other users', async () => {
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      [`GET /decks/${deckId}`]: () => Response.json(deck({ isOwner: false })),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
    })
    render(<App />)

    const main = await screen.findByRole('region', { name: /Main deck/ })
    expect(within(main).getByText('Fast mana')).toBeInTheDocument()
    expect(within(main).queryByRole('button', { name: /note/ })).not.toBeInTheDocument()
  })

  it('shows a note column in table mode and removes a note', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`PATCH /decks/${deckId}/lines/l1`]: patchNote,
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Table mode' }))
    const table = await screen.findByRole('table', { name: /The lines of the deck/ })
    expect(within(table).getByRole('columnheader', { name: 'Note' })).toBeInTheDocument()
    const row = within(table).getByRole('row', { name: /Sol Ring/ })
    expect(within(row).getByText('Fast mana')).toBeInTheDocument()

    await user.click(within(row).getByRole('button', { name: 'Edit the note for Sol Ring' }))
    expect(row).toHaveAttribute('aria-selected', 'false')
    await user.clear(within(row).getByLabelText('Note for Sol Ring'))
    await user.click(within(row).getByRole('button', { name: 'Save the note' }))

    await waitFor(() => expect(within(table).getByRole('button', { name: 'Add a note for Sol Ring' })).toBeInTheDocument())
    expect(calls.find((call) => call.key === `PATCH /decks/${deckId}/lines/l1`)?.body).toEqual({ note: '' })
  })

  it('shows the error of the API and keeps the field open', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`PATCH /decks/${deckId}/lines/l1`]: () => problem(400, 'validation_failed', [{ field: 'note', code: 'too_long', message: 'The note is too long.' }]),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Edit the note for Sol Ring' }))
    await user.type(screen.getByLabelText('Note for Sol Ring'), '!{enter}')

    const main = screen.getByRole('region', { name: /Main deck/ })
    expect(await within(main).findByRole('alert')).toBeInTheDocument()
    expect(within(main).getByLabelText('Note for Sol Ring')).toHaveValue('Fast mana!')
  })
})
