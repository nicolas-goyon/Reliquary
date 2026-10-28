import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { mockApi, problem, session, testAccount } from '../test/mockApi'

const serviceAccount = { id: '0190a000-0000-7000-8000-0000000000a1', name: 'moxfield-sync', description: 'Synchronizes your decks with Moxfield.' }
const delegationId = '0190a000-0000-7000-8000-0000000000d1'

describe('delegations', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', '/account')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  function handlers(extra: Record<string, Parameters<typeof mockApi>[0][string]>) {
    return {
      'POST /auth/refresh': () => session(),
      'GET /me': () => Response.json({ account: testAccount, externalLogins: [] }),
      'GET /me/api-keys': () => Response.json([]),
      'GET /me/exports': () => Response.json([]),
      'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
      ...extra,
    }
  }

  it('gives a delegation to a module, then revokes it', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    let delegations: unknown[] = []
    const calls = mockApi(
      handlers({
        'GET /service-accounts': () => Response.json([serviceAccount]),
        'GET /me/delegations': () => Response.json(delegations),
        'POST /me/delegations': ({ body }) => {
          const { scopes } = body as { scopes: string[] }
          const created = { id: delegationId, serviceAccount, scopes, createdAt: '2026-10-28T10:00:00Z', expiresAt: '2026-11-27T10:00:00Z', lastUsedAt: null }
          delegations = [created]
          return Response.json(created, { status: 201 })
        },
        [`DELETE /me/delegations/${delegationId}`]: () => {
          delegations = []
          return new Response(null, { status: 204 })
        },
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByText('You have no delegation.')).toBeInTheDocument()
    await user.click(screen.getByLabelText('Create, change and delete your decks, folders and tags'))
    await user.selectOptions(screen.getByLabelText('Duration'), '30')
    await user.click(screen.getByRole('button', { name: 'Give the delegation' }))

    expect(await screen.findByText(/The module "moxfield-sync" can now act for you until/)).toBeInTheDocument()
    expect(calls.find((call) => call.key === 'POST /me/delegations')?.body).toEqual({
      serviceAccountId: serviceAccount.id,
      scopes: ['decks:read', 'decks:write'],
      expiresInDays: 30,
    })
    const row = screen.getByRole('row', { name: /moxfield-sync/ })
    expect(within(row).getByText('Read your decks, folders and tags')).toBeInTheDocument()
    expect(within(row).getByText('Never')).toBeInTheDocument()

    await user.click(within(row).getByRole('button', { name: /Revoke/ }))

    expect(await screen.findByText('You have no delegation.')).toBeInTheDocument()
    expect(calls.some((call) => call.key === `DELETE /me/delegations/${delegationId}`)).toBe(true)
  })

  it('shows the error when the account has too many delegations', async () => {
    mockApi(
      handlers({
        'GET /service-accounts': () => Response.json([serviceAccount]),
        'GET /me/delegations': () => Response.json([]),
        'POST /me/delegations': () => problem(409, 'delegation_limit'),
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Give the delegation' }))

    expect(await screen.findByText('You have the maximum number of delegations. Revoke a delegation, then try again.')).toBeInTheDocument()
  })

  it('tells when no module can receive a delegation', async () => {
    mockApi(
      handlers({
        'GET /service-accounts': () => Response.json([]),
        'GET /me/delegations': () => Response.json([]),
      }),
    )
    render(<App />)

    expect(await screen.findByText('No module of this instance can receive a delegation.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Give the delegation' })).not.toBeInTheDocument()
  })
})
