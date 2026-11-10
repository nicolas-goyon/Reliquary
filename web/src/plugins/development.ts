/**
 * The development mode: on a development instance, a user loads a plugin from the URL of its plugin manifest,
 * for example `http://localhost:5173/manifest.json`. See docs/architecture/extensions.md.
 * The browser keeps the URLs. The core does not check these plugins.
 */

import { notifyPluginsChanged } from './events'

const storageKey = 'reliquary.developmentPlugins'

/** Gets the manifest URLs of the development plugins of this browser. */
export function developmentPluginUrls(): string[] {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(storageKey) ?? '[]')
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
  } catch {
    return []
  }
}

/** Replaces the manifest URLs of the development plugins, and tells the plugin runtime. */
export function setDevelopmentPluginUrls(urls: string[]): void {
  try {
    localStorage.setItem(storageKey, JSON.stringify([...new Set(urls)]))
  } catch {
    // The browser blocks the storage. The list stays the same.
  }
  notifyPluginsChanged()
}

/** Tells whether a text is an absolute http or https URL. */
export function isHttpUrl(text: string): boolean {
  try {
    const url = new URL(text)
    return (url.protocol === 'http:' || url.protocol === 'https:') && !url.username && !url.password
  } catch {
    return false
  }
}

/** A development plugin, from its plugin manifest. */
export interface DevelopmentPlugin {
  /** The URL of the plugin manifest. */
  manifestUrl: string
  id: string
  name: string
  version: string
  /** The absolute URL of the entry point. */
  entry: string
  /** The origins of the network list of the plugin manifest. */
  network: string[]
}

/** The causes of a DevelopmentManifestError. The interface shows a text for each code. */
export type DevelopmentManifestErrorCode = 'invalidUrl' | 'notAvailable' | 'missingFields' | 'invalidEntry'

/** The plugin manifest at the URL is not available or not correct. */
export class DevelopmentManifestError extends Error {
  readonly code: DevelopmentManifestErrorCode

  constructor(code: DevelopmentManifestErrorCode, message: string) {
    super(message)
    this.name = 'DevelopmentManifestError'
    this.code = code
  }
}

/**
 * Reads the plugin manifest of a development plugin.
 * The development server of the plugin must accept requests from the origin of the instance (CORS).
 * @throws DevelopmentManifestError when the manifest is not available, or when a necessary field is missing.
 */
export async function loadDevelopmentManifest(manifestUrl: string, fetcher: typeof fetch = fetch): Promise<DevelopmentPlugin> {
  if (!isHttpUrl(manifestUrl)) {
    throw new DevelopmentManifestError('invalidUrl', 'The URL of the plugin manifest must be an absolute http or https URL.')
  }
  let manifest: Record<string, unknown>
  try {
    const response = await fetcher(manifestUrl, { credentials: 'omit', cache: 'no-store' })
    if (!response.ok) {
      throw new DevelopmentManifestError('notAvailable', `The plugin manifest did not load: HTTP status ${response.status}.`)
    }
    manifest = (await response.json()) as Record<string, unknown>
  } catch (error) {
    throw error instanceof DevelopmentManifestError
      ? error
      : new DevelopmentManifestError('notAvailable', 'The plugin manifest did not load. Make sure that the server of the plugin runs and accepts requests from this site.')
  }
  const { id, name, version, entry, network } = manifest ?? {}
  if (typeof id !== 'string' || typeof version !== 'string' || typeof entry !== 'string') {
    throw new DevelopmentManifestError('missingFields', 'The plugin manifest must have the fields "id", "version" and "entry".')
  }
  const entryUrl = new URL(entry, manifestUrl).href
  if (!isHttpUrl(entryUrl)) {
    throw new DevelopmentManifestError('invalidEntry', 'The entry point must be an http or https URL.')
  }
  return {
    manifestUrl,
    id,
    name: typeof name === 'string' ? name : id,
    version,
    entry: entryUrl,
    network: Array.isArray(network) ? network.filter((item): item is string => typeof item === 'string' && isHttpUrl(item)) : [],
  }
}

/** Gets the address of the frame of a development plugin. The API sends it only on a development instance. */
export function developmentFrameUrl(plugin: DevelopmentPlugin, frameOrigin: string | null): string {
  const query = new URLSearchParams({ entry: plugin.entry })
  for (const origin of plugin.network) {
    query.append('network', origin)
  }
  const path = `/api/v1/plugins/development/frame?${query}`
  return frameOrigin ? `${frameOrigin.replace(/\/+$/, '')}${path}` : path
}
