import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import type { HostPeer } from '../plugins/connection'
import type { HostMethods, PluginMethods } from '../plugins/methods'
import { errorCodes } from '../plugins/protocol'
import { RpcError, RpcPeer, type PortLike } from '../plugins/rpc'
import type { LinkedAccountSite } from './api'
import { LinkedAccountSync } from './sync'
import { getSyncController, setSyncController, syncStateVersion } from './syncState'

const now = Date.parse('2026-11-16T12:00:00Z')
const minute = 60_000

function site(overrides: Partial<LinkedAccountSite> = {}): LinkedAccountSite {
  return {
    pluginId: 'deck-sync',
    pluginName: 'Deck sync',
    pluginActive: true,
    site: 'example.org',
    auth: 'public-username',
    sync: { mode: 'manual', intervalMinutes: null },
    link: { userName: 'Jace', hasSecret: false, secretReadable: true, state: 'linked', linkedAt: '2026-11-10T10:00:00Z', lastSyncAt: null, lastSyncError: null },
    ...overrides,
  }
}

const peers: { close(): void }[] = []

interface Call {
  path: string
  method: string
  body: unknown
}

function setup(items: LinkedAccountSite[], syncAccount: (site: string) => void | Promise<void> = () => undefined) {
  const calls: Call[] = []
  let clock = now
  const request = vi.fn<(path: string, options?: { method?: string; body?: unknown }) => Promise<unknown>>(async (path, options = {}) => {
    calls.push({ path, method: options.method ?? 'GET', body: options.body })
    if (path === '/me/linked-accounts') {
      return { items }
    }
    if (path === '/me/extensions/deck-sync/token') {
      return { token: `rlp_token_${calls.filter((call) => call.path.endsWith('/token')).length}`, expiresAt: new Date(clock + 10 * minute).toISOString() }
    }
    if (path.endsWith('/sync')) {
      return undefined
    }
    throw new Error(`No answer for ${path}.`)
  }) as unknown as <T>(path: string) => Promise<T>
  const pluginRequest = vi.fn<(path: string, options?: { accessToken?: string }) => Promise<unknown>>(async (path, options = {}) => {
    calls.push({ path, method: `GET with ${options.accessToken}`, body: undefined })
    if (path.includes('missing.example.com')) {
      throw new ApiError(404, 'not_found', 'no link')
    }
    return { site: 'api.example.com', auth: 'api-key', userName: null, secret: 'secret-key-1' }
  }) as unknown as typeof import('../api/client').apiRequest
  const sync = new LinkedAccountSync({ request, pluginRequest, now: () => clock })
  const channel = new MessageChannel()
  const host: HostPeer = new RpcPeer<HostMethods, PluginMethods>(channel.port1 as unknown as PortLike)
  const frame = new RpcPeer<PluginMethods, HostMethods>(channel.port2 as unknown as PortLike)
  peers.push(host, frame)
  const synced: string[] = []
  frame.handle('account.sync', async ({ account }) => {
    synced.push(account.site)
    await syncAccount(account.site)
    return null
  })
  return {
    sync,
    host,
    frame,
    calls,
    synced,
    syncCalls: () => calls.filter((call) => call.path.endsWith('/sync')),
    advance(ms: number) {
      clock += ms
      vi.advanceTimersByTime(ms)
    },
  }
}

async function settle() {
  for (let index = 0; index < 20; index += 1) {
    await new Promise((resolve) => setTimeout(resolve, 0))
  }
}

afterEach(() => {
  for (const peer of peers.splice(0)) {
    peer.close()
  }
  setSyncController(null)
  vi.useRealTimers()
})

