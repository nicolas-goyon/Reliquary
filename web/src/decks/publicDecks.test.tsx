import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { Deck } from '../api/decks'
import { filtersFromSearch, publicDeckQuery } from '../api/publicDecks'
import i18n from '../i18n'
import { mockApi, problem, session } from '../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d1'
const authorId = '0190a000-0000-7000-8000-0000000000a1'
const atraxa = '0190a000-0000-7000-8000-0000000000c2'

const publicDeck = {
  id: deckId,
  name: 'Atraxa superfriends',
  format: 'commander',
  colorIdentity: 'WUBG',
  commanders: [{ oracleId: atraxa, name: "Atraxa, Praetors' Voice", imageUri: null }],
  author: { id: authorId, displayName: 'Ana' },
  cardCount: 100,
  copyCount: 3,
  favoriteCount: 1,
  updatedAt: '2026-10-27T10:00:00Z',
}

function deck(): Deck {
  return {
    id: deckId,
    ownerId: authorId,
    name: 'Atraxa superfriends',
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
    isOwner: false,
    counts: { commander: 0, companion: 0, main: 0, sideboard: 0, considering: 0, tokens: 0 },
    lines: [],
    cards: {},
    printings: {},
    tags: {},
    deckTags: [],
    colorIdentity: 'WUBG',
  } as Deck
}

describe('public decks', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('lets a visitor search the public decks by commander', async () => {
    window.history.replaceState(null, '', '/explore')
    const urls: string[] = []
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /public-decks': ({ url }) => {
        urls.push(url.search)
        return Response.json({ items: url.searchParams.has('commander') ? [publicDeck] : [], nextCursor: null })
      },
    })
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByText('No deck agrees with the search.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'My favorites' })).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('Commander'), 'Atraxa')
    await user.click(screen.getByRole('button', { name: 'Search' }))

    const result = await screen.findByRole('link', { name: 'Atraxa superfriends' })
    expect(result).toHaveAttribute('href', `/decks/${deckId}`)
    expect(screen.getByRole('link', { name: 'Ana' })).toHaveAttribute('href', `/authors/${authorId}`)
    expect(screen.getByText('3 copies')).toBeInTheDocument()
    expect(urls.at(-1)).toBe('?limit=50&commander=Atraxa')
    expect(window.location.search).toBe('?commander=Atraxa')
  })

  it('follows an author from the author page', async () => {
    window.history.replaceState(null, '', `/authors/${authorId}`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /authors/${authorId}`]: () => Response.json({ id: authorId, displayName: 'Ana', deckCount: 1, followerCount: 0, following: false }),
      'GET /public-decks': () => Response.json({ items: [publicDeck], nextCursor: null }),
      [`PUT /authors/${authorId}/follow`]: () => new Response(null, { status: 204 }),
    })
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Ana' })).toBeInTheDocument()
    expect(screen.getByText('1 public deck · 0 followers')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Follow' }))

    expect(await screen.findByRole('button', { name: 'Stop following' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByText('1 public deck · 1 follower')).toBeInTheDocument()
    expect(calls.some((call) => call.key === `PUT /authors/${authorId}/follow`)).toBe(true)
  })

  it('adds a public deck of another user to the favorites', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/legality`]: () => Response.json({ format: 'commander', legal: true, problems: [] }),
      [`GET /decks/${deckId}/analysis`]: () => problem(500, 'internal_error'),
      [`GET /public-decks/${deckId}/social`]: () => Response.json({ public: true, favorite: false, favoriteCount: 1, copyCount: 3, author: { id: authorId, displayName: 'Ana' } }),
      [`PUT /public-decks/${deckId}/favorite`]: () => new Response(null, { status: 204 }),
    })
    const user = userEvent.setup()
    render(<App />)

    const add = await screen.findByRole('button', { name: 'Add to my favorites' })
    expect(within(add.parentElement!).getByRole('link', { name: 'Ana' })).toHaveAttribute('href', `/authors/${authorId}`)
    await user.click(add)

    await waitFor(() => expect(screen.getByRole('button', { name: 'Remove from my favorites' })).toHaveAttribute('aria-pressed', 'true'))
    expect(within(add.parentElement!).getByText('2 favorites')).toBeInTheDocument()
    expect(calls.some((call) => call.key === `PUT /public-decks/${deckId}/favorite`)).toBe(true)
  })

  it('writes and reads the filters of the address', () => {
    const filters = filtersFromSearch('?q=goblins&colors=r&sort=popular&unknown=1')
    expect(filters).toEqual({ q: 'goblins', format: '', commander: '', card: '', colors: 'r', sort: 'popular' })
    expect(publicDeckQuery(filters, authorId)).toBe(`limit=50&q=goblins&colors=r&sort=popular&author=${authorId}`)
  })
})
