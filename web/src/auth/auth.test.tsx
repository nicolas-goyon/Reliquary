import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { seriousAccessibilityProblems } from '../test/axe'
import { mockApi, problem, session, testAccount } from '../test/mockApi'

describe('authentication', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows one button for each OAuth provider, and no password form', async () => {
    window.history.replaceState(null, '', '/sign-in?returnUrl=/account')
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /auth/providers': () => Response.json({ providers: ['google', 'github'], developmentSignIn: false }),
    })
    render(<App />)

    expect(await screen.findByRole('link', { name: 'Sign in with Google' })).toHaveAttribute('href', '/api/v1/auth/external/google?returnUrl=%2Faccount&language=en')
    expect(screen.getByRole('link', { name: 'Sign in with GitHub' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Development sign-in' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Make an account' })).not.toBeInTheDocument()
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('tells the user when the instance has no sign-in service', async () => {
    window.history.replaceState(null, '', '/sign-in')
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /auth/providers': () => Response.json({ providers: [], developmentSignIn: false }),
    })
    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('This instance has no sign-in service.')
  })

  it('opens a demo account with the development sign-in and goes to the return page', async () => {
    window.history.replaceState(null, '', '/sign-in?returnUrl=/account')
    const calls = mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /auth/providers': () => Response.json({ providers: [], developmentSignIn: true }),
      'POST /auth/development/sign-in': () => session(),
      'GET /me': () => Response.json({ account: testAccount, externalLogins: ['github'] }),
      'GET /me/api-keys': () => Response.json([]),
      'GET /me/exports': () => Response.json([]),
      'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
    })
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Development sign-in' })).toBeInTheDocument()
    expect(await seriousAccessibilityProblems()).toEqual([])
    await user.click(screen.getByRole('button', { name: 'Open the account Demo' }))

    // The account page renders the core panels through the slot "account.page".
    expect(await screen.findByRole('heading', { level: 1, name: 'Your account' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/account')
    expect(screen.getByRole('heading', { name: 'Profile' })).toBeInTheDocument()
    expect(await screen.findByText('You have no API key.')).toBeInTheDocument()
    expect(await screen.findByText('github')).toBeInTheDocument()
    expect(document.querySelector('[data-slot="account.page"] [data-contribution="core.apiKeys"]')).not.toBeNull()
    expect(calls.find((call) => call.key === 'GET /me/api-keys')?.headers.Authorization).toBe('Bearer token-1')
    expect(calls.find((call) => call.key === 'POST /auth/development/sign-in')?.body).toEqual({ email: 'demo@reliquary.localhost', language: 'en' })
    expect(screen.queryByRole('heading', { name: 'Change the password' })).not.toBeInTheDocument()
  })

  it('keeps the session of a sign-in that ends before the first refresh', async () => {
    window.history.replaceState(null, '', '/sign-in?returnUrl=/account')
    // The first refresh of the page has no cookie. Its answer comes after the sign-in.
    let answerRefresh: (response: Response) => void = () => {}
    const refresh = new Promise<Response>((resolve) => (answerRefresh = resolve))
    mockApi({
      'POST /auth/refresh': () => refresh,
      'GET /auth/providers': () => Response.json({ providers: [], developmentSignIn: true }),
      'POST /auth/development/sign-in': () => session(),
      'GET /me': () => Response.json({ account: testAccount, externalLogins: [] }),
      'GET /me/api-keys': () => Response.json([]),
      'GET /me/exports': () => Response.json([]),
      'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Open the account Demo' }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Your account' })).toBeInTheDocument()

    answerRefresh(problem(401, 'session_expired'))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(window.location.pathname).toBe('/account')
    expect(screen.getByRole('heading', { level: 1, name: 'Your account' })).toBeInTheDocument()
  })

  it('opens a test account by its email address, and shows the error of an address that is not correct', async () => {
    window.history.replaceState(null, '', '/sign-in')
    const calls = mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /auth/providers': () => Response.json({ providers: [], developmentSignIn: true }),
      'POST /auth/development/sign-in': ({ body }) =>
        (body as { email: string }).email.includes('@') ? session() : problem(400, 'validation_failed', [{ field: 'email', code: 'invalid_format', message: 'The email address does not have a correct format.' }]),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText(/^Email address of a test account/), 'not an address')
    await user.click(screen.getByRole('button', { name: 'Open this account' }))
    await waitFor(() => expect(screen.getByLabelText(/^Email address of a test account/)).toHaveAccessibleDescription(/format/))

    await user.clear(screen.getByLabelText(/^Email address of a test account/))
    await user.type(screen.getByLabelText(/^Email address of a test account/), 'ana@example.org')
    await user.click(screen.getByRole('button', { name: 'Open this account' }))

    await waitFor(() => expect(window.location.pathname).toBe('/'))
    expect(calls.filter((call) => call.key === 'POST /auth/development/sign-in')).toHaveLength(2)
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

  it('shows the error of an OAuth sign-in in French', async () => {
    await i18n.changeLanguage('fr')
    window.history.replaceState(null, '', '/auth/complete?error=external_email_not_verified')
    mockApi({ 'POST /auth/refresh': () => problem(401, 'session_expired') })
    render(<App />)

    expect(await screen.findByText(/Le service externe n’a pas vérifié votre adresse e-mail/)).toBeInTheDocument()
  })
})