describe('sync of the linked accounts', () => {
  it('syncs the sites "on-open" when the plugin starts, and records the result', async () => {
    const { sync, host, synced, syncCalls } = setup([
      site({ sync: { mode: 'on-open', intervalMinutes: null } }),
      site({ site: 'manual.example.com' }),
      site({ site: 'unlinked.example.com', sync: { mode: 'on-open', intervalMinutes: null }, link: null }),
      site({ pluginId: 'other', site: 'other.example.com', sync: { mode: 'on-open', intervalMinutes: null } }),
    ])
    const before = syncStateVersion()

    sync.attach(host, 'deck-sync')
    await settle()

    expect(sync.isRunning('deck-sync')).toBe(true)
    expect(syncStateVersion()).toBeGreaterThan(before)
    expect(synced).toEqual(['example.org'])
    expect(syncCalls()).toEqual([{ path: '/me/extensions/deck-sync/accounts/example.org/sync', method: 'POST', body: { succeeded: true } }])
  })

  it('records the message of a failed sync', async () => {
    const { sync, host, syncCalls } = setup([site()], () => {
      throw new Error('The site answered 503.')
    })
    sync.attach(host, 'deck-sync')

    await sync.sync('deck-sync', 'example.org')

    expect(syncCalls().map((call) => call.body)).toEqual([{ succeeded: false, error: 'The site answered 503.' }])
  })

  it('runs one sync at a time for a site', async () => {
    let finish: () => void = () => undefined
    const { sync, host, synced } = setup([site()], () => new Promise<void>((resolve) => (finish = resolve)))
    sync.attach(host, 'deck-sync')

    const first = sync.sync('deck-sync', 'example.org')
    const second = sync.sync('deck-sync', 'example.org')
    await settle()
    finish()
    await Promise.all([first, second])

    expect(synced).toEqual(['example.org'])
  })

  it('syncs a periodic site after each interval, and never more often than 15 minutes', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const items = [
      site({ sync: { mode: 'periodic', intervalMinutes: 30 }, link: { ...site().link!, lastSyncAt: new Date(now - 10 * minute).toISOString() } }),
      site({ site: 'fast.example.com', sync: { mode: 'periodic', intervalMinutes: 1 } }),
    ]
    const { sync, host, synced, advance } = setup(items)

    sync.attach(host, 'deck-sync')
    await settle()
    // The last sync of example.org is recent. The other site has no sync yet.
    expect(synced).toEqual(['fast.example.com'])
    items[1] = { ...items[1], link: { ...items[1].link!, lastSyncAt: new Date(now).toISOString() } }

    advance(14 * minute)
    await settle()
    expect(synced).toEqual(['fast.example.com'])

    advance(1 * minute)
    await settle()
    expect(synced).toEqual(['fast.example.com', 'fast.example.com'])

    advance(15 * minute)
    await settle()
    expect(synced.filter((entry) => entry === 'example.org')).toEqual(['example.org'])

    sync.detach('deck-sync')
    advance(60 * minute)
    await settle()
    expect(synced).toHaveLength(4)
    expect(sync.isRunning('deck-sync')).toBe(false)
  })

  it('does not run a periodic sync when another tab ran it', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const items = [site({ sync: { mode: 'periodic', intervalMinutes: 30 }, link: { ...site().link!, lastSyncAt: new Date(now).toISOString() } })]
    const { sync, host, synced, advance } = setup(items)
    sync.attach(host, 'deck-sync')
    await settle()

    items[0] = { ...items[0], link: { ...items[0].link!, lastSyncAt: new Date(now + 20 * minute).toISOString() } }
    advance(30 * minute)
    await settle()

    expect(synced).toEqual([])
  })

  it('gives the linked accounts of the plugin without secrets', async () => {
    const { sync, host, frame } = setup([site({ link: { ...site().link!, hasSecret: true } }), site({ pluginId: 'other' }), site({ site: 'old.example.com', pluginActive: false })])
    sync.attach(host, 'deck-sync')

    const result = await frame.call('accounts.list', null)

    expect(result).toEqual({ accounts: [{ site: 'example.org', userName: 'Jace', connected: true, lastSyncAt: null }] })
  })

  it('gives the secret to the plugin with a plugin token, and keeps the token', async () => {
    const { sync, host, frame, calls } = setup([])
    sync.attach(host, 'deck-sync')

    const first = await frame.call('accounts.secret', { site: 'api.example.com' })
    await frame.call('accounts.secret', { site: 'api.example.com' })

    expect(first).toEqual({ site: 'api.example.com', auth: 'api-key', userName: null, secret: 'secret-key-1' })
    expect(calls.filter((call) => call.path.endsWith('/token'))).toHaveLength(1)
    expect(calls.filter((call) => call.path.endsWith('/secret')).map((call) => call.method)).toEqual(['GET with rlp_token_1', 'GET with rlp_token_1'])
    await expect(frame.call('accounts.secret', { site: 'missing.example.com' })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    await expect(frame.call('accounts.secret', { site: 42 })).rejects.toBeInstanceOf(RpcError)
  })

  it('gives the panel a controller while the runtime runs', async () => {
    const { startLinkedAccountSync, stopLinkedAccountSync } = await import('./sync')
    const request = vi.fn<() => Promise<never>>() as unknown as <T>(path: string) => Promise<T>

    const sync = startLinkedAccountSync({ request })
    expect(getSyncController()).toBe(sync)
    stopLinkedAccountSync(sync)
    expect(getSyncController()).toBeNull()
  })
})
