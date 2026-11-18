import { afterEach, describe, expect, it, vi } from 'vitest'
import type { HostPeer } from './connection'
import { checkApiPath, registerHostApi, type ApiPlugin } from './hostApi'
import type { HostMethods, PluginMethods } from './methods'
import { currentNotices, maxNoticeLength, maxNoticesPerPlugin, removeNotices } from './notices'
import { PluginTokens } from './pluginTokens'
import { errorCodes } from './protocol'
import { RpcPeer, type PortLike } from './rpc'

const peers: { close(): void }[] = []

interface Sent {
  url: string
  init: RequestInit
}

/** Connects a host peer with the API methods to a fake plugin peer. The fake API answers each request with the next response. */
function setup(responses: Response[], overrides: Partial<ApiPlugin> = {}) {
  const channel = new MessageChannel()
  const host: HostPeer = new RpcPeer<HostMethods, PluginMethods>(channel.port1 as unknown as PortLike)
  const plugin = new RpcPeer<PluginMethods, HostMethods>(channel.port2 as unknown as PortLike)
  peers.push(host, plugin)
  const sent: Sent[] = []
  const fetcher = vi.fn<typeof fetch>(async (url, init) => {
    sent.push({ url: String(url), init: init ?? {} })
    return responses.shift() ?? new Response(null, { status: 204 })
  })
  let count = 0
  const tokenRequests: { path: string; body: unknown }[] = []
  const request = async <T>(path: string, options: { method?: string; body?: unknown } = {}): Promise<T> => {
    tokenRequests.push({ path, body: options.body })
    count += 1
    return { token: `rlp_token-${count}`, expiresAt: new Date(Date.now() + 600_000).toISOString() } as T
  }
  const tokens = new PluginTokens({ request })
  const data: ApiPlugin = { id: 'deck-stats', name: 'Deck statistics', development: false, ...overrides }
  registerHostApi(host, data, { tokens, fetcher })
  return { plugin, sent, tokenRequests }
}

afterEach(() => {
  for (const peer of peers.splice(0)) {
    peer.close()
  }
  removeNotices('deck-stats')
})

