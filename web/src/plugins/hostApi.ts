import type { HostPeer } from './connection'
import { addNotice, type NoticeTone } from './notices'
import type { PluginTokens, TokenPlugin } from './pluginTokens'
import { errorCodes } from './protocol'
import { RpcError } from './rpc'

/**
 * The methods `api.fetch` and `ui.notify` of the core. See docs/decisions/0049-plugin-sandbox.md.
 * The core sends each request of a plugin to the public API with a plugin token, so the API applies the scopes that the user accepted.
 */

const methods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']
const tones: NoticeTone[] = ['info', 'success', 'error']

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

/** Adds the methods `api.fetch` and `ui.notify` to the peer of a plugin. */
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
    let response: Response
    try {
      response = await send(path, method, request.body)
      if (response.status === 401) {
        // The token expired, or the plugin changed. The core tries one time with a new token.
        options.tokens.forget(plugin.id)
        response = await send(path, method, request.body)
      }
    } catch (error) {
      if (error instanceof RpcError) {
        throw error
      }
      throw new RpcError(errorCodes.internalError, 'The core did not send the request to the public API. Make sure that the plugin is active for the user.')
    }
    const text = await response.text()
    let body: unknown = null
    if (text) {
      try {
        body = JSON.parse(text)
      } catch {
        body = text
      }
    }
    return { status: response.status, body }
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
