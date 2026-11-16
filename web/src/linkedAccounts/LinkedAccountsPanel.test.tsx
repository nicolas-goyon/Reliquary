import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { seriousAccessibilityProblems } from '../test/axe'
import { mockApi, problem, session, testAccount, type Handler } from '../test/mockApi'
import type { LinkedAccountSite } from './api'

const apiSite: LinkedAccountSite = {
  pluginId: 'deck-sync',
  pluginName: 'Deck sync',
  pluginActive: true,
  site: 'api.example.com',
  auth: 'api-key',
  sync: { mode: 'periodic', intervalMinutes: 30 },
  link: null,
}

const nameSite: LinkedAccountSite = {
  ...apiSite,
  site: 'example.org',
  auth: 'public-username',
  sync: { mode: 'manual', intervalMinutes: null },
  link: { userName: 'Jace', hasSecret: false, secretReadable: true, state: 'synced', linkedAt: '2026-11-10T10:00:00Z', lastSyncAt: '2026-11-15T10:00:00Z', lastSyncError: null },
}

const extensions = {
  coreApi: '1.1.0',
  items: [
    {
      id: 'deck-sync',
      kind: 'plugin',
      name: 'Deck sync',
      version: '1.0.0',
      coreApi: '^1.0',
      permissions: [],
      slots: ['account.linked'],
      enabled: true,
      compatible: true,
      active: false,
      panels: [],
      i18n: { fr: { name: 'Synchro des decks' } },
      formats: [],
    },
  ],
  plugins: { maxActive: 8, frameOrigin: null, developmentPlugins: false },
}