describe('api.fetch', () => {
  it('sends the request with a plugin token and with no cookie, and gives the status and the JSON body', async () => {
    const { plugin, sent, tokenRequests } = setup([Response.json({ items: [] }), Response.json({ id: 'deck-1' }, { status: 201 })])

    const list = await plugin.call('api.fetch', { path: '/decks?limit=10' })
    const created = await plugin.call('api.fetch', { path: '/decks', method: 'POST', body: { name: 'New deck' } })

    expect(list).toEqual({ status: 200, body: { items: [] } })
    expect(created).toEqual({ status: 201, body: { id: 'deck-1' } })
    expect(sent.map((item) => item.url)).toEqual(['/api/v1/decks?limit=10', '/api/v1/decks'])
    expect(sent[0].init).toMatchObject({ method: 'GET', credentials: 'omit', redirect: 'error' })
    expect((sent[0].init.headers as Record<string, string>).Authorization).toBe('Bearer rlp_token-1')
    expect(sent[1].init.body).toBe('{"name":"New deck"}')
    // The two requests use one token.
    expect(tokenRequests).toEqual([{ path: '/me/extensions/deck-stats/token', body: undefined }])
  })

  it('gives the error of the API to the plugin as a response, not as a failure', async () => {
    const { plugin } = setup([Response.json({ code: 'scope_missing' }, { status: 403 })])

    await expect(plugin.call('api.fetch', { path: '/decks', method: 'POST', body: {} })).resolves.toEqual({ status: 403, body: { code: 'scope_missing' } })
  })

  it('gets a new token one time after the status 401', async () => {
    const { plugin, sent, tokenRequests } = setup([new Response(null, { status: 401 }), Response.json({ ok: true })])

    const result = await plugin.call('api.fetch', { path: '/decks' })

    expect(result).toEqual({ status: 200, body: { ok: true } })
    expect(sent.map((item) => (item.init.headers as Record<string, string>).Authorization)).toEqual(['Bearer rlp_token-1', 'Bearer rlp_token-2'])
    expect(tokenRequests).toHaveLength(2)
  })

  it('asks the token of a development plugin with the scopes of its manifest', async () => {
    const { plugin, tokenRequests } = setup([Response.json({})], { development: true, permissions: ['decks:read'] })

    await plugin.call('api.fetch', { path: '/decks' })

    expect(tokenRequests).toEqual([{ path: '/me/extensions/dev%3Adeck-stats/token', body: { scopes: ['decks:read'] } }])
  })

  it.each([
    ['decks'],
    ['//evil.example.org/decks'],
    ['/decks/../auth/refresh'],
    ['/decks/%2e%2e/auth/refresh'],
    ['/decks/%2E%2E%2Fauth/refresh'],
    ['/decks\\..\\auth'],
    ['/./decks'],
    ['/decks#fragment'],
    ['/decks/%zz'],
    [42],
  ])('refuses the path %s', async (path) => {
    const { plugin, sent } = setup([])

    await expect(plugin.call('api.fetch', { path })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    expect(sent).toEqual([])
  })

  it('accepts a path with a query that contains dots and slashes', () => {
    expect(checkApiPath('/cards/search?q=o%3A%22..%22&next=/a//b')).toBe('/cards/search?q=o%3A%22..%22&next=/a//b')
  })

  it('refuses an unknown method and a body in a GET request', async () => {
    const { plugin, sent } = setup([])

    await expect(plugin.call('api.fetch', { path: '/decks', method: 'TRACE' })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    await expect(plugin.call('api.fetch', { path: '/decks', body: {} })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    expect(sent).toEqual([])
  })
})

describe('storage', () => {
  it('keeps, reads, lists and removes the values with the plugin token', async () => {
    const { plugin, sent } = setup([
      new Response(null, { status: 204 }),
      Response.json({ value: { theme: 'dark' } }),
      new Response(null, { status: 404 }),
      Response.json({ keys: ['settings'] }),
      new Response(null, { status: 204 }),
      new Response(null, { status: 204 }),
    ])

    await plugin.call('storage.set', { key: 'settings', value: { theme: 'dark' } })
    const value = await plugin.call('storage.get', { key: 'settings' })
    const missing = await plugin.call('storage.get', { key: 'last sync/é' })
    const keys = await plugin.call('storage.keys', null)
    await plugin.call('storage.remove', { key: 'settings' })
    await plugin.call('storage.set', { key: 'settings', value: null })

    expect(value).toEqual({ value: { theme: 'dark' } })
    expect(missing).toEqual({ value: null })
    expect(keys).toEqual({ keys: ['settings'] })
    expect(sent.map((item) => `${item.init.method} ${item.url}`)).toEqual([
      'PUT /api/v1/me/extensions/deck-stats/storage?key=settings',
      'GET /api/v1/me/extensions/deck-stats/storage?key=settings',
      'GET /api/v1/me/extensions/deck-stats/storage?key=last%20sync%2F%C3%A9',
      'GET /api/v1/me/extensions/deck-stats/storage',
      'DELETE /api/v1/me/extensions/deck-stats/storage?key=settings',
      'DELETE /api/v1/me/extensions/deck-stats/storage?key=settings',
    ])
    expect(sent[0].init.body).toBe('{"value":{"theme":"dark"}}')
    expect((sent[0].init.headers as Record<string, string>).Authorization).toBe('Bearer rlp_token-1')
  })

  it('gives the error code of a limit when the storage is full, and refuses a key that is not valid', async () => {
    const { plugin, sent } = setup([new Response(null, { status: 413 })])

    await expect(plugin.call('storage.set', { key: 'big', value: 'x' })).rejects.toMatchObject({ code: errorCodes.limitExceeded })
    await expect(plugin.call('storage.get', { key: '' })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    await expect(plugin.call('storage.set', { key: 'k'.repeat(129), value: 1 })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    expect(sent).toHaveLength(1)
  })

  it('uses the development ID for the storage of a development plugin', async () => {
    const { plugin, sent } = setup([Response.json({ keys: [] })], { development: true })

    await plugin.call('storage.keys', null)

    expect(sent[0].url).toBe('/api/v1/me/extensions/dev%3Adeck-stats/storage')
  })
})

describe('ui.notify', () => {
  it('adds a notice with the name of the plugin, and cuts a long message', async () => {
    const { plugin } = setup([])

    await plugin.call('ui.notify', { message: '  The sync completed.  ', tone: 'success' })
    await plugin.call('ui.notify', { message: 'x'.repeat(500) })

    const notices = currentNotices()
    expect(notices[0]).toMatchObject({ pluginId: 'deck-stats', pluginName: 'Deck statistics', message: 'The sync completed.', tone: 'success' })
    expect(notices[1]).toMatchObject({ tone: 'info' })
    expect(notices[1].message).toHaveLength(maxNoticeLength)
  })

  it('keeps only the last notices of a plugin', async () => {
    const { plugin } = setup([])

    for (let index = 0; index < maxNoticesPerPlugin + 2; index += 1) {
      await plugin.call('ui.notify', { message: `Notice ${index}` })
    }

    expect(currentNotices().map((notice) => notice.message)).toEqual(['Notice 2', 'Notice 3', 'Notice 4'])
  })

  it('refuses an empty message and an unknown tone', async () => {
    const { plugin } = setup([])

    await expect(plugin.call('ui.notify', { message: ' ' })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    await expect(plugin.call('ui.notify', { message: 'Text', tone: 'loud' })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    expect(currentNotices()).toEqual([])
  })
})
