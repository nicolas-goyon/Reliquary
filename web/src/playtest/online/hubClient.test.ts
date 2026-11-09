import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HubClient, gapTimeout, keepAliveInterval, reconnectDelays, serverTimeout, type HubClientOptions, type Patch, type SocketLike, type Snapshot } from './hubClient'

const separator = '\u001e'

/** A fake WebSocket. The test plays the server: it reads the sent messages and gives frames to the client. */
class FakeSocket implements SocketLike {
  readyState = 0
  sent: string[] = []
  closed: { code?: number; reason?: string } | null = null
  onopen: ((event: unknown) => void) | null = null
  onmessage: ((event: { data: unknown }) => void) | null = null
  onclose: ((event: unknown) => void) | null = null
  onerror: ((event: unknown) => void) | null = null
  readonly url: string

  constructor(url: string) {
    this.url = url
  }

  send(data: string) {
    this.sent.push(data)
  }

  close(code?: number, reason?: string) {
    if (this.readyState === 3) {
      return
    }
    this.closed = { code, reason }
    this.readyState = 3
    this.onclose?.({})
  }

  /** The server accepts the connection and the handshake. */
  accept() {
    this.readyState = 1
    this.onopen?.({})
    this.frame('{}')
  }

  frame(...records: string[]) {
    this.onmessage?.({ data: records.map((record) => record + separator).join('') })
  }

  /** The messages that the client sent, as objects, with no handshake. */
  messages(): Record<string, unknown>[] {
    return this.sent
      .join('')
      .split(separator)
      .filter(Boolean)
      .map((record) => JSON.parse(record) as Record<string, unknown>)
      .filter((message) => 'type' in message)
  }

  invocations(target?: string) {
    return this.messages().filter((message) => message.type === 1 && (target === undefined || message.target === target))
  }

  /** The server answers the last invocation of a method. */
  complete(target: string, result: unknown) {
    const invocation = this.invocations(target).at(-1)
    if (!invocation) {
      throw new Error(`The client did not call ${target}.`)
    }
    this.frame(JSON.stringify({ type: 3, invocationId: invocation.invocationId, result }))
  }

  /** The server sends a patch. */
  patch(seq: number) {
    this.frame(JSON.stringify({ type: 1, target: 'Patch', arguments: [{ seq, actorSeat: 's1', log: `action ${seq}`, changes: [], hash: `h${seq}` }] }))
  }
}

function setup(overrides: Partial<HubClientOptions> = {}) {
  const sockets: FakeSocket[] = []
  const snapshots: Snapshot[] = []
  const patches: number[] = []
  const statuses: string[] = []
  const closed: string[] = []
  let token = 'token-1'
  const client = new HubClient({
    url: 'wss://example.org/api/v1/playtest/hub',
    accessToken: () => token,
    onSnapshot: (snapshot) => snapshots.push(snapshot),
    onPatch: (patch: Patch) => patches.push(patch.seq),
    onStatus: (status) => statuses.push(status),
    onClosed: (reason) => closed.push(reason),
    createSocket: (url) => {
      const socket = new FakeSocket(url)
      sockets.push(socket)
      return socket
    },
    ...overrides,
  })
  return { client, sockets, snapshots, patches, statuses, closed, setToken: (value: string) => (token = value), socket: () => sockets.at(-1)! }
}

/** Starts the client and answers Join with a snapshot. */
function joined(seq = 10) {
  const test = setup()
  test.client.start()
  test.socket().accept()
  test.socket().complete('Join', { seq, view: { players: [] }, hash: 'h' })
  return test
}

