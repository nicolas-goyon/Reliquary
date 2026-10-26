import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from './i18n'
import { App } from './App'
import { mockApi, problem } from './test/mockApi'

describe('App', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', '/')
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /status': () => Response.json({ name: 'Reliquary', version: '0.1.0', components: [], coreApi: '1.0.0' }),
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the home page in English', async () => {
    render(<App />)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Manage your decks and your collection.')
    expect(await screen.findByText('The API is available. Version 0.1.0.')).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('changes the interface to French when the user selects French', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.selectOptions(screen.getByLabelText('Language'), 'fr')

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Gérez vos decks et votre collection.')
    expect(screen.getByLabelText('Langue')).toHaveValue('fr')
    expect(document.documentElement.lang).toBe('fr')
  })

  it('tells the user when the API is not available', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('Network error'))))
    render(<App />)

    expect(await screen.findByText('The API is not available.')).toBeInTheDocument()
  })

  it('shows a page for an unknown address', async () => {
    window.history.replaceState(null, '', '/nowhere')
    render(<App />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
  })
})
