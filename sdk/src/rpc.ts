import {
  defaultTimeoutMs,
  errorCodes,
  maxMessageBytes,
  type RequestId,
  type RpcErrorObject,
  type RpcMessage,
  type RpcRequest,
} from './protocol.ts'

/** The methods of one side: the type of the parameters and of the result of each method. */
export type MethodMap = { [method: string]: { params: unknown; result: unknown } }

/** The part of a MessagePort that the RPC peer uses. */
export interface PortLike {
  postMessage(message: string): void
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void
  removeEventListener(type: 'message', listener: (event: { data: unknown }) => void): void
  start?(): void
  close?(): void
}

/** An error of a call: an error answer of the other side, a timeout or a closed connection. */
export class RpcError extends Error {
  readonly code: number
  readonly data: unknown

  constructor(code: number, message: string, data?: unknown) {
    super(message)
    this.name = 'RpcError'
    this.code = code
    this.data = data
  }
}

export interface CallOptions {
  /** The time limit of the call, in milliseconds. The default is 5000. */
  timeoutMs?: number
}

export interface PeerOptions {
  /** The time limit of a call, in milliseconds, when the call gives no limit. The default is 5000. */
  timeoutMs?: number
  /** The maximum size of a message, in bytes of UTF-8. The default is 1 MB. */
  maxMessageBytes?: number
}

type Handler = (params: unknown) => unknown

interface Pending {
  resolve(value: unknown): void
  reject(error: RpcError): void
  timer: ReturnType<typeof setTimeout>
}

/** Gets the size of a string in bytes of UTF-8, without a copy of the string. */
export function utf8Length(text: string): number {
  let bytes = 0
  for (let index = 0; index < text.length; index++) {
    const code = text.charCodeAt(index)
    if (code < 0x80) {
      bytes += 1
    } else if (code < 0x800) {
      bytes += 2
    } else if (code >= 0xd800 && code <= 0xdbff && index + 1 < text.length) {
      // A surrogate pair is one code point of 4 bytes.
      bytes += 4
      index++
    } else {
      bytes += 3
    }
  }
  return bytes
}

/**
 * One side of a JSON-RPC 2.0 connection on a MessagePort.
 * `Local` gives the methods that this side answers. `Remote` gives the methods that the other side answers.
 */
export class RpcPeer<Local extends MethodMap, Remote extends MethodMap> {
  private readonly port: PortLike
  private readonly timeoutMs: number
  private readonly maxBytes: number
  private readonly handlers = new Map<string, Handler>()
  private readonly pending = new Map<RequestId, Pending>()
  private nextId = 1
  private closed = false

  constructor(port: PortLike, options: PeerOptions = {}) {
    this.port = port
    this.timeoutMs = options.timeoutMs ?? defaultTimeoutMs
    this.maxBytes = options.maxMessageBytes ?? maxMessageBytes
    this.port.addEventListener('message', this.receive)
    this.port.start?.()
  }

  /** True after `close`. A closed peer refuses all calls. */
  get isClosed(): boolean {
    return this.closed
  }

  /** Sets the function that answers a method. The function can return a value or a promise. */
  handle<Method extends keyof Local & string>(
    method: Method,
    handler: (params: Local[Method]['params']) => Local[Method]['result'] | Promise<Local[Method]['result']>,
  ): void {
    this.handlers.set(method, handler as Handler)
  }

  /**
   * Calls a method of the other side.
   * @throws RpcError when the other side answers with an error, when the time limit ends,
   * when the message is too large, or when the connection is closed.
   */
  call<Method extends keyof Remote & string>(
    method: Method,
    params: Remote[Method]['params'],
    options: CallOptions = {},
  ): Promise<Remote[Method]['result']> {
    if (this.closed) {
      return Promise.reject(new RpcError(errorCodes.connectionClosed, 'The connection is closed.'))
    }
    const id = this.nextId++
    const request: RpcRequest = { jsonrpc: '2.0', id, method, params }
    return new Promise((resolve, reject) => {
      const error = this.send(request)
      if (error) {
        reject(error)
        return
      }
      const timeoutMs = options.timeoutMs ?? this.timeoutMs
      const timer = setTimeout(() => {
        this.pending.delete(id)
        reject(new RpcError(errorCodes.timeout, `The method "${method}" did not answer in ${timeoutMs} ms.`))
      }, timeoutMs)
      this.pending.set(id, { resolve: resolve as (value: unknown) => void, reject, timer })
    })
  }

