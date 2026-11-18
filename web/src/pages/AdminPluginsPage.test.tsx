import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { seriousAccessibilityProblems } from '../test/axe'
import { mockApi, problem, session, testAccount, type Handler } from '../test/mockApi'

const administrator = { ...testAccount, isAdministrator: true }

const catalog = {
  configured: true,
  serial: 4,
  expiresAt: '2026-12-01T00:00:00Z',
  checkedAt: '2026-11-18T08:00:00Z',
  plugins: [
    { id: 'deck-stats', name: 'Deck statistics', description: 'Charts for a deck.', latestVersion: '1.2.0', versions: [{ version: '1.2.0', revocationReason: null }] },
    { id: 'price-watch', name: 'Price watch', description: null, latestVersion: '2.0.0', versions: [{ version: '2.0.0', revocationReason: null }] },
  ],
}

function extension(id: string, name: string, version: string, enabled: boolean) {
  return { extension: { id, kind: 'plugin', name, version, enabled, compatible: true }, revocationReason: null }
}

function handlers(account: Record<string, unknown>, extra: Record<string, Handler>): Record<string, Handler> {
  return {
    'POST /auth/refresh': () => session('token-1', account),
    'GET /me': () => Response.json({ account, externalLogins: [] }),
    'GET /extensions': () => Response.json({ coreApi: '1.1.0', items: [], plugins: { maxActive: 8, frameOrigin: null, developmentPlugins: false } }),
    ...extra,
  }
}

describe('plugin page of the administrators', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', '/admin/plugins')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('installs and enables a plugin of the catalog, updates a plugin and disables it', async () => {
    let installed = [extension('price-watch', 'Price watch', '1.0.0', true)]
    const calls = mockApi(
      handlers(administrator, {
        'GET /admin/plugin-catalog': () => Response.json(catalog),
        'GET /admin/extensions': () => Response.json(installed),
        'POST /admin/plugin-catalog/deck-stats/1.2.0/install': () => {
          installed = [...installed, extension('deck-stats', 'Deck statistics', '1.2.0', false)]
          return Response.json(installed[1], { status: 201 })
        },
        'PATCH /admin/extensions/deck-stats': () => {
          installed = installed.map((item) => (item.extension.id === 'deck-stats' ? extension('deck-stats', 'Deck statistics', '1.2.0', true) : item))
          return Response.json(installed[1])
        },
        'POST /admin/plugin-catalog/price-watch/2.0.0/install': () => {
          installed = installed.map((item) => (item.extension.id === 'price-watch' ? extension('price-watch', 'Price watch', '2.0.0', true) : item))
          return Response.json(installed[0])
        },
        'PATCH /admin/extensions/price-watch': () => {
          installed = installed.map((item) => (item.extension.id === 'price-watch' ? extension('price-watch', 'Price watch', '2.0.0', false) : item))
          return Response.json(installed[0])
        },
      }),
    )
    render(<App />)
    const user = userEvent.setup()

    expect(await screen.findByRole('heading', { name: 'Plugins of the instance' })).toBeInTheDocument()
    expect(await screen.findByText(/^Index 4\. Last sync: /)).toBeInTheDocument()
    const stats = () => screen.getByRole('row', { name: /Deck statistics/ })
    const prices = () => screen.getByRole('row', { name: /Price watch/ })
    expect(within(stats()).getByText('Not installed.')).toBeInTheDocument()
    expect(within(prices()).getByText('Version 1.0.0. Enabled for the instance.')).toBeInTheDocument()
    expect(await seriousAccessibilityProblems()).toEqual([])

    await user.click(within(stats()).getByRole('button', { name: 'Install and enable Deck statistics' }))
    expect(await screen.findByText('The plugin Deck statistics (version 1.2.0) is enabled for the instance.')).toBeInTheDocument()
    expect(within(stats()).getByText('Version 1.2.0. Enabled for the instance.')).toBeInTheDocument()

    await user.click(within(prices()).getByRole('button', { name: 'Update to 2.0.0 Price watch' }))
    expect(await screen.findByText('The plugin Price watch has the version 2.0.0.')).toBeInTheDocument()
    expect(within(prices()).queryByRole('button', { name: /Update/ })).not.toBeInTheDocument()

    await user.click(within(prices()).getByRole('button', { name: 'Disable Price watch' }))
    expect(await within(prices()).findByText('Version 2.0.0. Installed, not enabled.')).toBeInTheDocument()
    expect(calls.filter((call) => call.key.startsWith('PATCH')).map((call) => call.body)).toEqual([{ enabled: true }, { enabled: false }])
  })

  it('syncs the catalog and shows the error of a sync that failed', async () => {
    let fail = false
    mockApi(
      handlers(administrator, {
        'GET /admin/plugin-catalog': () => Response.json({ ...catalog, plugins: [] }),
        'GET /admin/extensions': () => Response.json([]),
        'POST /admin/plugin-catalog/sync': () => (fail ? problem(502, 'catalog_sync_failed') : Response.json({ serial: 5, addedVersions: ['deck-stats 1.3.0'], revokedVersions: [] })),
      }),
    )
    render(<App />)
    const user = userEvent.setup()

    expect(await screen.findByText('The instance has no plugin. Sync the catalog to get its plugins.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Sync the catalog now' }))
    expect(await screen.findByText('The catalog has the index 5. New plugin versions: 1. New revoked versions: 0.')).toBeInTheDocument()

    fail = true
    await user.click(screen.getByRole('button', { name: 'Sync the catalog now' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The sync of the plugin catalog failed.')
  })

  it('refuses a user who is not an administrator, in French', async () => {
    await i18n.changeLanguage('fr')
    const account = { ...testAccount, language: 'fr' }
    mockApi(handlers(account, {}))
    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Seuls les administrateurs peuvent ouvrir cette page.')
    expect(screen.queryByRole('link', { name: 'Plugins' })).not.toBeInTheDocument()
  })
})
