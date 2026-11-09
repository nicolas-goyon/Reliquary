import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { App } from '../App'
import { seriousAccessibilityProblems } from '../test/axe'
import { deckId, testDeck, testDeckSummary } from '../test/fixtures'
import { mockApi, problem, session } from '../test/mockApi'

const otherId = '0190a000-0000-7000-8000-0000000000d2'
const lands = '0190a000-0000-7000-8000-0000000000c3'

function deckWithLands(id: string, name: string) {
  const deck = testDeck({ id, name })
  return {
    ...deck,
    lines: [...deck.lines, { id: 'l3', oracleId: lands, printingId: null, finish: null, quantity: 20, zone: 'main', note: '', tagIds: [], categories: [] }],
    cards: { ...deck.cards, [lands]: { ...deck.cards[Object.keys(deck.cards)[0]], name: 'Forest', isLand: true } },
  }
}

const handlers = {
  'POST /auth/refresh': () => session(),
  [`GET /decks/${deckId}`]: () => Response.json(deckWithLands(deckId, 'Atraxa superfriends')),
  [`GET /decks/${otherId}`]: () => Response.json(deckWithLands(otherId, 'Goblins')),
  'GET /decks': () => Response.json({ items: [testDeckSummary, { ...testDeckSummary, id: otherId, name: 'Goblins' }], nextCursor: null }),
}