  /** Sends a notification: a message with no answer. */
  notify<Method extends keyof Remote & string>(method: Method, params: Remote[Method]['params']): void {
    if (this.closed) {
      throw new RpcError(errorCodes.connectionClosed, 'The connection is closed.')
    }
    const error = this.send({ jsonrpc: '2.0', method, params })
    if (error) {
      throw error
    }
  }

  /** Closes the connection. The calls that wait for an answer fail with the code `connectionClosed`. */
  close(): void {
    if (this.closed) {
      return
    }
    this.closed = true
    this.port.removeEventListener('message', this.receive)
    this.port.close?.()
    for (const [id, pending] of this.pending) {
      clearTimeout(pending.timer)
      pending.reject(new RpcError(errorCodes.connectionClosed, 'The connection is closed.'))
      this.pending.delete(id)
    }
  }

  private send(message: RpcMessage): RpcError | undefined {
    let text: string
    try {
      text = JSON.stringify(message)
    } catch {
      return new RpcError(errorCodes.invalidParams, 'The message cannot be converted to JSON.')
    }
    if (text.length > this.maxBytes || utf8Length(text) > this.maxBytes) {
      return new RpcError(errorCodes.messageTooLarge, `The message is larger than ${this.maxBytes} bytes.`)
    }
    this.port.postMessage(text)
    return undefined
  }

  private readonly receive = (event: { data: unknown }): void => {
    if (this.closed || typeof event.data !== 'string') {
      return
    }
    const text = event.data
    if (text.length > this.maxBytes || utf8Length(text) > this.maxBytes) {
      this.sendError(null, errorCodes.messageTooLarge, `The message is larger than ${this.maxBytes} bytes.`)
      return
    }
    let message: unknown
    try {
      message = JSON.parse(text)
    } catch {
      this.sendError(null, errorCodes.parseError, 'The message is not valid JSON.')
      return
    }
    if (!isObject(message) || message.jsonrpc !== '2.0') {
      this.sendError(null, errorCodes.invalidRequest, 'The message is not a JSON-RPC 2.0 message.')
      return
    }
    if (typeof message.method === 'string') {
      void this.answer(message as unknown as RpcRequest)
    } else if ('result' in message || 'error' in message) {
      this.settle(message)
    }
  }

  private async answer(request: RpcRequest): Promise<void> {
    const hasId = typeof request.id === 'number' || typeof request.id === 'string'
    const handler = this.handlers.get(request.method)
    if (!handler) {
      if (hasId) {
        this.sendError(request.id, errorCodes.methodNotFound, `The method "${request.method}" does not exist.`)
      }
      return
    }
    try {
      const result = await handler(request.params)
      if (hasId && !this.closed) {
        const error = this.send({ jsonrpc: '2.0', id: request.id, result: result ?? null })
        if (error) {
          this.sendError(request.id, error.code, error.message)
        }
      }
    } catch (error) {
      if (hasId && !this.closed) {
        const rpcError = error instanceof RpcError ? error : new RpcError(errorCodes.internalError, error instanceof Error ? error.message : 'The method failed.')
        this.sendError(request.id, rpcError.code, rpcError.message, rpcError.data)
      }
    }
  }

  private settle(message: Record<string, unknown>): void {
    const id = message.id as RequestId
    const pending = this.pending.get(id)
    if (!pending) {
      return
    }
    this.pending.delete(id)
    clearTimeout(pending.timer)
    if ('error' in message) {
      const error = (isObject(message.error) ? message.error : {}) as Partial<RpcErrorObject>
      pending.reject(new RpcError(typeof error.code === 'number' ? error.code : errorCodes.internalError, typeof error.message === 'string' ? error.message : 'The method failed.', error.data))
    } else {
      pending.resolve(message.result)
    }
  }

  private sendError(id: RequestId | null, code: number, message: string, data?: unknown): void {
    if (this.closed) {
      return
    }
    const error: RpcErrorObject = data === undefined ? { code, message } : { code, message, data }
    if (this.send({ jsonrpc: '2.0', id, error })) {
      this.send({ jsonrpc: '2.0', id, error: { code, message } })
    }
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
