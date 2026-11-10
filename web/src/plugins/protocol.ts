/**
 * The RPC protocol between the core and a plugin frame. See docs/decisions/0049-plugin-sandbox.md.
 *
 * The core and the plugin send JSON-RPC 2.0 messages on a MessageChannel.
 * Each message is one JSON string. The protocol version is the core API version.
 *
 * The plugin SDK has the same protocol in sdk/src/protocol.ts and sdk/src/rpc.ts.
 * The container build of the web app sees only the folder web/, so the web app keeps a copy.
 * Change the two copies together.
 */

/** The time limit of a call, in milliseconds, when the caller gives no other limit. */
export const defaultTimeoutMs = 5000

/** The maximum size of one message, in bytes of UTF-8. */
export const maxMessageBytes = 1024 * 1024

/** The interval between two heartbeats of the core, in milliseconds. */
export const heartbeatIntervalMs = 2000

/** The core removes a frame after this number of missed heartbeats. */
export const maxMissedHeartbeats = 3

/**
 * The window messages of the connection. They are the only messages that do not use the port.
 * 1. The plugin frame sends `ready` to its parent window.
 * 2. The core checks the source of the message, then sends `connect` one time, with the port.
 */
export const readyMessageType = 'reliquary:ready'
export const connectMessageType = 'reliquary:connect'

/** The message that the plugin frame sends to its parent window when the SDK is ready to get the port. */
export interface ReadyMessage {
  type: typeof readyMessageType
  /** The core API version of the SDK. */
  coreApi: string
}

/** The message that the core sends one time to the plugin frame, with the port in the transfer list. */
export interface ConnectMessage {
  type: typeof connectMessageType
  /** The core API version of the core. */
  coreApi: string
  plugin: { id: string; version: string }
  /** The language of the interface, for example "en" or "fr". */
  language: string
  /** True when the plugin comes from a URL in the development mode. */
  development: boolean
}

/** The error codes of the protocol. The codes from -32768 to -32000 come from JSON-RPC 2.0. */
export const errorCodes = {
  parseError: -32700,
  invalidRequest: -32600,
  methodNotFound: -32601,
  invalidParams: -32602,
  internalError: -32603,
  /** The other side did not answer in the time limit. */
  timeout: -32000,
  /** The message is larger than the size limit. */
  messageTooLarge: -32001,
  /** The user did not accept the permission that the call needs. */
  permissionDenied: -32002,
  /** The connection is closed, for example because the core removed the frame. */
  connectionClosed: -32003,
} as const

export type RequestId = number | string

export interface RpcRequest {
  jsonrpc: '2.0'
  id: RequestId
  method: string
  params?: unknown
}

export interface RpcNotification {
  jsonrpc: '2.0'
  method: string
  params?: unknown
}

export interface RpcErrorObject {
  code: number
  message: string
  data?: unknown
}

export type RpcResponse =
  | { jsonrpc: '2.0'; id: RequestId | null; result: unknown }
  | { jsonrpc: '2.0'; id: RequestId | null; error: RpcErrorObject }

export type RpcMessage = RpcRequest | RpcNotification | RpcResponse
