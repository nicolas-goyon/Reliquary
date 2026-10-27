import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { mockApi, problem, session, testAccount } from '../test/mockApi'

describe('authentication', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('signs in with a password and opens the return page', async () => {
    window.history.replaceState(null, '', '/sign-in?returnUrl=/account')
    const calls = mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /auth/providers': () => Response.json({ providers: ['github'] }),
      'POST /auth/sign-in': ({ body }) => ((body as { password: string }).password === 'correct horse battery' ? session() : problem(401, 'invalid_credentials')),
      'GET /me': () => Response.json({ account: testAccount, externalLogins: ['github'] }),
      'GET /me/api-keys': () => Response.json([]),
      'GET /me/exports': () => Response.json([]),
      'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
    })
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByRole('link', { name: 'GitHub' })).toHaveAttribute(
      'href',
      '/api/v1/auth/external/github?returnUrl=%2Faccount&language=en',
    )
    await user.type(screen.getByLabelText('Email address'), 'ana@example.org')
    await user.type(screen.getByLabelText('Password'), 'wrong password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('The email address or the password is not correct.')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Password'))
    await user.type(screen.getByLabelText('Password'), 'correct horse battery')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    // The account page renders the core panels through the slot "account.page".
    expect(await screen.findByRole('heading', { level: 1, name: 'Your account' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/account')
    expect(screen.getByRole('heading', { name: 'Profile' })).toBeInTheDocument()
    expect(await screen.findByText('You have no API key.')).toBeInTheDocument()
    expect(await screen.findByText('github')).toBeInTheDocument()
    expect(document.querySelector('[data-slot="account.page"] [data-contribution="core.apiKeys"]')).not.toBeNull()
    expect(calls.find((call) => call.key === 'GET /me/api-keys')?.headers.Authorization).toBe('Bearer token-1')
  })

  it('gets a new access token and sends the request again when the token expired', async () => {
    window.history.replaceState(null, '', '/account')
    let refreshes = 0
    let keyRequests = 0
    const calls = mockApi({
      'POST /auth/refresh': () => session(`token-${++refreshes}`),
      'GET /me': () => Response.json({ account: testAccount, externalLogins: [] }),
      'GET /me/exports': () => Response.json([]),
      'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
      'GET /me/api-keys': ({ headers }) => {
        keyRequests += 1
        return headers.Authorization === 'Bearer token-1' ? problem(401, 'invalid_authentication') : Response.json([])
      },
    })
    render(<App />)

    expect(await screen.findByText('You have no API key.')).toBeInTheDocument()
    expect(refreshes).toBe(2)
    expect(keyRequests).toBe(2)
    expect(calls.filter((call) => call.key === 'GET /me/api-keys').at(-1)?.headers.Authorization).toBe('Bearer token-2')
  })

  it('sends an anonymous user from the account page to the sign-in page', async () => {
    window.history.replaceState(null, '', '/account')
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /auth/providers': () => Response.json({ providers: [] }),
    })
    render(<App />)

    await waitFor(() => expect(window.location.pathname).toBe('/sign-in'))
    expect(window.location.search).toBe('?returnUrl=%2Faccount')
  })

  it('shows the field errors of the registration', async () => {
    window.history.replaceState(null, '', '/register')
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'POST /auth/register': () =>
        problem(400, 'validation_failed', [{ field: 'password', code: 'too_short', message: 'The password must have 10 characters or more.' }]),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText('Email address'), 'ana@example.org')
    await user.type(screen.getByLabelText('Public name'), 'Ana')
    await user.type(screen.getByLabelText('Password'), 'short')
    await user.click(screen.getByRole('button', { name: 'Make the account' }))

    expect(await screen.findByText('This value is too short.')).toBeInTheDocument()
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'true')
  })

  it('checks the email address with the token of the link', async () => {
    window.history.replaceState(null, '', '/check-email?token=abc')
    const calls = mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'POST /auth/email-check': () => new Response(null, { status: 204 }),
    })
    render(<App />)

    expect(await screen.findByText('We checked your email address. You can sign in now.')).toBeInTheDocument()
    expect(calls.filter((call) => call.key === 'POST /auth/email-check')).toEqual([
      { key: 'POST /auth/email-check', body: { token: 'abc' }, headers: expect.any(Object) },
    ])
  })

  it('shows the error of an OAuth sign-in in French', async () => {
    await i18n.changeLanguage('fr')
    window.history.replaceState(null, '', '/auth/complete?error=external_email_not_verified')
    mockApi({ 'POST /auth/refresh': () => problem(401, 'session_expired') })
    render(<App />)

    expect(await screen.findByText(/Le service externe n’a pas vérifié votre adresse e-mail/)).toBeInTheDocument()
  })
})
