import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { App } from '../App'
import { applyAction, createTable, type PlayerSetup } from '../playtest/engine'
import { hubSocket, storeSeat } from '../playtest/online/gameApi'
import { diffViews, playerView, viewHash } from '../playtest/views'
import { seriousAccessibilityProblems } from '../test/axe'
import { FakeSocket } from '../test/fakeSocket'
import { deckId, testDeck } from '../test/fixtures'
import { mockApi, problem, session } from '../test/mockApi'

const gameId = 'g7Kx2'
const anonymous = { 'POST /auth/refresh': () => problem(401, 'session_expired') }

let sockets: FakeSocket[] = []

function setup(id: string, name: string): PlayerSetup {
  return {
    id,
    name,
    library: Array.from({ length: 20 }, (_, index) => ({ id: `${id}-${index}`, oracleId: `o-${id}-${index}`, name: `${name} card ${index}`, imageUri: null, isLand: index % 2 === 0, manaValue: 1 })),
    command: [],
  }
}

const lobby = {
  gameId,
  seats: [
    { seat: '0', nickname: 'Ana', ready: true, guest: false, cardCount: 100 },
    { seat: '1', nickname: 'Bob', ready: true, guest: true, cardCount: 60 },
  ],
  hostSeat: '0',
  size: 4,
  started: false,
}

/** Accepts the connection of the page and answers Join. */
async function connect(view: unknown, seq = 3) {
  const socket = await waitFor(() => {
    const last = sockets.at(-1)
    if (!last) {
      throw new Error('The page did not open a socket.')
    }
    return last
  })
  act(() => socket.accept())
  act(() => socket.complete('Join', { seq, view, hash: view ? viewHash(view as never) : '' }))
  return socket
}

