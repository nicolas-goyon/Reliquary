import type { TableAction } from '../engine'
import { formatMessage, handshakeRequest, messageTypes, parseHandshakeResponse, parseMessage, splitRecords, type HubMessage } from './hubProtocol'
import { ReorderBuffer } from './reorderBuffer'

/*
 * A small client for the table hub of online games. It uses the browser WebSocket and the JSON hub protocol of SignalR, with no library.
 * The design of issue #54, section 3, gives the hub methods and the messages. ADR 0047 gives the server design.
 */

/** The full view of a player at one sequence number. The hub methods Join and Resync give it. */
export interface Snapshot {
  seq: number
  /** The view of the player. The client does not read it: it gives it to onSnapshot. */
  view: unknown
  hash: string
}

/** The changes of one action in the view of the player. */
export interface Patch {
  seq: number
  actorSeat: string
  /** The public text of the action, for example "Ana drew 2 cards". */
  log: string
  changes: unknown[]
  /** The hash of the view after the patch. */
  hash: string
}

export type ActResult = { accepted: number } | { rejected: string }

export type HubStatus = 'connecting' | 'connected' | 'reconnecting' | 'closed'

/** The part of the browser WebSocket that the client uses. A test can give a fake socket. */
export interface SocketLike {
  readonly readyState: number
  send(data: string): void
  close(code?: number, reason?: string): void
  onopen: ((event: unknown) => void) | null
  onmessage: ((event: { data: unknown }) => void) | null
  onclose: ((event: unknown) => void) | null
  onerror: ((event: unknown) => void) | null
}

export interface HubClientOptions {
  /** The URL of the hub, for example "wss://example.org/api/v1/playtest/hub". */
  url: string
  /** Gives the game token. The client reads it at each connection, so the caller can replace it. */
  accessToken: () => string
  onSnapshot(snapshot: Snapshot): void
  /** Gets the patches in the order of their sequence numbers, with no gap. */
  onPatch(patch: Patch): void
  onLobby?(lobby: unknown): void
  /** The server closed the game, for example with the reason "ended", "kicked" or "expired". The client does not reconnect. */
  onClosed?(reason: string): void
  onStatus?(status: HubStatus): void
  /** Makes the socket. The default is the browser WebSocket. */
  createSocket?: (url: string) => SocketLike
}

/** The waits before each new connection attempt, in milliseconds. The last value repeats. */
export const reconnectDelays = [500, 1000, 2000, 4000, 8000, 10000] as const
/** A gap in the sequence numbers that stays open for this time causes a Resync. */
export const gapTimeout = 1000
/** The client sends a ping after this time with no message to the server. */
export const keepAliveInterval = 15000
/** The client closes the connection after this time with no message from the server. */
export const serverTimeout = 30000

const socketOpen = 1

interface Pending {
  method: string
  args: unknown[]
  resolve(value: unknown): void
  reject(error: Error): void
}

/** The connection to the table hub of one game, for one seat. */
export class HubClient {
  private socket: SocketLike | null = null
  private status: HubStatus = 'connecting'
  private stopped = false
  private attempt = 0
  private rest = ''
  private handshakeDone = false
  private nextInvocationId = 1
  private pending = new Map<string, Pending>()
  /** The Act calls with no answer. The client sends them again after a reconnect, with the same clientActionId. */
  private unansweredActs = new Map<string, Pending>()
  private nextActionId = 1
  private readonly actionPrefix = Math.random().toString(36).slice(2, 10)
  private buffer: ReorderBuffer<Patch> | null = null
  /** The patches that arrive before the first snapshot. */
  private early: Patch[] = []
  private resyncing = false
  private timers: { reconnect?: ReturnType<typeof setTimeout>; gap?: ReturnType<typeof setTimeout>; keepAlive?: ReturnType<typeof setTimeout>; server?: ReturnType<typeof setTimeout> } = {}

  private readonly options: HubClientOptions

  constructor(options: HubClientOptions) {
    this.options = options
  }

  /** The sequence number of the last change in the view of the client, or null before the first snapshot. */
  get seq(): number | null {
    return this.buffer?.seq ?? null
  }

  /** Opens the connection. Then the client joins the game and gives the snapshot to onSnapshot. */
  start(): void {
    this.stopped = false
    this.open()
  }

