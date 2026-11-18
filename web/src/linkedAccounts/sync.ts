import { ApiError, apiRequest, type RequestOptions } from '../api/client'
import type { HostPeer } from '../plugins/connection'
import { errorCodes } from '../plugins/protocol'
import { PluginTokens } from '../plugins/pluginTokens'
import { RpcError } from '../plugins/rpc'
import { linkPath, siteKey, type LinkedAccountList, type LinkedAccountSite } from './api'
import { notifyLinkedAccountsChanged, setSyncController, type SyncController } from './syncState'

/**
 * The sync of the linked accounts and the methods `accounts.list` and `accounts.secret` of the core.
 * The plugin runtime loads this module. See docs/decisions/0051-linked-accounts.md.
 */

/** The minimum interval of a periodic sync, in minutes. */
export const minIntervalMinutes = 15

/** The interval of a periodic sync when the plugin manifest gives no interval, in minutes. */
export const defaultIntervalMinutes = 60

/** The time limit of a sync, in milliseconds. */
export const syncTimeoutMs = 60_000

/** A sync that started less than this time before the planned time counts as on time, in milliseconds. */
const scheduleSlackMs = 60_000

type SessionRequest = <T>(path: string, options?: Omit<RequestOptions, 'accessToken'>) => Promise<T>

export interface LinkedAccountSyncOptions {
  /** Sends a request with the web session of the user. */
  request: SessionRequest
  /** Sends a request with a plugin token. The default is `apiRequest`. */
  pluginRequest?: typeof apiRequest
  /** The plugin tokens of the running plugins. The default is a new store that uses `request`. */
  tokens?: PluginTokens
  /** The clock, in milliseconds. */
  now?: () => number
}

/** The linked account that the core gives to the plugin, without the secret. */
function toPluginAccount(item: LinkedAccountSite) {
  return {
    site: item.site,
    userName: item.link?.userName ?? null,
    connected: Boolean(item.link?.secretReadable),
    lastSyncAt: item.link?.lastSyncAt ?? null,
  }
}

function intervalMs(item: LinkedAccountSite): number {
  return Math.max(minIntervalMinutes, item.sync?.intervalMinutes ?? defaultIntervalMinutes) * 60_000
}

/**
 * Runs the syncs of the linked accounts of the running plugins:
 * - `on-open`: one time when the frame of the plugin starts,
 * - `periodic`: when the frame starts and the last sync is old, then after each interval,
 * - all modes: when the user selects Sync, and after a link.
 */
export class LinkedAccountSync implements SyncController {
  private readonly options: LinkedAccountSyncOptions
  private readonly peers = new Map<string, HostPeer>()
  private readonly timers = new Map<string, ReturnType<typeof setInterval>[]>()
  private readonly active = new Map<string, Promise<void>>()
  private readonly tokens: PluginTokens

  constructor(options: LinkedAccountSyncOptions) {
    this.options = options
    this.tokens = options.tokens ?? new PluginTokens({ request: options.request, now: options.now })
  }

  private now(): number {
    return this.options.now?.() ?? Date.now()
  }

  /** Adds the methods of the linked accounts to the peer of a plugin, then starts the syncs of the plugin. */
  attach(peer: HostPeer, pluginId: string): void {
    this.detach(pluginId)
    this.peers.set(pluginId, peer)
    peer.handle('accounts.list', async () => ({ accounts: (await this.sites(pluginId)).map(toPluginAccount) }))
    peer.handle('accounts.secret', async (params) => this.secret(pluginId, params))
    notifyLinkedAccountsChanged()
    this.schedule(pluginId).catch((error: unknown) => console.warn(`The core did not start the syncs of the plugin "${pluginId}".`, error))
  }

  /** Stops the syncs of a plugin, for example when its frame stops. */
  detach(pluginId: string): void {
    for (const timer of this.timers.get(pluginId) ?? []) {
      clearInterval(timer)
    }
    this.timers.delete(pluginId)
    this.tokens.forget(pluginId)
    if (this.peers.delete(pluginId)) {
      notifyLinkedAccountsChanged()
    }
  }

  /** Stops all the syncs. */
  stop(): void {
    for (const pluginId of [...this.peers.keys()]) {
      this.detach(pluginId)
    }
  }

  isRunning(pluginId: string): boolean {
    return this.peers.has(pluginId)
  }

  /** Runs a sync now. A second call for the same site during a sync gets the same promise. */
  sync(pluginId: string, site: string): Promise<void> {
    const key = siteKey({ pluginId, site })
    const current = this.active.get(key)
    if (current) {
      return current
    }
    const run = this.run(pluginId, site).finally(() => this.active.delete(key))
    this.active.set(key, run)
    return run
  }

