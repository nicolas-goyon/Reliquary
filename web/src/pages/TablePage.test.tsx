import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { App } from '../App'
import { mockApi, problem } from '../test/mockApi'

const id = '01a1a7ea-0cbe-75d5-8691-85c2e6d59164'

const deck = {
  id,
  name: 'Test deck',
  format: 'commander',
  isOwner: true,
  counts: { commander: 1, main: 20, companion: 0, sideboard: 2 },
  lines: [
    { id: 'l1', oracleId: 'o1', printingId: null, quantity: 1, zone: 'commander', tagIds: [], categories: [] },
    { id: 'l2', oracleId: 'o2', printingId: null, quantity: 20, zone: 'main', tagIds: [], categories: [] },
    { id: 'l3', oracleId: 'o3', printingId: null, quantity: 2, zone: 'sideboard', tagIds: [], categories: [] },
  ],
  cards: {
    o1: { name: 'Leader', isLand: false, manaValue: 3 },
    o2: { name: 'Plains', isLand: true, manaValue: 0 },
    o3: { name: 'Spare', isLand: false, manaValue: 1 },
  },
  printings: {},
  tags: {},
}

describe('goldfish mode', () => {
  beforeEach(() => window.history.replaceState(null, '', `/decks/${id}/test`))
  afterEach(() => window.history.replaceState(null, '', '/'))

  it('shows an opening hand, plays a card, and does the London mulligan', async () => {
    mockApi({ 'POST /auth/refresh': () => problem(401, 'session_expired'), [`GET /decks/${id}`]: () => Response.json(deck) })
    const user = userEvent.setup()
    render(<App />)

    const hand = await screen.findByRole('region', { name: 'Hand' })
    expect(within(hand).getAllByRole('button', { name: 'Play Plains' })).toHaveLength(7)
    expect(screen.getByLabelText('Life points')).toHaveTextContent('40')
    expect(within(screen.getByRole('region', { name: 'Command zone' })).getByRole('button', { name: 'Play Leader' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Draw (13)' })).toBeInTheDocument()

    await user.click(within(hand).getAllByRole('button', { name: 'Play Plains' })[0])
    const battlefield = screen.getByRole('region', { name: 'Battlefield' })
    await user.click(within(battlefield).getByRole('button', { name: 'Tap or untap Plains' }))
    expect(within(battlefield).getByRole('button', { name: 'Tap or untap Plains' })).toHaveAttribute('aria-pressed', 'true')

    await user.click(screen.getByRole('button', { name: 'Mulligan' }))
    expect(screen.getByRole('status')).toHaveTextContent('Select 1 card of your hand.')
    await user.click(within(hand).getAllByRole('button', { name: 'Put Plains at the bottom of the library' })[0])
    expect(within(hand).getAllByRole('button', { name: 'Play Plains' })).toHaveLength(6)
  })

  it('opens a table window that masks the hand and the library', async () => {
    mockApi({ 'POST /auth/refresh': () => problem(401, 'session_expired'), [`GET /decks/${id}`]: () => Response.json(deck) })
    const user = userEvent.setup()
    render(<App />)

    const hand = await screen.findByRole('region', { name: 'Hand' })
    await user.click(within(hand).getAllByRole('button', { name: 'Play Plains' })[0])
    const path = screen.getByRole('link', { name: 'Open the table window' }).getAttribute('href') ?? ''
    expect(screen.queryByRole('link', { name: /Open the player window/ })).not.toBeInTheDocument()

    window.history.replaceState(null, '', path)
    const second = document.createElement('div')
    document.body.append(second)
    render(<App />, { container: second })
    const view = within(second)
    const board = await view.findByRole('region', { name: 'Test deck' })
    expect(board).toHaveTextContent('Hand6Library13')
    expect(view.getAllByRole('img').map((image) => image.getAttribute('aria-label') ?? image.getAttribute('alt'))).toEqual(['Plains', 'Leader'])

    await user.click(screen.getByRole('button', { name: 'Draw (13)' }))
    await waitFor(() => expect(board).toHaveTextContent('Hand7Library12'))
    expect(view.getAllByRole('img')).toHaveLength(2)
  })
})