  /** Closes the connection and stops the reconnects. The pending calls fail. */
  stop(): void {
    this.stopped = true
    this.clearTimers()
    this.failPending('The client stopped.', true)
    this.socket?.close(1000)
    this.socket = null
    this.setStatus('closed')
  }

  /** Calls a hub method and gives its result. The call fails when the connection closes before the answer. */
  invoke<T = unknown>(method: string, ...args: unknown[]): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const pending: Pending = { method, args, resolve: resolve as (value: unknown) => void, reject }
      if (!this.send(pending)) {
        reject(new Error(`The client is not connected. The call of ${method} failed.`))
      }
    })
  }

  /**
   * Sends an action of the player. When the connection closes before the answer, the client sends the action again after the reconnect.
   * The server keeps the last clientActionId of each seat, so it applies the action only one time.
   */
  act(action: TableAction): Promise<ActResult> {
    const clientActionId = `${this.actionPrefix}-${this.nextActionId++}`
    return new Promise<ActResult>((resolve, reject) => {
      const pending: Pending = { method: 'Act', args: [clientActionId, action], resolve: resolve as (value: unknown) => void, reject }
      this.unansweredActs.set(clientActionId, pending)
      this.send(pending)
    })
  }

  /** Asks for a full view. Call it when the hash of the view after a patch is not the hash of the patch. */
  resync(): void {
    if (this.resyncing || !this.handshakeDone) {
      return
    }
    this.resyncing = true
    const done = () => {
      this.resyncing = false
    }
    // The snapshot goes to the view directly in the completion, before the patches of the next frames.
    this.send({
      method: 'Resync',
      args: [],
      resolve: (snapshot) => {
        done()
        this.acceptSnapshot(snapshot as Snapshot)
      },
      reject: done,
    })
  }

  private open(): void {
    this.clearTimers()
    this.rest = ''
    this.handshakeDone = false
    const separator = this.options.url.includes('?') ? '&' : '?'
    const url = `${this.options.url}${separator}access_token=${encodeURIComponent(this.options.accessToken())}`
    const socket = (this.options.createSocket ?? ((value: string) => new WebSocket(value) as unknown as SocketLike))(url)
    this.socket = socket
    socket.onopen = () => {
      socket.send(handshakeRequest())
      this.watchServer()
    }
    socket.onmessage = (event) => {
      if (socket === this.socket && typeof event.data === 'string') {
        this.receive(event.data)
      }
    }
    socket.onclose = () => {
      if (socket === this.socket) {
        this.lost()
      }
    }
    socket.onerror = () => undefined
  }

  private receive(data: string): void {
    this.watchServer()
    const { records, rest } = splitRecords(this.rest + data)
    this.rest = rest
    for (const record of records) {
      if (!this.handshakeDone) {
        const error = parseHandshakeResponse(record)
        if (error !== null) {
          this.socket?.close(1002, error)
          return
        }
        this.connected()
        continue
      }
      const message = parseMessage(record)
      if (message) {
        this.handle(message)
      }
    }
  }

  private connected(): void {
    this.handshakeDone = true
    this.attempt = 0
    this.setStatus('connected')
    this.keepAlive()
    // The snapshot goes to the view directly in the completion, before the patches of the next frames.
    // When Join fails, the client has no view: it closes the connection and tries again with the backoff.
    const socket = this.socket
    this.send({ method: 'Join', args: [this.seq], resolve: (snapshot) => this.acceptSnapshot(snapshot as Snapshot), reject: () => {
        if (!this.stopped && socket === this.socket) {
          socket?.close(4001, 'Join failed.')
        }
      },
    })
    for (const pending of this.unansweredActs.values()) {
      this.send(pending)
    }
  }

  private handle(message: HubMessage): void {
    switch (message.type) {
      case messageTypes.completion: {
        const pending = this.pending.get(message.invocationId)
        this.pending.delete(message.invocationId)
        if (!pending) {
          return
        }
        if (pending.method === 'Act') {
          this.unansweredActs.delete(pending.args[0] as string)
        }
        if (message.error !== undefined) {
          pending.reject(new Error(message.error))
        } else {
          pending.resolve(message.result)
        }
        return
      }
      case messageTypes.invocation:
        this.serverCall(message.target, message.arguments)
        return
      case messageTypes.ping:
        return
      case messageTypes.close:
        if (message.allowReconnect) {
          this.socket?.close(1000)
        } else {
          this.stop()
          this.options.onClosed?.(message.error ?? 'closed')
        }
        return
    }
  }

  private serverCall(target: string, args: unknown[]): void {
    switch (target) {
      case 'Patch':
        this.acceptPatch(args[0] as Patch)
        return
      case 'Lobby':
        this.options.onLobby?.(args[0])
        return
      case 'Closed': {
        const reason = (args[0] as { reason?: unknown } | undefined)?.reason
        this.stop()
        this.options.onClosed?.(typeof reason === 'string' ? reason : 'closed')
        return
      }
    }
  }

  private acceptPatch(patch: Patch): void {
    if (!patch || !Number.isInteger(patch.seq)) {
      return
    }
    if (!this.buffer) {
      this.early.push(patch)
      return
    }
    this.deliver(this.buffer.push(patch, Date.now()))
  }

  private acceptSnapshot(snapshot: Snapshot): void {
    if (!snapshot || !Number.isInteger(snapshot.seq)) {
      return
    }
    const now = Date.now()
    const buffer = this.buffer ?? new ReorderBuffer<Patch>(snapshot.seq)
    this.buffer = buffer
    const early = this.early
    this.early = []
    this.options.onSnapshot(snapshot)
    const ready = buffer.reset(snapshot.seq, now)
    this.deliver([...ready, ...early.flatMap((patch) => buffer.push(patch, now))])
  }

  private deliver(patches: Patch[]): void {
    for (const patch of patches) {
      this.options.onPatch(patch)
    }
    this.watchGap()
  }

  /** Asks for a Resync when a gap stays open for gapTimeout. */
  private watchGap(): void {
    clearTimeout(this.timers.gap)
    const age = this.buffer?.gapAge(Date.now())
    if (age === null || age === undefined) {
      return
    }
    this.timers.gap = setTimeout(() => {
      const current = this.buffer?.gapAge(Date.now())
      if (current !== null && current !== undefined && current >= gapTimeout) {
        this.resync()
      } else {
        this.watchGap()
      }
    }, Math.max(0, gapTimeout - age))
  }

  private send(pending: Pending): boolean {
    if (!this.socket || this.socket.readyState !== socketOpen || !this.handshakeDone) {
      return false
    }
    const invocationId = String(this.nextInvocationId++)
    this.pending.set(invocationId, pending)
    this.socket.send(formatMessage({ type: 1, invocationId, target: pending.method, arguments: pending.args }))
    this.keepAlive()
    return true
  }

  private keepAlive(): void {
    clearTimeout(this.timers.keepAlive)
    this.timers.keepAlive = setTimeout(() => {
      if (this.socket?.readyState === socketOpen && this.handshakeDone) {
        this.socket.send(formatMessage({ type: 6 }))
      }
      this.keepAlive()
    }, keepAliveInterval)
  }

  private watchServer(): void {
    clearTimeout(this.timers.server)
    this.timers.server = setTimeout(() => this.socket?.close(4000, 'The server did not answer.'), serverTimeout)
  }

  private lost(): void {
    this.socket = null
    this.handshakeDone = false
    this.clearTimers()
    this.failPending('The connection closed before the answer.', false)
    if (this.stopped) {
      return
    }
    this.setStatus('reconnecting')
    const delay = reconnectDelays[Math.min(this.attempt, reconnectDelays.length - 1)]
    this.attempt++
    this.timers.reconnect = setTimeout(() => this.open(), delay)
  }

  /** Fails the pending calls. The Act calls stay for the next connection, except when the client stops. */
  private failPending(reason: string, includeActs: boolean): void {
    for (const pending of this.pending.values()) {
      if (pending.method !== 'Act') {
        pending.reject(new Error(reason))
      }
    }
    this.pending.clear()
    if (includeActs) {
      for (const pending of this.unansweredActs.values()) {
        pending.reject(new Error(reason))
      }
      this.unansweredActs.clear()
    }
  }

  private clearTimers(): void {
    for (const timer of Object.values(this.timers)) {
      clearTimeout(timer)
    }
    this.timers = {}
  }

  private setStatus(status: HubStatus): void {
    if (this.status !== status) {
      this.status = status
      this.options.onStatus?.(status)
    }
  }
}