describe('local multiplayer', () => {
  beforeEach(() => window.history.replaceState(null, '', `/decks/${deckId}/test/local`))
  afterEach(() => window.history.replaceState(null, '', '/'))

  it('plays 3 decks in one window, and hides the hand until the player shows it', async () => {
    mockApi(handlers)
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('radio', { name: '3 players' }))
    expect(screen.getByRole('combobox', { name: 'Deck of player 2' })).toHaveValue(otherId)
    expect(screen.queryByRole('combobox', { name: 'Deck of player 4' })).not.toBeInTheDocument()
    expect(await seriousAccessibilityProblems()).toEqual([])
    await user.click(screen.getByRole('button', { name: 'Start the game' }))

    const players = await screen.findByRole('list', { name: 'Players' })
    expect(within(players).getAllByRole('listitem')).toHaveLength(3)
    expect(screen.getByRole('heading', { name: 'Board of Atraxa superfriends' })).toBeInTheDocument()
    let hand = screen.getByRole('region', { name: 'Hand' })
    expect(within(hand).queryByRole('button', { name: /^Play/ })).not.toBeInTheDocument()
    expect(hand).toHaveTextContent('Hand (7)')
    expect(await seriousAccessibilityProblems()).toEqual([])

    await user.click(within(hand).getByRole('button', { name: 'Show the hand of Atraxa superfriends' }))
    hand = screen.getByRole('region', { name: 'Hand' })
    expect(within(hand).getAllByRole('button', { name: /^Play/ })).toHaveLength(7)

    await user.click(screen.getByRole('button', { name: 'Add one: Poison' }))
    expect(within(screen.getByRole('group', { name: 'Poison' })).getByRole('status')).toHaveTextContent('1')
    await user.click(screen.getByRole('button', { name: 'Add one: Commander damage from Goblins' }))
    expect(within(screen.getByRole('group', { name: 'Commander damage from Goblins' })).getByRole('status')).toHaveTextContent('1')

    await user.click(screen.getByRole('button', { name: 'Next player' }))
    expect(screen.getByRole('heading', { name: 'Board of Goblins' })).toBeInTheDocument()
    hand = screen.getByRole('region', { name: 'Hand' })
    expect(hand).toHaveTextContent('Hand (8)')
    expect(within(hand).queryByRole('button', { name: /^Play/ })).not.toBeInTheDocument()

    await user.click(within(players).getByRole('button', { name: /^Atraxa superfriends 40/ }))
    expect(screen.getByRole('heading', { name: 'Board of Atraxa superfriends' })).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Hand' })).queryByRole('button', { name: /^Play/ })).not.toBeInTheDocument()
  })

  it('opens a player window that shows the hand of its player and sends the actions to the game window', async () => {
    mockApi(handlers)
    const user = userEvent.setup()
    const game = render(<App />)

    await user.click(await screen.findByRole('radio', { name: /One window for each player/ }))
    await user.click(screen.getByRole('button', { name: 'Start the game' }))
    const link = await screen.findByRole('link', { name: 'Open the player window of Goblins' })
    expect(link).toHaveAttribute('target', '_blank')
    const path = link.getAttribute('href') ?? ''
    expect(path).toMatch(/^\/test\/games\/[0-9a-f]{16}\/players\/player-2$/)

    // The test renders the player window in a second container. The two "windows" use the same BroadcastChannel names.
    window.history.replaceState(null, '', path)
    const second = document.createElement('div')
    document.body.append(second)
    const playerWindow = render(<App />, { container: second })
    const view = within(second)
    const hand = await view.findByRole('region', { name: 'Hand' })
    expect(view.getByRole('heading', { level: 1, name: 'Player window: Goblins' })).toBeInTheDocument()
    expect(within(hand).getAllByRole('button', { name: /^Play/ })).toHaveLength(7)

    await user.click(view.getByRole('button', { name: 'Draw (14)' }))
    await waitFor(() => expect(within(game.container).getByRole('list', { name: 'Players' })).toHaveTextContent('40 life points, 8 cards in hand, 13 cards in library'))
    expect(within(hand).getAllByRole('button', { name: /^Play/ })).toHaveLength(8)
    expect(await seriousAccessibilityProblems(second)).toEqual([])

    game.unmount()
    expect(await view.findByRole('alert')).toHaveTextContent('The game window closed.')
    playerWindow.unmount()
  })

  it('hides the commander damage in a game with no commander', async () => {
    const modern = (id: string, name: string) => {
      const deck = deckWithLands(id, name)
      return { ...deck, format: 'modern', lines: deck.lines.filter((line) => line.zone !== 'commander') }
    }
    mockApi({ ...handlers, [`GET /decks/${deckId}`]: () => Response.json(modern(deckId, 'Burn')), [`GET /decks/${otherId}`]: () => Response.json(modern(otherId, 'Goblins')) })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Start the game' }))
    const counters = await screen.findByRole('region', { name: 'Player counters' })
    expect(within(counters).getByRole('group', { name: 'Poison' })).toBeInTheDocument()
    expect(within(counters).queryByRole('group', { name: /Commander damage/ })).not.toBeInTheDocument()
  })

  it('opens a table window that shows the boards and masks the hands of all the players', async () => {
    mockApi(handlers)
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('radio', { name: '3 players' }))
    await user.click(screen.getByRole('button', { name: 'Start the game' }))
    await user.click(within(await screen.findByRole('region', { name: 'Hand' })).getByRole('button', { name: 'Show the hand of Atraxa superfriends' }))
    await user.click(within(screen.getByRole('region', { name: 'Hand' })).getAllByRole('button', { name: 'Play Forest' })[0])
    await user.click(screen.getByRole('button', { name: 'Add one: Poison' }))
    const path = screen.getByRole('link', { name: 'Open the table window' }).getAttribute('href') ?? ''
    expect(path).toMatch(/^\/test\/games\/[0-9a-f]{16}\/table$/)

    window.history.replaceState(null, '', path)
    const second = document.createElement('div')
    document.body.append(second)
    render(<App />, { container: second })
    const view = within(second)
    const board = await view.findByRole('region', { name: 'Atraxa superfriends' })
    expect(view.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual(['Atraxa superfriends Active', 'Goblins', 'Atraxa superfriends (2)'])
    expect(within(board).getByRole('region', { name: 'Battlefield of Atraxa superfriends' })).toContainElement(within(board).getByRole('img', { name: 'Forest' }))
    expect(within(board).getByText('Poison: 1')).toBeInTheDocument()
    expect(board).toHaveTextContent('Hand6Library14')
    // The table window shows no card of a hand or a library: only the commanders and the land on the battlefield have a name.
    expect(view.getAllByRole('img').map((image) => image.getAttribute('aria-label') ?? image.getAttribute('alt'))).toEqual([
      'Forest',
      "Atraxa, Praetors' Voice",
      "Atraxa, Praetors' Voice",
      "Atraxa, Praetors' Voice",
    ])
    // The table window has no game controls.
    expect(within(view.getByRole('main')).queryByRole('button')).not.toBeInTheDocument()
    expect(await seriousAccessibilityProblems(second)).toEqual([])
  })

  it('shows a message when the game window is not open', async () => {
    window.history.replaceState(null, '', '/test/games/0123456789abcdef/players/player-1')
    mockApi({ 'POST /auth/refresh': () => problem(401, 'session_expired') })
    render(<App />)
    expect(await screen.findByRole('status')).toHaveTextContent('The window waits for the game window.')
  })
})
