import type { HostPeer } from './connection'
import { addNotice, type NoticeTone } from './notices'
import { tokenPluginId, type PluginTokens, type TokenPlugin } from './pluginTokens'
import { errorCodes } from './protocol'
import { RpcError } from './rpc'

/**
 * The methods `api.fetch`, `storage.*` and `ui.notify` of the core. See docs/decisions/0049-plugin-sandbox.md.
 * The core sends each request of a plugin to the public API with a plugin token, so the API applies the scopes that the user accepted.
 */

const methods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']
const tones: NoticeTone[] = ['info', 'success', 'error']

/** The maximum length of a key of the plugin storage. The API has the same limit. */
export const maxStorageKeyLength = 128

/** The maximum length of the path of `api.fetch`. */
const maxPathLength = 2000

export interface ApiPlugin extends TokenPlugin {
  name: string
}

export interface HostApiOptions {
  tokens: PluginTokens
  fetcher?: typeof fetch
}

/**
 * Checks the path of `api.fetch`: a path of the public API after `/api/v1`, for example "/decks?limit=10".
 * The core refuses a path that can leave `/api/v1` or that names a different host.
 */
export function checkApiPath(path: unknown): string {
  if (typeof path !== 'string' || !path.startsWith('/') || path.length > maxPathLength) {
    throw new RpcError(errorCodes.invalidParams, 'The parameter "path" must be a path of the public API, for example "/decks".')
  }
  const [pathname] = path.split(/[?#]/, 1)
  let decoded: string
  try {
    decoded = decodeURIComponent(pathname)
  } catch {
    throw new RpcError(errorCodes.invalidParams, 'The parameter "path" has an encoding that is not correct.')
  }
  // "//" names a host. A dot segment or a backslash can leave "/api/v1" after the browser normalizes the address.
  const hasControlCharacter = [...path].some((character) => character.charCodeAt(0) < 0x20)
  if (/\/\/|\\|(^|\/)\.{1,2}(\/|$)/.test(decoded) || hasControlCharacter || path.includes('#')) {
    throw new RpcError(errorCodes.invalidParams, 'The parameter "path" must not contain "//", "\\", "#", "." or ".." segments.')
  }
  return path
}

/** Adds the methods `api.fetch`, `storage.*` and `ui.notify` to the peer of a plugin. */
export function registerHostApi(peer: HostPeer, plugin: ApiPlugin, options: HostApiOptions): void {
  const fetcher = options.fetcher ?? fetch

  const send = async (path: string, method: string, body: unknown): Promise<Response> => {
    const headers: Record<string, string> = { Accept: 'application/json', Authorization: `Bearer ${await options.tokens.get(plugin)}` }
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json'
    }
    // The request has no cookie, so the plugin cannot use the refresh cookie of the web session.
    return fetcher(`/api/v1${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), credentials: 'omit', redirect: 'error' })
  }

  /** Sends a request, and one more time with a new token after the status 401: the token expired, or the plugin changed. */
  const sendWithToken = async (path: string, method: string, body: unknown): Promise<Response> => {
    try {
      const response = await send(path, method, body)
      if (response.status !== 401) {
        return response
      }
      options.tokens.forget(plugin.id)
      return await send(path, method, body)
    } catch {
      throw new RpcError(errorCodes.internalError, 'The core did not send the request to the public API. Make sure that the plugin is active for the user.')
    }
  }

  const readJson = async (response: Response): Promise<unknown> => {
    const text = await response.text()
    if (!text) {
      return null
    }
    try {
      return JSON.parse(text) as unknown
    } catch {
      return text
    }
  }

  peer.handle('api.fetch', async (params) => {
    const request = (params ?? {}) as { path?: unknown; method?: unknown; body?: unknown }
    const path = checkApiPath(request.path)
    const method = request.method === undefined ? 'GET' : request.method
    if (typeof method !== 'string' || !methods.includes(method)) {
      throw new RpcError(errorCodes.invalidParams, `The parameter "method" must be one of ${methods.join(', ')}.`)
    }
    if ((method === 'GET' || method === 'DELETE') && request.body !== undefined) {
      throw new RpcError(errorCodes.invalidParams, `A ${method} request has no body.`)
    }
    const response = await sendWithToken(path, method, request.body)
    return { status: response.status, body: await readJson(response) }
  })

  // The storage of the plugin for the user. The API keeps it, because the plugin frame has no storage.
  const storagePath = `/me/extensions/${encodeURIComponent(tokenPluginId(plugin))}/storage`
  const storageKey = (params: unknown): string => {
    const key = (params as { key?: unknown } | null)?.key
    if (typeof key !== 'string' || key.length === 0 || key.length > maxStorageKeyLength) {
      throw new RpcError(errorCodes.invalidParams, `The parameter "key" must be a text of 1 to ${maxStorageKeyLength} characters.`)
    }
    return `${storagePath}?key=${encodeURIComponent(key)}`
  }
  const storageFailed = (response: Response): RpcError =>
    response.status === 413
      ? new RpcError(errorCodes.limitExceeded, 'The storage of the plugin for this user is full: 64 KB at most. Remove values, or keep a smaller value.')
      : response.status === 400
        ? new RpcError(errorCodes.invalidParams, 'The API refused the key or the value of the storage.')
        : new RpcError(errorCodes.internalError, `The storage of the plugin is not available: HTTP status ${response.status}.`)

  peer.handle('storage.get', async (params) => {
    const response = await sendWithToken(storageKey(params), 'GET', undefined)
    if (response.status === 404) {
      return { value: null }
    }
    if (!response.ok) {
      throw storageFailed(response)
    }
    return { value: ((await readJson(response)) as { value?: unknown } | null)?.value ?? null }
  })
  peer.handle('storage.set', async (params) => {
    const value = (params as { value?: unknown } | null)?.value
    // The storage keeps no null value: a null value removes the key.
    const response = value === undefined || value === null ? await sendWithToken(storageKey(params), 'DELETE', undefined) : await sendWithToken(storageKey(params), 'PUT', { value })
    if (!response.ok) {
      throw storageFailed(response)
    }
    return null
  })
  peer.handle('storage.remove', async (params) => {
    const response = await sendWithToken(storageKey(params), 'DELETE', undefined)
    if (!response.ok) {
      throw storageFailed(response)
    }
    return null
  })
  peer.handle('storage.keys', async () => {
    const response = await sendWithToken(storagePath, 'GET', undefined)
    if (!response.ok) {
      throw storageFailed(response)
    }
    const keys = ((await readJson(response)) as { keys?: unknown } | null)?.keys
    return { keys: Array.isArray(keys) ? keys.filter((key): key is string => typeof key === 'string') : [] }
  })

  peer.handle('ui.notify', (params) => {
    const notice = (params ?? {}) as { message?: unknown; tone?: unknown }
    if (typeof notice.message !== 'string' || notice.message.trim().length === 0) {
      throw new RpcError(errorCodes.invalidParams, 'The parameter "message" must be a text that is not empty.')
    }
    const tone = notice.tone === undefined ? 'info' : notice.tone
    if (!tones.includes(tone as NoticeTone)) {
      throw new RpcError(errorCodes.invalidParams, `The parameter "tone" must be one of ${tones.join(', ')}.`)
    }
    addNotice({ pluginId: plugin.id, pluginName: plugin.name, development: plugin.development ?? false, message: notice.message.trim(), tone: tone as NoticeTone })
    return null
  })
}
