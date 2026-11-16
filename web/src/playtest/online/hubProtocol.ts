/*
 * The JSON hub protocol of SignalR, version 1, with no library. Only the parts that the table hub uses.
 * Each message is one JSON text followed by the record separator 0x1E. One WebSocket frame can contain many messages.
 * Specification: https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/HubProtocol.md
 */

export const recordSeparator = '\u001e'

/** The message types of the hub protocol that the client uses. */
export const messageTypes = { invocation: 1, completion: 3, ping: 6, close: 7 } as const

export type HubMessage =
  | { type: 1; invocationId?: string; target: string; arguments: unknown[] }
  | { type: 3; invocationId: string; result?: unknown; error?: string }
  | { type: 6 }
  | { type: 7; error?: string; allowReconnect?: boolean }

/** The first message of the client after the WebSocket opens. */
export function handshakeRequest(): string {
  return JSON.stringify({ protocol: 'json', version: 1 }) + recordSeparator
}

/** Writes one message with its record separator. */
export function formatMessage(message: HubMessage): string {
  return JSON.stringify(message) + recordSeparator
}

/**
 * Splits the data of one frame into records. The text after the last separator is not complete: it stays in rest.
 * Give the rest of the last frame as the start of the next frame.
 */
export function splitRecords(data: string): { records: string[]; rest: string } {
  const parts = data.split(recordSeparator)
  return { records: parts.slice(0, -1), rest: parts.at(-1) ?? '' }
}

/** Reads the answer of the server to the handshake: null when the handshake is correct, else the error. */
export function parseHandshakeResponse(record: string): string | null {
  let value: unknown
  try {
    value = JSON.parse(record)
  } catch {
    return 'The handshake response is not JSON.'
  }
  if (!value || typeof value !== 'object') {
    return 'The handshake response is not an object.'
  }
  const error = (value as { error?: unknown }).error
  return typeof error === 'string' ? error : null
}

/** Reads one record. It gives null for a message type that the client does not use, or for a message with a wrong format. */
export function parseMessage(record: string): HubMessage | null {
  let value: unknown
  try {
    value = JSON.parse(record)
  } catch {
    return null
  }
  if (!value || typeof value !== 'object') {
    return null
  }
  const message = value as Record<string, unknown>
  switch (message.type) {
    case messageTypes.invocation:
      return typeof message.target === 'string' && Array.isArray(message.arguments)
        ? { type: 1, target: message.target, arguments: message.arguments, ...(typeof message.invocationId === 'string' ? { invocationId: message.invocationId } : {}) }
        : null
    case messageTypes.completion:
      return typeof message.invocationId === 'string'
        ? { type: 3, invocationId: message.invocationId, ...('result' in message ? { result: message.result } : {}), ...(typeof message.error === 'string' ? { error: message.error } : {}) }
        : null
    case messageTypes.ping:
      return { type: 6 }
    case messageTypes.close:
      return { type: 7, ...(typeof message.error === 'string' ? { error: message.error } : {}), allowReconnect: message.allowReconnect === true }
    default:
      return null
  }
}

/**
 * Reads the error code of a hub method error. SignalR sends the text
 * "An unexpected error occurred invoking 'X' on the server. HubException: <code>: <message>".
 * The code is the text after "HubException: " and before the next ":". It gives "internal_error" when the text has no code.
 */
export function hubErrorCode(text: string): string {
  const match = /HubException: ([a-z0-9_]+)(?::|$)/.exec(text)
  return match ? match[1] : 'internal_error'
}
