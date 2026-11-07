import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../../App'
import type { Deck } from '../../api/decks'
import type { Author } from '../../api/publicDecks'
import i18n from '../../i18n'
import { mockApi, problem, session, testAccount, type Handler } from '../../test/mockApi'

const authorId = '0190a000-0000-7000-8000-0000000000a9'

function author(overrides: Partial<Author> = {}): Author {
  return { id: authorId, displayName: 'Bob', deckCount: 1, followerCount: 0, following: false, ...overrides }
}

function handlers(overrides: Record<string, Handler> = {}): Record<string, Handler> {
  return {
    'POST /auth/refresh': () => session(),
    [`GET /authors/${authorId}`]: () => Response.json(author()),
    'GET /public-decks': () => Response.json({ items: [], nextCursor: null }),
    ...overrides,
  }
}

describe('report dialog', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', `/authors/${authorId}`)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('sends the reason and the comment of a report', async () => {
    const calls = mockApi(handlers({ 'POST /reports': () => Response.json({ id: 'r1', status: 'open' }, { status: 201 }) }))
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Report' }))
    const dialog = screen.getByRole('dialog', { name: 'Report this profile' })
    const submit = within(dialog).getByRole('button', { name: 'Send the report' })
    expect(submit).toBeDisabled()
    await user.click(within(dialog).getByRole('radio', { name: 'Spam or advertisement' }))
    await user.type(within(dialog).getByLabelText('Comment'), '  Links to a shop.  ')
    await user.click(submit)

    expect(await screen.findByText('The administrators received your report.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Report' })).toBeDisabled()
    expect(calls.find((call) => call.key === 'POST /reports')?.body).toEqual({ itemType: 'author', itemId: authorId, reason: 'spam', comment: 'Links to a shop.' })
  })

  it('shows the error of a second report and of the rate limit', async () => {
    let status = 409
    mockApi(handlers({ 'POST /reports': () => problem(status, status === 409 ? 'report_exists' : 'rate_limited') }))
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Report' }))
    await user.click(screen.getByRole('radio', { name: 'Other problem' }))
    await user.click(screen.getByRole('button', { name: 'Send the report' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('You sent a report about this item before.')

    status = 429
    await user.click(screen.getByRole('button', { name: 'Send the report' }))
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('You sent too many reports today. Try again tomorrow.'))
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('does not show the button to the author, and tells the author that the profile is hidden', async () => {
    mockApi(handlers({ [`GET /authors/${testAccount.id}`]: () => Response.json(author({ id: testAccount.id, displayName: 'Ana', hidden: true })) }))
    window.history.replaceState(null, '', `/authors/${testAccount.id}`)
    render(<App />)

    expect(await screen.findByText(/An administrator hid your profile/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Report' })).not.toBeInTheDocument()
  })

  it('does not show the button to a visitor', async () => {
    mockApi(handlers({ 'POST /auth/refresh': () => problem(401, 'session_expired') }))
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Bob' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Report' })).not.toBeInTheDocument()
  })
})

describe('deck page moderation', () => {
  const deckId = '0190a000-0000-7000-8000-0000000000d8'

  function deck(overrides: Partial<Deck>): Deck {
    return {
      id: deckId,
      ownerId: authorId,
      name: 'Shop deck',
      format: 'commander',
      visibility: 'public',
      folderId: null,
      description: '',
      sourceDeckId: null,
      pinned: false,
      archived: false,
      version: 1,
      createdAt: '2026-10-27T10:00:00Z',
      updatedAt: '2026-10-27T10:00:00Z',
      isOwner: false,
      counts: { commander: 0, companion: 0, main: 0, sideboard: 0, considering: 0, tokens: 0 },
      lines: [],
      cards: {},
      printings: {},
      tags: {},
      ...overrides,
    }
  }

  function deckHandlers(value: Deck): Record<string, Handler> {
    return {
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(value),
      [`GET /decks/${deckId}/legality`]: () => Response.json({ format: 'commander', legal: true, problems: [] }),
      [`GET /decks/${deckId}/analysis`]: () => problem(500, 'internal_error'),
      [`GET /decks/${deckId}/recommendations`]: () => problem(404, 'recommendations_disabled'),
      [`GET /public-decks/${deckId}/social`]: () => Response.json({ public: !value.hidden, favorite: false, favoriteCount: 0, copyCount: 0, author: { id: authorId, displayName: 'Bob' } }),
    }
  }

  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', `/decks/${deckId}`)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the report button on the public deck of another user', async () => {
    mockApi(deckHandlers(deck({})))
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Report' }))
    expect(screen.getByRole('dialog', { name: 'Report this deck' })).toBeInTheDocument()
  })

  it('tells the owner that the deck is hidden, with no report button', async () => {
    mockApi(deckHandlers(deck({ isOwner: true, ownerId: testAccount.id, hidden: true })))
    render(<App />)

    expect(await screen.findByText(/An administrator hid this deck/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Report' })).not.toBeInTheDocument()
  })
})