  private async run(pluginId: string, site: string): Promise<void> {
    const peer = this.peers.get(pluginId)
    if (!peer) {
      throw new Error(`The plugin "${pluginId}" does not run.`)
    }
    const item = (await this.sites(pluginId)).find((entry) => entry.site === site)
    if (!item?.link) {
      throw new Error(`The user has no linked account on ${site} for the plugin "${pluginId}".`)
    }
    let error: string | null = null
    try {
      await peer.call('account.sync', { account: toPluginAccount(item) }, { timeoutMs: syncTimeoutMs })
    } catch (caught) {
      error =
        caught instanceof RpcError && caught.code === errorCodes.timeout
          ? `The plugin did not complete the sync in ${syncTimeoutMs / 1000} seconds.`
          : caught instanceof Error
            ? caught.message
            : String(caught)
    }
    await this.options.request(`${linkPath(pluginId, site)}/sync`, { method: 'POST', body: error === null ? { succeeded: true } : { succeeded: false, error } })
    notifyLinkedAccountsChanged()
  }

  /** Gets the sites of a plugin that runs for the user. */
  private async sites(pluginId: string): Promise<LinkedAccountSite[]> {
    const list = await this.options.request<LinkedAccountList>('/me/linked-accounts')
    return list.items.filter((item) => item.pluginId === pluginId && item.pluginActive)
  }

  private async schedule(pluginId: string): Promise<void> {
    const sites = await this.sites(pluginId)
    if (!this.peers.has(pluginId)) {
      return
    }
    const timers: ReturnType<typeof setInterval>[] = []
    for (const item of sites) {
      const mode = item.sync?.mode
      if (mode === 'on-open' && item.link) {
        this.background(pluginId, item.site)
      }
      if (mode === 'periodic') {
        const interval = intervalMs(item)
        if (item.link && this.isDue(item, interval)) {
          this.background(pluginId, item.site)
        }
        timers.push(setInterval(() => void this.syncWhenDue(pluginId, item.site, interval), interval))
      }
    }
    this.timers.set(pluginId, timers)
  }

  /** Runs a periodic sync when the last sync is older than the interval. Another tab can run the sync too, so the core reads the last sync again. */
  private async syncWhenDue(pluginId: string, site: string, interval: number): Promise<void> {
    try {
      const item = (await this.sites(pluginId)).find((entry) => entry.site === site)
      if (item?.link && this.isDue(item, interval)) {
        await this.sync(pluginId, site)
      }
    } catch (error) {
      console.warn(`The periodic sync of ${site} for the plugin "${pluginId}" failed.`, error)
    }
  }

  private isDue(item: LinkedAccountSite, interval: number): boolean {
    const last = item.link?.lastSyncAt ? Date.parse(item.link.lastSyncAt) : Number.NaN
    return Number.isNaN(last) || this.now() - last >= interval - scheduleSlackMs
  }

  private background(pluginId: string, site: string): void {
    this.sync(pluginId, site).catch((error: unknown) => console.warn(`The sync of ${site} for the plugin "${pluginId}" failed.`, error))
  }

  /** Gives the secret of a site to the plugin of the link, with a plugin token of this plugin. */
  private async secret(pluginId: string, params: unknown) {
    const site = (params as { site?: unknown } | null)?.site
    if (typeof site !== 'string' || site.length === 0) {
      throw new RpcError(errorCodes.invalidParams, 'The parameter "site" must be the host name of a site of the plugin manifest.')
    }
    try {
      const token = await this.tokens.get({ id: pluginId })
      return await (this.options.pluginRequest ?? apiRequest)(`${linkPath(pluginId, site)}/secret`, { accessToken: token })
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        throw new RpcError(errorCodes.invalidParams, `The user has no linked account on ${site} for this plugin.`)
      }
      if (error instanceof ApiError) {
        throw new RpcError(errorCodes.internalError, `The core did not get the secret: ${error.code}.`, { code: error.code })
      }
      throw error
    }
  }
}

/** Creates the sync of the linked accounts, and gives it to the Linked accounts panel. */
export function startLinkedAccountSync(options: LinkedAccountSyncOptions): LinkedAccountSync {
  const sync = new LinkedAccountSync(options)
  setSyncController(sync)
  return sync
}

/** Stops the syncs and removes the controller of the panel. */
export function stopLinkedAccountSync(sync: LinkedAccountSync): void {
  sync.stop()
  setSyncController(null)
}
