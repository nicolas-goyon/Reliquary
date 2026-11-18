import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { Extension, PluginConsent } from '../api/extensions'
import i18n from '../i18n'
import { seriousAccessibilityProblems } from '../test/axe'
import { mockApi, problem, session, testAccount, type Handler } from '../test/mockApi'

function plugin(fields: Partial<Extension> = {}): Extension {
  return {
    id: 'deck-sync',
    kind: 'plugin',
    name: 'Deck sync',
    version: '1.0.0',
    coreApi: '^1.0',
    permissions: ['decks:read'],
    slots: ['deck.actions'],
    enabled: true,
    compatible: true,
    active: false,
    panels: [],
    i18n: { fr: { name: 'Synchro de decks' } },
    formats: [],
    network: ['https://api.example.com'],
    devices: [],
    sites: [],
    consent: null,
    paused: false,
    ...fields,
  }
}

const acceptedV1: PluginConsent = { version: '1.0.0', scopes: ['decks:read'], network: ['https://api.example.com'], devices: [], sites: [], acceptedAt: '2026-11-01T10:00:00Z' }

function list(...items: Extension[]) {
  return Response.json({ coreApi: '1.1.0', items, plugins: { maxActive: 8, frameOrigin: null, developmentPlugins: false } })
}

function handlers(extra: Record<string, Handler>): Record<string, Handler> {
  return {
    'POST /auth/refresh': () => session(),
    'GET /me': () => Response.json({ account: testAccount, externalLogins: [] }),
    'GET /me/api-keys': () => Response.json([]),
    'GET /me/exports': () => Response.json([]),
    'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
    'GET /service-accounts': () => Response.json([]),
    'GET /me/delegations': () => Response.json([]),
    ...extra,
  }
}

describe('plugins panel and consent dialog', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', '/account')
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('activates a plugin after the user accepts its access in plain words', async () => {
    let current = plugin()
    const calls = mockApi(
      handlers({
        'GET /extensions': () => list(current),
        'PUT /me/extensions/deck-sync': () => {
          current = plugin({ active: true, consent: acceptedV1 })
          return new Response(null, { status: 204 })
        },
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Activate Deck sync' }))
    const dialog = screen.getByRole('dialog', { name: 'Activate Deck sync' })
    expect(within(dialog).getByText('Read your decks, folders and tags')).toBeInTheDocument()
    expect(within(dialog).getByText('The site api.example.com')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus()
    expect(await seriousAccessibilityProblems()).toEqual([])

    await user.click(within(dialog).getByRole('button', { name: 'Accept and activate' }))

    expect(await screen.findByText('The plugin Deck sync is active.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(calls.find((call) => call.key === 'PUT /me/extensions/deck-sync')?.body).toEqual({
      enabled: true,
      consent: { version: '1.0.0', scopes: ['decks:read'], network: ['https://api.example.com'], devices: [], sites: [] },
    })
  })

  it('shows only the new access of a paused plugin, and closes with the Escape key', async () => {
    mockApi(
      handlers({
        'GET /extensions': () =>
          list(plugin({ version: '2.0.0', permissions: ['decks:read', 'decks:write'], devices: ['camera'], consent: acceptedV1, paused: true })),
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByText(/Paused\. The new version asks for more access\./)).toBeInTheDocument()
    const review = screen.getByRole('button', { name: 'Read the new access Deck sync' })
    await user.click(review)
    const dialog = screen.getByRole('dialog', { name: 'New access for Deck sync' })
    expect(within(dialog).getByText(/You accepted the version 1\.0\.0 of Deck sync\. The version 2\.0\.0/)).toBeInTheDocument()
    expect(within(dialog).getByText('Create, change and delete your decks, folders and tags')).toBeInTheDocument()
    expect(within(dialog).getByText('Your camera')).toBeInTheDocument()
    expect(within(dialog).queryByText('Read your decks, folders and tags')).toBeNull()
    expect(within(dialog).queryByText('The site api.example.com')).toBeNull()

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(review).toHaveFocus()
  })

  it('activates again with no dialog when the user accepted all the access, and deactivates', async () => {
    let current = plugin({ consent: acceptedV1 })
    const calls = mockApi(
      handlers({
        'GET /extensions': () => list(current),
        'PUT /me/extensions/deck-sync': ({ body }) => {
          const enabled = (body as { enabled: boolean }).enabled
          current = plugin({ consent: acceptedV1, active: enabled })
          return new Response(null, { status: 204 })
        },
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Activate Deck sync' }))
    expect(await screen.findByText('The plugin Deck sync is active.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Deactivate Deck sync' }))

    expect(await screen.findByText('The plugin Deck sync is not active.')).toBeInTheDocument()
    expect(calls.filter((call) => call.key === 'PUT /me/extensions/deck-sync').map((call) => call.body)).toEqual([{ enabled: true }, { enabled: false }])
  })

  it('shows the error when the plugin changed while the dialog was open, in French', async () => {
    await i18n.changeLanguage('fr')
    const french = { ...testAccount, language: 'fr' }
    mockApi(
      handlers({
        'POST /auth/refresh': () => session('token-1', french),
        'GET /me': () => Response.json({ account: french, externalLogins: [] }),
        'GET /extensions': () => list(plugin()),
        'PUT /me/extensions/deck-sync': () => problem(409, 'consent_outdated'),
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Activer Synchro de decks' }))
    const dialog = screen.getByRole('dialog', { name: 'Activer Synchro de decks' })
    await user.click(within(dialog).getByRole('button', { name: 'Accepter et activer' }))

    expect(await within(dialog).findByText(/Le plugin a changé pendant que la fenêtre était ouverte\./)).toBeInTheDocument()
  })

  it('does not show the panel when the instance enables no plugin', async () => {
    mockApi(handlers({}))
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Delegations' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Plugins' })).toBeNull()
  })
})
