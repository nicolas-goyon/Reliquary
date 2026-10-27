import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { Deck } from '../api/decks'
import i18n from '../i18n'
import { mockApi, problem, session } from '../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d1'
const solRing = '0190a000-0000-7000-8000-0000000000c1'
const atraxa = '0190a000-0000-7000-8000-0000000000c2'

function deck(overrides: Partial<Deck> = {}): Deck {
  return {
    id: deckId,
    ownerId: 'owner',
    name: 'Atraxa superfriends',
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
    counts: { commander: 1, companion: 0, main: 1, sideboard: 0, considering: 0, tokens: 0 },
    lines: [
      { id: 'l1', oracleId: atraxa, printingId: null, finish: null, quantity: 1, zone: 'commander', note: '', tagIds: [], categories: [] },
      { id: 'l2', oracleId: solRing, printingId: null, finish: 'foil', quantity: 1, zone: 'main', note: '', tagIds: ['t1'], categories: ['ramp'] },
    ],
    cards: {
      [atraxa]: { name: "Atraxa, Praetors' Voice", layout: 'normal', manaValue: 4, curveManaValue: 4, manaCost: '{G}{W}{U}{B}', typeLine: 'Legendary Creature', colorIdentity: ['W', 'U', 'B', 'G'], cardTypes: ['Creature'], isLand: false, defaultPrintingId: null, faces: [] },
      [solRing]: { name: 'Sol Ring', layout: 'normal', manaValue: 1, curveManaValue: 1, manaCost: '{1}', typeLine: 'Artifact', colorIdentity: [], cardTypes: ['Artifact'], isLand: false, defaultPrintingId: null, faces: [] },
    },
    printings: {},
    tags: { t1: { name: 'Fast mana', global: false } },
    ...overrides,
  }
}

const notLegal = { format: 'commander', legal: false, problems: [{ code: 'deck_size', oracleId: null, count: 2, limit: 100 }] }

describe('decks', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('makes a deck from the deck list and opens it', async () => {
    window.history.replaceState(null, '', '/decks')
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /decks': () => Response.json({ items: [], nextCursor: null }),
      'POST /decks': () => Response.json(deck({ lines: [], counts: { commander: 0, companion: 0, main: 0, sideboard: 0, considering: 0, tokens: 0 } }), { status: 201 }),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(notLegal),
    })
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByText('You have no deck. Make your first deck.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Make a deck' }))
    await user.type(screen.getByLabelText('Name'), 'Atraxa superfriends')
    await user.selectOptions(screen.getByLabelText('Visibility'), 'unlisted')
    await user.click(screen.getByRole('button', { name: 'Make the deck' }))

    expect(await screen.findByRole('heading', { level: 1, name: /Atraxa superfriends/ })).toBeInTheDocument()
    expect(window.location.pathname).toBe(`/decks/${deckId}`)
    expect(calls.find((call) => call.key === 'POST /decks')?.body).toEqual({ name: 'Atraxa superfriends', format: 'commander', visibility: 'unlisted' })
  })

  it('shows the zones, the legality and changes a quantity', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(notLegal),
      [`PATCH /decks/${deckId}/lines/l2`]: ({ body }) =>
        Response.json(deck({ version: 4, lines: [deck().lines[0], { ...deck().lines[1], quantity: (body as { quantity: number }).quantity }], counts: { ...deck().counts, main: 2 } })),
    })
    const user = userEvent.setup()
    render(<App />)

    const main = await screen.findByRole('region', { name: /Main deck/ })
    expect(within(main).getByRole('link', { name: 'Sol Ring' })).toBeInTheDocument()
    expect(within(main).getByText('#Fast mana')).toBeInTheDocument()
    expect(within(main).getByText('Ramp')).toBeInTheDocument()
    expect(await screen.findByText('The deck has 2 cards. It must have 100 cards.')).toBeInTheDocument()

    await user.click(within(main).getByRole('button', { name: 'Add one Sol Ring' }))

    await waitFor(() => expect(within(screen.getByRole('region', { name: /Main deck/ })).getByLabelText('Quantity of Sol Ring')).toHaveTextContent('2'))
    expect(calls.find((call) => call.key === `PATCH /decks/${deckId}/lines/l2`)?.body).toEqual({ quantity: 2 })
  })

  it('adds a card with the autocomplete', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(notLegal),
      'GET /cards/autocomplete': () => Response.json([{ oracleId: 'bolt', name: 'Lightning Bolt', matchedName: 'Foudre', lang: 'fr' }]),
      [`POST /decks/${deckId}/lines`]: () => Response.json(deck({ version: 4 })),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByRole('combobox', { name: 'Add a card' }), 'foud')
    await user.click(await screen.findByRole('option', { name: /Foudre/ }))

    await waitFor(() => expect(calls.find((call) => call.key === `POST /decks/${deckId}/lines`)?.body).toEqual({ lines: [{ oracleId: 'bolt', zone: 'main' }] }))
  })

  it('shows the line errors of the text mode', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(notLegal),
      [`GET /decks/${deckId}/text`]: () => Response.json({ text: 'Main\n1 Sol Ring\n', version: 3 }),
      [`PUT /decks/${deckId}/text`]: () =>
        Response.json({ status: 400, code: 'deck_text_invalid', detail: 'x', lineErrors: [{ line: 3, code: 'card_not_found', text: '1 No such card' }] }, { status: 400 }),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Text mode' }))
    const text = await screen.findByLabelText('Deck list')
    expect(text).toHaveValue('Main\n1 Sol Ring\n')
    await user.type(text, '1 No such card')
    await user.click(screen.getByRole('button', { name: 'Save the list' }))

    expect(await screen.findByText('Line 3: no card has this name. "1 No such card"')).toBeInTheDocument()
  })

  it('shows the deck of another user in read-only mode', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      [`GET /decks/${deckId}`]: () => Response.json(deck({ isOwner: false, visibility: 'public' })),
      [`GET /decks/${deckId}/legality`]: () => Response.json({ format: 'commander', legal: true, problems: [] }),
    })
    render(<App />)

    expect(await screen.findByText('The deck is legal in Commander.')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Add a card' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add one Sol Ring' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Copy the deck' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: /Sideboard/ })).not.toBeInTheDocument()
  })
})