describe('hub client', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('sends the handshake, joins with the token in the query string, and gives the snapshot', () => {
    const test = setup()
    test.client.start()
    const socket = test.socket()
    expect(socket.url).toBe('wss://example.org/api/v1/playtest/hub?access_token=token-1')
    socket.accept()
    expect(socket.sent[0]).toBe(`{"protocol":"json","version":1}${separator}`)
    expect(socket.invocations('Join')).toEqual([{ type: 1, invocationId: '1', target: 'Join', arguments: [null] }])
    socket.complete('Join', { seq: 4, view: { players: [] }, hash: 'h4' })
    expect(test.snapshots).toEqual([{ seq: 4, view: { players: [] }, hash: 'h4' }])
    expect(test.client.seq).toBe(4)
    expect(test.statuses).toEqual(['connected'])
  })

  it('reads many records in one frame and a record in two frames', () => {
    const test = joined(0)
    const one = JSON.stringify({ type: 1, target: 'Patch', arguments: [{ seq: 1, actorSeat: 's1', log: '', changes: [], hash: '' }] })
    const two = JSON.stringify({ type: 1, target: 'Patch', arguments: [{ seq: 2, actorSeat: 's1', log: '', changes: [], hash: '' }] })
    test.socket().onmessage?.({ data: `${one}${separator}${two.slice(0, 10)}` })
    test.socket().onmessage?.({ data: `${two.slice(10)}${separator}` })
    expect(test.patches).toEqual([1, 2])
  })

  it('gives the patches in order and holds an early patch', () => {
    const test = joined(10)
    test.socket().patch(11)
    test.socket().patch(13)
    test.socket().patch(12)
    test.socket().patch(12)
    expect(test.patches).toEqual([11, 12, 13])
    expect(test.socket().invocations('Resync')).toEqual([])
  })

  it('asks for a Resync when a gap stays open for 1 second, and continues after the snapshot', () => {
    const test = joined(10)
    test.socket().patch(12)
    vi.advanceTimersByTime(gapTimeout - 1)
    expect(test.socket().invocations('Resync')).toEqual([])
    vi.advanceTimersByTime(1)
    expect(test.socket().invocations('Resync')).toHaveLength(1)
    test.socket().patch(14)
    test.socket().complete('Resync', { seq: 13, view: {}, hash: 'h13' })
    expect(test.snapshots.map((snapshot) => snapshot.seq)).toEqual([10, 13])
    expect(test.patches).toEqual([14])
    expect(test.client.seq).toBe(14)
  })

  it('holds the patches that arrive before the first snapshot', () => {
    const test = setup()
    test.client.start()
    test.socket().accept()
    test.socket().patch(6)
    test.socket().patch(5)
    test.socket().complete('Join', { seq: 4, view: {}, hash: 'h' })
    expect(test.patches).toEqual([5, 6])
  })

  it('sends an action and gives the answer of the server', async () => {
    const test = joined()
    const result = test.client.act({ type: 'draw', playerId: 's1' })
    const [invocation] = test.socket().invocations('Act')
    const [clientActionId, action] = invocation.arguments as [string, unknown]
    expect(clientActionId).toMatch(/^[0-9a-z]+-1$/)
    expect(action).toEqual({ type: 'draw', playerId: 's1' })
    test.socket().complete('Act', { accepted: 11 })
    await expect(result).resolves.toEqual({ accepted: 11 })
  })

  it('gives the error of a failed call', async () => {
    const test = joined()
    const result = test.client.invoke('Start')
    test.socket().frame(JSON.stringify({ type: 3, invocationId: test.socket().invocations('Start')[0].invocationId, error: 'Only the host can start the game.' }))
    await expect(result).rejects.toThrow('Only the host can start the game.')
  })

  it('reconnects with backoff, joins with the last sequence number, and sends an unanswered action again with the same ID', async () => {
    const test = joined(10)
    test.socket().patch(11)
    const result = test.client.act({ type: 'nextTurn' })
    const id = (test.socket().invocations('Act')[0].arguments as string[])[0]
    const call = test.client.invoke('Lobby')
    test.setToken('token-2')
    test.socket().close(1006)
    await expect(call).rejects.toThrow('The connection closed before the answer.')
    expect(test.statuses.at(-1)).toBe('reconnecting')

    vi.advanceTimersByTime(reconnectDelays[0] - 1)
    expect(test.sockets).toHaveLength(1)
    vi.advanceTimersByTime(1)
    expect(test.sockets).toHaveLength(2)
    // The second attempt fails: the next wait is longer.
    test.socket().close(1006)
    vi.advanceTimersByTime(reconnectDelays[1] - 1)
    expect(test.sockets).toHaveLength(2)
    vi.advanceTimersByTime(1)
    expect(test.sockets).toHaveLength(3)

    const socket = test.socket()
    expect(socket.url).toContain('access_token=token-2')
    socket.accept()
    expect(socket.invocations('Join')[0].arguments).toEqual([11])
    expect((socket.invocations('Act')[0].arguments as string[])[0]).toBe(id)
    socket.complete('Act', { accepted: 12 })
    await expect(result).resolves.toEqual({ accepted: 12 })
    socket.complete('Join', { seq: 12, view: {}, hash: 'h12' })
    expect(test.client.seq).toBe(12)

    // A good connection resets the backoff.
    socket.close(1006)
    vi.advanceTimersByTime(reconnectDelays[0])
    expect(test.sockets).toHaveLength(4)
  })

  it('waits at most 10 seconds between two attempts', () => {
    const test = setup()
    test.client.start()
    for (let attempt = 0; attempt < 8; attempt++) {
      test.socket().close(1006)
      vi.advanceTimersByTime(10000)
    }
    expect(test.sockets).toHaveLength(9)
    expect(reconnectDelays.at(-1)).toBe(10000)
  })

  it('sends a ping when it sends nothing for 15 seconds', () => {
    const test = joined()
    vi.advanceTimersByTime(keepAliveInterval - 1)
    test.socket().frame('{"type":6}')
    expect(test.socket().messages().filter((message) => message.type === 6)).toEqual([])
    vi.advanceTimersByTime(1)
    expect(test.socket().messages().filter((message) => message.type === 6)).toHaveLength(1)
  })

  it('closes the connection and reconnects when the server sends nothing for 30 seconds', () => {
    const test = joined()
    vi.advanceTimersByTime(serverTimeout)
    expect(test.sockets[0].closed?.code).toBe(4000)
    vi.advanceTimersByTime(reconnectDelays[0])
    expect(test.sockets).toHaveLength(2)
  })

  it('stops when the server closes the game, and does not reconnect', async () => {
    const test = joined()
    const result = test.client.act({ type: 'nextTurn' })
    test.socket().frame(JSON.stringify({ type: 1, target: 'Closed', arguments: [{ reason: 'kicked' }] }))
    expect(test.closed).toEqual(['kicked'])
    await expect(result).rejects.toThrow('The client stopped.')
    vi.advanceTimersByTime(60000)
    expect(test.sockets).toHaveLength(1)
    expect(test.statuses.at(-1)).toBe('closed')
  })

  it('reconnects after a close message that allows it, and stops after one that does not', () => {
    const test = joined()
    test.socket().frame('{"type":7,"allowReconnect":true}')
    vi.advanceTimersByTime(reconnectDelays[0])
    expect(test.sockets).toHaveLength(2)
    test.socket().accept()
    test.socket().frame('{"type":7,"error":"Server shutdown."}')
    vi.advanceTimersByTime(60000)
    expect(test.sockets).toHaveLength(2)
    expect(test.closed).toEqual(['Server shutdown.'])
  })

  it('closes the socket when the handshake fails', () => {
    const test = setup()
    test.client.start()
    test.socket().readyState = 1
    test.socket().onopen?.({})
    test.socket().frame('{"error":"Requested protocol is not available."}')
    expect(test.sockets[0].closed).toEqual({ code: 1002, reason: 'Requested protocol is not available.' })
  })

  it('closes the connection and tries again when Join fails', () => {
    const test = setup()
    test.client.start()
    test.socket().accept()
    test.socket().frame(JSON.stringify({ type: 3, invocationId: test.socket().invocations('Join')[0].invocationId, error: 'The game is not available.' }))
    expect(test.sockets[0].closed?.code).toBe(4001)
    vi.advanceTimersByTime(reconnectDelays[0])
    expect(test.sockets).toHaveLength(2)
  })

  it('gives the lobby to onLobby', () => {
    const lobbies: unknown[] = []
    const test = setup({ onLobby: (lobby) => lobbies.push(lobby) })
    test.client.start()
    test.socket().accept()
    test.socket().frame(JSON.stringify({ type: 1, target: 'Lobby', arguments: [{ hostSeat: 's1' }] }))
    expect(lobbies).toEqual([{ hostSeat: 's1' }])
  })

  it('refuses a call when it is not connected, and stops', async () => {
    const test = setup()
    await expect(test.client.invoke('Join')).rejects.toThrow('The client is not connected.')
    test.client.start()
    test.socket().accept()
    test.client.stop()
    expect(test.sockets[0].closed?.code).toBe(1000)
    vi.advanceTimersByTime(60000)
    expect(test.sockets).toHaveLength(1)
  })
})
