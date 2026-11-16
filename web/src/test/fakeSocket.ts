/** A fake WebSocket for the tests of the hub client and of the online pages. */
import type { SocketLike } from '../playtest/online/hubClient'

const separator = '\u001e'

/** A fake WebSocket. The test plays the server: it reads the sent messages and gives frames to the client. */
export class FakeSocket implements SocketLike {
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
  patch(seq: number, changes: unknown[] = [], hash = `h${seq}`, log = `action ${seq}`) {
    this.invoke('Patch', { seq, actorSeat: 's1', log, changes, hash })
  }

  /** The server calls a method of the client. */
  invoke(target: string, ...args: unknown[]) {
    this.frame(JSON.stringify({ type: 1, target, arguments: args }))
  }
}
