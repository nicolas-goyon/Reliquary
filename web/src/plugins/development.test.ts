import { afterEach, describe, expect, it, vi } from 'vitest'
import { pluginsChangedEvent } from './events'
import {
  DevelopmentManifestError,
  developmentFrameUrl,
  developmentPluginUrls,
  isHttpUrl,
  loadDevelopmentManifest,
  setDevelopmentPluginUrls,
} from './development'

function fetcher(response: Response | Error) {
  return vi.fn<typeof fetch>(async () => {
    if (response instanceof Error) {
      throw response
    }
    return response
  })
}

describe('development plugins', () => {
  afterEach(() => {
    localStorage.clear()
  })

  it('keeps the manifest URLs in the browser and tells the runtime', () => {
    const listener = vi.fn<() => void>()
    window.addEventListener(pluginsChangedEvent, listener)

    setDevelopmentPluginUrls(['http://localhost:5173/manifest.json', 'http://localhost:5173/manifest.json'])

    expect(developmentPluginUrls()).toEqual(['http://localhost:5173/manifest.json'])
    expect(listener).toHaveBeenCalledTimes(1)
    window.removeEventListener(pluginsChangedEvent, listener)
  })

  it('ignores a damaged list', () => {
    localStorage.setItem('reliquary.developmentPlugins', '{not json')

    expect(developmentPluginUrls()).toEqual([])
  })

  it('accepts only absolute http and https URLs with no user information', () => {
    expect(isHttpUrl('http://localhost:5173/manifest.json')).toBe(true)
    expect(isHttpUrl('https://example.org/manifest.json')).toBe(true)
    expect(isHttpUrl('/manifest.json')).toBe(false)
    expect(isHttpUrl('javascript:alert(1)')).toBe(false)
    expect(isHttpUrl('http://user:secret@localhost/manifest.json')).toBe(false)
  })

  it('reads the manifest and gives the entry point as an absolute URL', async () => {
    const manifest = { id: 'dev-plugin', name: 'Dev plugin', version: '0.0.1', entry: 'src/main.ts', network: ['https://api.example.org', 'ftp://bad'] }

    const plugin = await loadDevelopmentManifest('http://localhost:5173/manifest.json', fetcher(Response.json(manifest)))

    expect(plugin).toEqual({
      manifestUrl: 'http://localhost:5173/manifest.json',
      id: 'dev-plugin',
      name: 'Dev plugin',
      version: '0.0.1',
      entry: 'http://localhost:5173/src/main.ts',
      network: ['https://api.example.org'],
      slots: [],
    })
  })

  it('gives an error code for each problem of the manifest', async () => {
    const url = 'http://localhost:5173/manifest.json'
    const codeOf = (promise: Promise<unknown>) => promise.then(() => null, (error: DevelopmentManifestError) => error.code)

    expect(await codeOf(loadDevelopmentManifest('manifest.json', fetcher(Response.json({}))))).toBe('invalidUrl')
    expect(await codeOf(loadDevelopmentManifest(url, fetcher(new TypeError('Failed to fetch'))))).toBe('notAvailable')
    expect(await codeOf(loadDevelopmentManifest(url, fetcher(new Response('', { status: 404 }))))).toBe('notAvailable')
    expect(await codeOf(loadDevelopmentManifest(url, fetcher(Response.json({ id: 'x' }))))).toBe('missingFields')
    expect(await codeOf(loadDevelopmentManifest(url, fetcher(Response.json({ id: 'x', version: '1.0.0', entry: 'javascript:alert(1)' }))))).toBe('invalidEntry')
  })

  it('makes the address of the development frame', () => {
    const plugin = { manifestUrl: '', id: 'x', name: 'x', version: '1.0.0', entry: 'http://localhost:5173/src/main.ts', network: ['https://api.example.org'], slots: [] }

    expect(developmentFrameUrl(plugin, null)).toBe(
      '/api/v1/plugins/development/frame?entry=http%3A%2F%2Flocalhost%3A5173%2Fsrc%2Fmain.ts&network=https%3A%2F%2Fapi.example.org',
    )
    expect(developmentFrameUrl(plugin, 'https://plugins.example.org/')).toMatch(/^https:\/\/plugins\.example\.org\/api\/v1\/plugins\/development\/frame\?/)
  })
})
