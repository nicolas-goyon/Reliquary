import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { HiddenItem, ModerationLogEntry, ModerationReport } from '../api/moderation'
import i18n from '../i18n'
import { mockApi, session, testAccount, type Handler } from '../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d7'
const administrator = { ...testAccount, isAdministrator: true }

const report: ModerationReport = {
  id: 'r1',
  itemType: 'deck',
  itemId: deckId,
  itemName: 'Spam deck',
  owner: { id: 'o1', displayName: 'Bob' },
  reporter: { id: 'u2', displayName: 'Carla' },
  reason: 'spam',
  comment: 'A link to a shop.',
  status: 'open',
  itemHidden: false,
  createdAt: '2026-10-28T10:00:00Z',
  closedAt: null,
}

const hidden: HiddenItem = { itemType: 'deck', itemId: deckId, itemName: 'Spam deck', owner: { id: 'o1', displayName: 'Bob' }, reason: 'Spam', hiddenAt: '2026-10-28T11:00:00Z' }

const logEntry: ModerationLogEntry = {
  id: 'l1',
  action: 'hide',
  itemType: 'author',
  itemId: null,
  itemName: '',
  moderator: null,
  reportId: null,
  reason: 'Spam account',
  createdAt: '2026-10-28T11:00:00Z',
}

function page<T>(items: T[]): Response {
  return Response.json({ items, nextCursor: null })
}

describe('moderation page', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', '/admin/moderation')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the open reports and hides a reported deck with a reason', async () => {
    let hiddenNow = false
    const handlers: Record<string, Handler> = {
      'POST /auth/refresh': () => session('token-1', administrator),
      'GET /moderation/reports': ({ url }) => page(url.searchParams.get('status') === 'open' ? [{ ...report, itemHidden: hiddenNow }] : []),
      [`PUT /moderation/hidden/deck/${deckId}`]: () => {
        hiddenNow = true
        return new Response(null, { status: 204 })
      },
    }
    const calls = mockApi(handlers)
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByRole('link', { name: 'Moderation' })).toHaveAttribute('href', '/admin/moderation')
    const row = (await screen.findByRole('link', { name: 'Spam deck' })).closest('tr')!
    expect(within(row).getByText('Spam or advertisement')).toBeInTheDocument()
    expect(within(row).getByText('A link to a shop.')).toBeInTheDocument()
    expect(within(row).getByText('Reported by Carla')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Reason for the moderation log'), 'Spam')
    await user.click(within(row).getByRole('button', { name: 'Hide' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Show again' })).toBeInTheDocument())
    expect(screen.getByText('Hidden')).toBeInTheDocument()
    expect(calls.find((call) => call.key === `PUT /moderation/hidden/deck/${deckId}`)?.body).toEqual({ reason: 'Spam' })
    expect(screen.getByLabelText('Reason for the moderation log')).toHaveValue('')
  })

  it('shows the hidden content and the moderation log, and shows a deck again', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session('token-1', administrator),
      'GET /moderation/reports': () => page([]),
      'GET /moderation/hidden': () => page([hidden]),
      'GET /moderation/log': () => page([logEntry]),
      [`DELETE /moderation/hidden/deck/${deckId}`]: () => new Response(null, { status: 204 }),
    })
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByText('No report has this status.')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Hidden content' }))
    await user.click(await screen.findByRole('button', { name: 'Show again' }))
    await waitFor(() => expect(calls.filter((call) => call.key === `DELETE /moderation/hidden/deck/${deckId}`)).toHaveLength(1))

    await user.click(screen.getByRole('tab', { name: 'Moderation log' }))
    const row = (await screen.findByText('Spam account')).closest('tr')!
    expect(within(row).getByText('Hid')).toBeInTheDocument()
    expect(within(row).getAllByText('Deleted account')).toHaveLength(2)
  })

  it('refuses a user who is not an administrator', async () => {
    mockApi({ 'POST /auth/refresh': () => session() })
    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Only the administrators can open this page.')
    expect(screen.queryByRole('link', { name: 'Moderation' })).not.toBeInTheDocument()
  })
})