describe('online games', () => {
  beforeEach(() => {
    sockets = []
    hubSocket.create = (url) => {
      const socket = new FakeSocket(url)
      sockets.push(socket)
      return socket
    }
  })

  afterEach(() => {
    sessionStorage.clear()
    hubSocket.create = undefined
    window.history.replaceState(null, '', '/')
  })

  it('creates a game with a deck and shows the invite link in the lobby', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}/test/online`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json(testDeck()),
      'POST /playtest/games': () => Response.json({ gameId, seat: '0', gameToken: 'game-token', inviteSecret: 'secret', tokenExpiresAt: '2026-11-10T03:00:00Z', lobby }),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('radio', { name: '2 players' }))
    const name = screen.getByRole('textbox', { name: 'Name in the game (optional)' })
    expect(name).toHaveValue('Ana')
    await user.clear(name)
    await user.type(name, 'Ana the host')
    expect(await seriousAccessibilityProblems()).toEqual([])
    await user.click(screen.getByRole('button', { name: 'Create the game' }))
    expect(calls.find((call) => call.key === 'POST /playtest/games')?.body).toEqual({ deckId, seats: 2, nickname: 'Ana the host' })

    const socket = await connect(null, 0)
    expect(socket.url).toBe(`ws://${window.location.host}/api/v1/playtest/hub?access_token=game-token`)
    act(() => socket.invoke('Lobby', { ...lobby, seats: [lobby.seats[0]] }))
    expect(screen.getByRole('textbox', { name: 'Invite link' })).toHaveValue(`${window.location.origin}/play/${gameId}#secret`)
    expect(screen.getByRole('button', { name: 'Start the game' })).toBeDisabled()
    expect(await seriousAccessibilityProblems()).toEqual([])

    // Each change of the lobby comes with an empty patch. The page does not ask for a full view.
    act(() => socket.invoke('Lobby', lobby))
    act(() => socket.patch(1, [], '', ''))
    expect(socket.invocations('Resync')).toHaveLength(0)
    expect(screen.getByRole('list', { name: 'Players' })).toHaveTextContent('Bob Guest, 60 cards')
    await user.click(screen.getByRole('button', { name: 'Remove Bob' }))
    expect(socket.invocations('Kick')[0].arguments).toEqual(['1'])

    // The hub sends its error code in the text of the error. The page shows the translated message.
    await user.click(screen.getByRole('button', { name: 'Start the game' }))
    const start = socket.invocations('Start')[0]
    act(() =>
      socket.frame(
        JSON.stringify({ type: 3, invocationId: start.invocationId, error: "An unexpected error occurred invoking 'Start' on the server. HubException: game_not_enough_players: Not enough players." }),
      ),
    )
    expect(await screen.findByRole('alert')).toHaveTextContent('The game needs 2 players or more.')

    // The host ends the game after a confirmation.
    await user.click(screen.getByRole('button', { name: 'End the game' }))
    expect(screen.getByRole('group', { name: 'End the game' })).toHaveTextContent('The game ends for all the players.')
    await user.click(screen.getByRole('button', { name: 'End the game for all' }))
    expect(socket.invocations('Close')).toHaveLength(1)
    expect(socket.invocations('Leave')).toHaveLength(0)
  })

  it('sends a user who is not signed in to the sign-in page to create a game', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}/test/online`)
    mockApi(anonymous)
    render(<App />)
    await waitFor(() => expect(window.location.pathname).toBe('/sign-in'))
  })

  it('joins as a guest with a nickname and a deck list, then waits for the host', async () => {
    window.history.replaceState(null, '', `/play/${gameId}#invite-1`)
    const calls = mockApi({ ...anonymous, [`POST /playtest/games/${gameId}/join`]: () => Response.json({ gameId, seat: '1', gameToken: 'guest-token', inviteSecret: null, lobby }) })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Join the game' }))
    expect(screen.getByRole('alert')).toHaveTextContent('The deck link is not correct.')
    await user.click(screen.getByRole('radio', { name: 'A deck list' }))
    await user.type(screen.getByRole('textbox', { name: 'Deck list' }), '1 Sol Ring')
    await user.click(screen.getByRole('button', { name: 'Join the game' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Enter a nickname.')
    await user.type(screen.getByRole('textbox', { name: 'Nickname' }), 'Bob')
    expect(await seriousAccessibilityProblems()).toEqual([])
    await user.click(screen.getByRole('button', { name: 'Join the game' }))
    expect(calls.find((call) => call.key === `POST /playtest/games/${gameId}/join`)?.body).toEqual({ inviteSecret: 'invite-1', deckText: '1 Sol Ring', nickname: 'Bob' })

    const socket = await connect(null, 0)
    expect(socket.url).toContain('access_token=guest-token')
    act(() => socket.invoke('Lobby', lobby))
    expect(screen.queryByRole('button', { name: 'Start the game' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'End the game' })).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'Invite link' })).not.toBeInTheDocument()
    expect(screen.getByText('The host starts the game.')).toBeInTheDocument()

    // A player who leaves the lobby frees the seat: the page shows the join form again.
    await user.click(screen.getByRole('button', { name: 'Leave the game' }))
    act(() => socket.complete('Leave', null))
    expect(await screen.findByRole('heading', { name: 'Join the game' })).toBeInTheDocument()
    expect(sessionStorage.getItem(`reliquary.onlineGame.${gameId}`)).toBeNull()
  })

  it('joins with an account and a deck link, with the display name as the default name', async () => {
    window.history.replaceState(null, '', `/play/${gameId}#invite-1`)
    const calls = mockApi({ 'POST /auth/refresh': () => session(), [`POST /playtest/games/${gameId}/join`]: () => Response.json({ gameId, seat: '2', gameToken: 'account-token', inviteSecret: null, lobby }) })
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByText('Select the deck that you play.')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Name in the game (optional)' })).toHaveValue('Ana')
    await user.type(screen.getByRole('textbox', { name: 'Deck link' }), `https://example.org/decks/${deckId}`)
    await user.click(screen.getByRole('button', { name: 'Join the game' }))
    expect(calls.find((call) => call.key === `POST /playtest/games/${gameId}/join`)?.body).toEqual({ inviteSecret: 'invite-1', deckId, nickname: 'Ana' })
    expect(calls.find((call) => call.key === `POST /playtest/games/${gameId}/join`)?.headers.Authorization).toBe('Bearer token-1')
  })

  it('shows the lines of a deck list that the server cannot read, and the errors of the game', async () => {
    window.history.replaceState(null, '', `/play/${gameId}#invite-1`)
    let answer = Response.json({ status: 400, code: 'deck_text_invalid', detail: 'x', lineErrors: [{ line: 2, code: 'card_not_found', text: '1 No such card' }] }, { status: 400 })
    mockApi({ ...anonymous, [`POST /playtest/games/${gameId}/join`]: () => answer })
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByRole('textbox', { name: 'Nickname' }), 'Bob')
    await user.click(screen.getByRole('radio', { name: 'A deck list' }))
    await user.type(screen.getByRole('textbox', { name: 'Deck list' }), '1 Sol Ring{enter}1 No such card')
    await user.click(screen.getByRole('button', { name: 'Join the game' }))
    expect(await screen.findByText(/Line 2/)).toHaveTextContent('1 No such card')
    expect(await seriousAccessibilityProblems()).toEqual([])

    answer = problem(404, 'game_invite_invalid')
    await user.click(screen.getByRole('button', { name: 'Join the game' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The invite link is not valid.')
  })

  it('asks for the invite link when the page has no seat and no invitation', async () => {
    window.history.replaceState(null, '', `/play/${gameId}`)
    mockApi(anonymous)
    render(<App />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Ask the host for the invite link.')
  })

  it('shows the table: the own hand, the hidden zones of the others as counts, and the patches of the hub', async () => {
    window.history.replaceState(null, '', `/play/${gameId}`)
    storeSeat({ gameId, seat: '0', gameToken: 'game-token' })
    mockApi(anonymous)
    const user = userEvent.setup()
    render(<App />)

    // In a game with the commander damage, Ana dealt 3 commander damage to Bob. The board of Bob shows it.
    const start = applyAction(createTable([setup('0', 'Ana'), setup('1', 'Bob')], 5, 40, { commander: true }), { type: 'commanderDamage', playerId: '1', fromPlayerId: '0', change: 3 })
    const socket = await connect(playerView(start, '0'))
    const hand = await screen.findByRole('region', { name: 'Hand' })
    expect(within(hand).getAllByRole('button', { name: /^Play Ana card/ })).toHaveLength(7)
    const bob = screen.getByRole('region', { name: 'Bob' })
    expect(bob).toHaveTextContent('Hand7Library13')
    expect(within(bob).getByText('Commander damage from Ana: 3')).toBeInTheDocument()
    expect(screen.queryByText(/Bob card/)).not.toBeInTheDocument()
    expect(await seriousAccessibilityProblems()).toEqual([])

    // The page sends the action to the hub. The hub answers, then sends the patch.
    await user.click(screen.getByRole('button', { name: 'Draw (13)' }))
    const [invocation] = socket.invocations('Act')
    expect((invocation.arguments as unknown[])[1]).toEqual({ type: 'draw', playerId: '0' })
    act(() => socket.complete('Act', { accepted: 4 }))
    const after = applyAction(start, { type: 'draw', playerId: '0' })
    act(() => socket.patch(4, diffViews(playerView(start, '0'), playerView(after, '0')), viewHash(playerView(after, '0')), 'Ana drew 1 card'))
    expect(within(screen.getByRole('region', { name: 'Hand' })).getAllByRole('button', { name: /^Play Ana card/ })).toHaveLength(8)
    expect(screen.getByRole('region', { name: 'Game log' })).toHaveTextContent('Ana drew 1 card')
    expect(socket.invocations('Resync')).toHaveLength(0)

    // A patch with a wrong hash makes the page ask for a full view.
    act(() => socket.patch(5, [], '0000000000000000'))
    expect(socket.invocations('Resync')).toHaveLength(1)

    // The refused action shows the message of its code.
    await user.click(screen.getByRole('button', { name: 'Shuffle' }))
    act(() => socket.complete('Act', { rejected: 'not_your_object' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('You can change only your own cards and counters.')
    expect(screen.getByRole('link', { name: 'Open the table window' })).toBeInTheDocument()

    // A player who leaves the table keeps the seat, and comes back with the same token.
    await user.click(screen.getByRole('button', { name: 'Leave the table' }))
    act(() => socket.complete('Leave', null))
    expect(await screen.findByText('You left the table. Your place stays in the game.')).toBeInTheDocument()
    expect(socket.closed?.code).toBe(1000)
    await user.click(screen.getByRole('button', { name: 'Go back to the table' }))
    expect(sockets).toHaveLength(2)
    expect(sockets[1].url).toContain('access_token=game-token')
  })

  it('shows the reason when the host removes the player', async () => {
    window.history.replaceState(null, '', `/play/${gameId}`)
    storeSeat({ gameId, seat: '1', gameToken: 'game-token' })
    mockApi(anonymous)
    render(<App />)
    const socket = await connect(null, 0)
    act(() => socket.invoke('Closed', { reason: 'kicked' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The host removed you from the game.')
    expect(sessionStorage.getItem(`reliquary.onlineGame.${gameId}`)).toBeNull()
  })
})
