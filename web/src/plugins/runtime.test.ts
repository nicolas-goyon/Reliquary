import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ExtensionList } from '../api/extensions'
import { startPluginRuntime } from './runtime'

function list(developmentPlugins: boolean): ExtensionList {
  return {
    coreApi: '1.1.0',
    plugins: { maxActive: 8, frameOrigin: null, developmentPlugins },
    items: [{ id: 'installed', kind: 'plugin', name: 'Installed', version: '1.0.0', coreApi: '^1.0', permissions: [], slots: [], enabled: true, compatible: true, active: true, panels: [], i18n: null }],
  }
}

const manifest = vi.fn<typeof fetch>(async () => Response.json({ id: 'dev-plugin', version: '0.0.1', entry: 'http://localhost:5173/src/main.ts' }))

describe('startPluginRuntime', () => {
  afterEach(() => {
    document.body.replaceChildren()
    vi.restoreAllMocks()
  })

  it('creates a frame for each active plugin and each development plugin', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const host = startPluginRuntime(list(true), { developmentUrls: ['http://localhost:5173/manifest.json'], fetcher: manifest, connectTimeoutMs: 60_000 })

    await vi.waitFor(() => expect(document.querySelectorAll('.plugin-frames iframe')).toHaveLength(2))

    expect(document.querySelector('iframe[data-plugin="installed"]')).toHaveAttribute('src', '/api/v1/plugins/installed/1.0.0/frame')
    expect(document.querySelector('iframe[data-plugin="dev-plugin"]')!.getAttribute('src')).toMatch(/^\/api\/v1\/plugins\/development\/frame\?entry=/)
    expect(host.isDevelopment('dev-plugin')).toBe(true)
    expect(host.isDevelopment('installed')).toBe(false)
    await host.stopAll()
    expect(document.querySelector('.plugin-frames')).toBeNull()
  })

  it('ignores the development plugins when the instance does not allow them', async () => {
    const host = startPluginRuntime(list(false), { developmentUrls: ['http://localhost:5173/manifest.json'], fetcher: manifest, connectTimeoutMs: 60_000 })

    await new Promise((resolve) => setTimeout(resolve, 20))

    expect(manifest).not.toHaveBeenCalled()
    expect(host.running).toEqual(['installed'])
    await host.stopAll()
  })
})
