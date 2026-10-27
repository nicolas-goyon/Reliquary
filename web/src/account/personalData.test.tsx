import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { mockApi, problem, session, testAccount } from '../test/mockApi'

const exportId = '0190a000-0000-7000-8000-0000000000e1'

describe('personal data', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', '/account')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  function handlers(extra: Record<string, Parameters<typeof mockApi>[0][string]>) {
    return {
      'POST /auth/refresh': () => session(),
      'GET /me': () => Response.json({ account: testAccount, externalLogins: [] }),
      'GET /me/api-keys': () => Response.json([]),
      ...extra,
    }
  }

  it('asks for an export and downloads the archive', async () => {
    let requested = false
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn<(blob: Blob) => string>(() => 'blob:zip'), revokeObjectURL: vi.fn<(url: string) => void>() }))
    const calls = mockApi(
      handlers({
        'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
        'GET /me/exports': () =>
          Response.json(
            requested
              ? [{ id: exportId, status: 'ready', requestedAt: '2026-10-27T10:00:00Z', completedAt: '2026-10-27T10:01:00Z', expiresAt: '2026-11-03T10:01:00Z', sizeBytes: 1000, downloadPath: `/api/v1/me/exports/${exportId}/archive` }]
              : [],
          ),
        'POST /me/exports': () => {
          requested = true
          return Response.json({ id: exportId, status: 'pending' }, { status: 202 })
        },
        [`GET /me/exports/${exportId}/archive`]: () => new Response('zip', { headers: { 'Content-Type': 'application/zip' } }),
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Ask for an export' }))
    await user.click(await screen.findByRole('button', { name: 'Download the archive' }))

    await waitFor(() => expect(click).toHaveBeenCalled())
    expect(calls.some((call) => call.key === `GET /me/exports/${exportId}/archive`)).toBe(true)
    click.mockRestore()
  })

  it('asks for the deletion with the password, then cancels it', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const calls = mockApi(
      handlers({
        'GET /me/exports': () => Response.json([]),
        'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
        'POST /me/deletion': ({ body }) =>
          (body as { password: string }).password === 'secret password'
            ? Response.json({ requestedAt: '2026-10-27T10:00:00Z', deleteAfter: '2026-11-03T10:00:00Z' })
            : problem(400, 'invalid_credentials'),
        'DELETE /me/deletion': () => new Response(null, { status: 204 }),
      }),
    )
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText('Password'), 'secret password')
    await user.click(screen.getByRole('button', { name: 'Delete my account' }))
    expect(await screen.findByText(/Reliquary deletes your account and all your data on/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cancel the deletion' }))

    expect(await screen.findByRole('button', { name: 'Delete my account' })).toBeInTheDocument()
    expect(calls.find((call) => call.key === 'POST /me/deletion')?.body).toEqual({ password: 'secret password' })
    expect(calls.some((call) => call.key === 'DELETE /me/deletion')).toBe(true)
  })
})