describe('linked accounts', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', '/account')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  function handlers(extra: Record<string, Handler>): Record<string, Handler> {
    return {
      'POST /auth/refresh': () => session(),
      'GET /me': () => Response.json({ account: testAccount, externalLogins: [] }),
      'GET /me/api-keys': () => Response.json([]),
      'GET /me/delegations': () => Response.json([]),
      'GET /service-accounts': () => Response.json([]),
      'GET /me/exports': () => Response.json([]),
      'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
      'GET /extensions': () => Response.json(extensions),
      ...extra,
    }
  }

  it('does not show the panel when no plugin declares a site', async () => {
    mockApi(handlers({}))
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Delegations' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Linked accounts' })).not.toBeInTheDocument()
  })

  it('links an account with an API key, and never shows the key again', async () => {
    let items: LinkedAccountSite[] = [apiSite, nameSite]
    const calls = mockApi(
      handlers({
        'GET /me/linked-accounts': () => Response.json({ items }),
        'PUT /me/extensions/deck-sync/accounts/api.example.com': () => {
          const link = { userName: null, hasSecret: true, secretReadable: true, state: 'linked' as const, linkedAt: '2026-11-16T10:00:00Z', lastSyncAt: null, lastSyncError: null }
          items = [{ ...apiSite, link }, nameSite]
          return Response.json(items[0])
        },
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    const row = await screen.findByRole('row', { name: /api\.example\.com/ })
    expect(within(row).getByText('Deck sync')).toBeInTheDocument()
    expect(within(row).getByText('Not linked')).toBeInTheDocument()
    expect(within(screen.getByRole('row', { name: /example\.org/ })).getByText('Synced')).toBeInTheDocument()
    expect(await seriousAccessibilityProblems()).toEqual([])

    await user.click(within(row).getByRole('button', { name: 'Link api.example.com' }))
    const key = screen.getByLabelText('API key')
    expect(key).toHaveAttribute('type', 'password')
    expect(key).toHaveAttribute('autocomplete', 'off')
    expect(await seriousAccessibilityProblems()).toEqual([])
    await user.type(key, 'secret-key-1')
    await user.click(screen.getByRole('button', { name: 'Link the account' }))

    expect(await screen.findByText('Reliquary linked your account on api.example.com.')).toBeInTheDocument()
    expect(calls.find((call) => call.key === 'PUT /me/extensions/deck-sync/accounts/api.example.com')?.body).toEqual({ secret: 'secret-key-1' })
    expect(within(screen.getByRole('row', { name: /api\.example\.com/ })).getByText('Linked')).toBeInTheDocument()
    expect(document.body.textContent).not.toContain('secret-key-1')
    expect(screen.queryByLabelText('API key')).not.toBeInTheDocument()
  })

  it('links a public user name and shows the field error', async () => {
    let attempts = 0
    const calls = mockApi(
      handlers({
        'GET /me/linked-accounts': () => Response.json({ items: [{ ...nameSite, link: null }] }),
        'PUT /me/extensions/deck-sync/accounts/example.org': () => {
          attempts += 1
          return attempts === 1
            ? problem(400, 'validation_failed', [{ field: 'userName', code: 'too_long', message: 'too long' }])
            : Response.json({ ...nameSite })
        },
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Link example.org' }))
    expect(screen.queryByLabelText('API key')).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('User name'), 'Jace')
    await user.click(screen.getByRole('button', { name: 'Link the account' }))

    expect(await screen.findByText('This value is too long.')).toBeInTheDocument()
    expect(screen.getByLabelText('User name')).toHaveAttribute('aria-invalid', 'true')
    await user.click(screen.getByRole('button', { name: 'Link the account' }))
    expect(await screen.findByText('Reliquary linked your account on example.org.')).toBeInTheDocument()
    expect(calls.filter((call) => call.key === 'PUT /me/extensions/deck-sync/accounts/example.org').map((call) => call.body)).toEqual([{ userName: 'Jace' }, { userName: 'Jace' }])
  })

  it('unlinks an account after a confirmation', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    let items: LinkedAccountSite[] = [nameSite]
    const calls = mockApi(
      handlers({
        'GET /me/linked-accounts': () => Response.json({ items }),
        'DELETE /me/extensions/deck-sync/accounts/example.org': () => {
          items = [{ ...nameSite, link: null }]
          return new Response(null, { status: 204 })
        },
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Unlink example.org' }))

    expect(window.confirm).toHaveBeenCalledWith('Unlink your account on example.org? Reliquary deletes the link and its API key.')
    expect(await screen.findByText('Not linked')).toBeInTheDocument()
    expect(calls.some((call) => call.key === 'DELETE /me/extensions/deck-sync/accounts/example.org')).toBe(true)
  })

  it('shows the links that need a new link, the plugins that do not run and the sites with OAuth', async () => {
    mockApi(
      handlers({
        'GET /me/linked-accounts': () =>
          Response.json({
            items: [
              { ...apiSite, link: { ...nameSite.link!, hasSecret: true, secretReadable: false, state: 'linked' } },
              { ...apiSite, site: 'oauth.example.net', auth: 'oauth2-pkce' },
              { ...nameSite, pluginId: 'old-plugin', pluginName: null, pluginActive: false, sync: null },
            ],
          }),
      }),
    )
    render(<App />)

    expect(within(await screen.findByRole('row', { name: /api\.example\.com/ })).getByText('Link the account again')).toBeInTheDocument()
    const oauth = screen.getByRole('row', { name: /oauth\.example\.net/ })
    expect(within(oauth).getByText('This sign-in method is not available yet.')).toBeInTheDocument()
    expect(within(oauth).queryByRole('button')).not.toBeInTheDocument()
    const old = screen.getByRole('row', { name: /old-plugin/ })
    expect(within(old).getByText('The plugin does not run. You can only unlink the account.')).toBeInTheDocument()
    expect(within(old).queryByRole('button', { name: /Change/ })).not.toBeInTheDocument()
    expect(within(old).getByRole('button', { name: 'Unlink example.org' })).toBeInTheDocument()
  })

  it('shows the panel in French with the translated name of the plugin', async () => {
    await i18n.changeLanguage('fr')
    const account = { ...testAccount, language: 'fr' }
    mockApi(
      handlers({
        'POST /auth/refresh': () => session('token-1', account),
        'GET /me': () => Response.json({ account, externalLogins: [] }),
        'GET /me/linked-accounts': () => Response.json({ items: [apiSite] }),
      }),
    )
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Comptes liés' })).toBeInTheDocument()
    expect(await screen.findByText('Synchro des decks')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Lier api.example.com' })).toBeInTheDocument()
    expect(await seriousAccessibilityProblems()).toEqual([])
  })
})
