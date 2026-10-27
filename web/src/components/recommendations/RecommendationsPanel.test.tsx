import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../../App'
import type { Deck, DeckLine } from '../../api/decks'
import type { DeckRecommendations } from '../../api/recommendations'
import i18n from '../../i18n'
import { mockApi, problem, session, testAccount } from '../../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d1'
const atraxa = '0190a000-0000-7000-8000-0000000000c2'
const solRing = '0190a000-0000-7000-8000-0000000000c1'
const cultivate = '0190a000-0000-7000-8000-0000000000c3'
const swords = '0190a000-0000-7000-8000-0000000000c4'

const commanderLine: DeckLine = { id: 'l1', oracleId: atraxa, printingId: null, finish: null, quantity: 1, zone: 'commander', note: '', tagIds: [], categories: [] }
const solRingLine: DeckLine = { id: 'l2', oracleId: solRing, printingId: null, finish: null, quantity: 1, zone: 'main', note: '', tagIds: [], categories: [] }

function deck(lines: DeckLine[] = [commanderLine, solRingLine], overrides: Partial<Deck> = {}): Deck {
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
    counts: { commander: 1, companion: 0, main: lines.length - 1, sideboard: 0, considering: 0, tokens: 0 },
    lines,
    cards: {},
    printings: {},
    tags: {},
    ...overrides,
  }
}

const recommendations: DeckRecommendations = {
  status: 'ok',
  source: { name: 'Recommander', url: 'https://recommander.cards/' },
  commanders: [atraxa],
  cards: [
    { oracleId: cultivate, name: 'Cultivate', score: 0.94, manaCost: '{2}{G}', typeLine: 'Sorcery', imageUri: null },
    { oracleId: swords, name: 'Swords to Plowshares', score: 0.92, manaCost: '{W}', typeLine: 'Instant', imageUri: null },
  ],
}

const legal = { format: 'commander', legal: true, problems: [] }

function open() {
  window.history.replaceState(null, '', `/decks/${deckId}`)
  render(<App />)
}

describe('recommendations panel', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the recommendations with the credit of the source', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`GET /decks/${deckId}/recommendations`]: () => Response.json(recommendations),
    })
    open()

    const panel = await screen.findByRole('region', { name: 'Recommendations' })
    expect(await within(panel).findByRole('link', { name: 'Cultivate' })).toHaveAttribute('href', `/cards/${cultivate}`)
    expect(within(panel).getByText('Sorcery')).toBeInTheDocument()
    expect(within(panel).getByText('Recommendations by')).toBeInTheDocument()
    expect(within(panel).getByRole('link', { name: 'Recommander' })).toHaveAttribute('href', 'https://recommander.cards/')
  })

  it('adds a card to the main deck or to the considering zone in one action, then hides it', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`GET /decks/${deckId}/recommendations`]: () => Response.json(recommendations),
      [`POST /decks/${deckId}/lines`]: ({ body }) => {
        const [line] = (body as { lines: { oracleId: string; zone: DeckLine['zone'] }[] }).lines
        const added: DeckLine = { ...solRingLine, id: `new-${line.oracleId}`, oracleId: line.oracleId, zone: line.zone }
        const previous = calls.filter((call) => call.key === `POST /decks/${deckId}/lines`).length
        const lines = previous > 1 ? [commanderLine, solRingLine, { ...solRingLine, id: 'first', oracleId: cultivate }, added] : [commanderLine, solRingLine, added]
        return Response.json(deck(lines, { version: 3 + previous }))
      },
    })
    const user = userEvent.setup()
    open()

    const panel = await screen.findByRole('region', { name: 'Recommendations' })
    await user.click(await within(panel).findByRole('button', { name: 'Add Cultivate to the main deck' }))

    await waitFor(() => expect(within(panel).queryByRole('link', { name: 'Cultivate' })).not.toBeInTheDocument())
    expect(calls.find((call) => call.key === `POST /decks/${deckId}/lines`)?.body).toEqual({ lines: [{ oracleId: cultivate, zone: 'main' }] })

    await user.click(within(panel).getByRole('button', { name: 'Add Swords to Plowshares to the considering zone' }))

    await waitFor(() => expect(within(panel).queryByRole('link', { name: 'Swords to Plowshares' })).not.toBeInTheDocument())
    expect(calls.filter((call) => call.key === `POST /decks/${deckId}/lines`).map((call) => call.body)[1]).toEqual({ lines: [{ oracleId: swords, zone: 'considering' }] })
    expect(within(panel).getByText('The source has no other card to recommend for these commanders.')).toBeInTheDocument()
    // A new card in the deck does not ask for the recommendations again.
    expect(calls.filter((call) => call.key === `GET /decks/${deckId}/recommendations`)).toHaveLength(1)
  })

  it('is not visible when the administrator did not enable the recommendations', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`GET /decks/${deckId}/recommendations`]: () => problem(404, 'recommendations_disabled'),
    })
    open()

    expect(await screen.findByRole('region', { name: /Main deck/ })).toBeInTheDocument()
    await waitFor(() => expect(calls.some((call) => call.key === `GET /decks/${deckId}/recommendations`)).toBe(true))
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Recommendations' })).not.toBeInTheDocument())
  })

  it('tells the user to add a commander, and tells when the source is not available', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck([solRingLine])),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`GET /decks/${deckId}/recommendations`]: () => Response.json({ ...recommendations, status: 'no_commander', commanders: [], cards: [] }),
    })
    open()

    expect(await screen.findByText('The deck has no commander. Put a card in the commander zone to get recommendations.')).toBeInTheDocument()
    vi.unstubAllGlobals()
    cleanup()

    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`GET /decks/${deckId}/recommendations`]: () => Response.json({ ...recommendations, status: 'unavailable', cards: [] }),
    })
    open()

    expect(await screen.findByText('The recommendations are not available now. Try again later.')).toBeInTheDocument()
  })

  it('is not visible for a user who is not the owner', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck(undefined, { isOwner: false, visibility: 'public' })),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
    })
    open()

    expect(await screen.findByRole('region', { name: /Main deck/ })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Recommendations' })).not.toBeInTheDocument()
    expect(calls.some((call) => call.key === `GET /decks/${deckId}/recommendations`)).toBe(false)
  })

  it('shows the texts in French', async () => {
    await i18n.changeLanguage('fr')
    mockApi({
      'POST /auth/refresh': () => session('token-1', { ...testAccount, language: 'fr' }),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json(legal),
      [`GET /decks/${deckId}/recommendations`]: () => Response.json(recommendations),
    })
    open()

    const panel = await screen.findByRole('region', { name: 'Recommandations' })
    expect(await within(panel).findByRole('button', { name: 'Ajouter Cultivate au deck principal' })).toBeInTheDocument()
    expect(within(panel).getByText('Recommandations de')).toBeInTheDocument()
  })
})
