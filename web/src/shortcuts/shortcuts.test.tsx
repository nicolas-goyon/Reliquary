import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { mockApi, problem, session } from '../test/mockApi'
import { deckId, notLegal, testDeck, testDeckSummary } from '../test/fixtures'
import { ShortcutMatcher, sequenceTimeout } from './shortcuts'

function signedInApi(extra: Parameters<typeof mockApi>[0] = {}) {
  return mockApi({
    'POST /auth/refresh': () => session(),
    'GET /status': () => Response.json({ name: 'Reliquary', version: '0.1.0', components: [], coreApi: '1.0.0' }),
    'GET /decks': () => Response.json({ items: [testDeckSummary], nextCursor: null }),
    'GET /deck-folders': () => Response.json([]),
    'GET /decks/tags': () => Response.json([]),
    [`GET /decks/${deckId}`]: () => Response.json(testDeck()),
    [`GET /decks/${deckId}/legality`]: () => Response.json(notLegal),
    [`GET /decks/${deckId}/analysis`]: () => problem(404, 'not_found'),
    [`GET /decks/${deckId}/recommendations`]: () => problem(404, 'not_found'),
    ...extra,
  })
}

describe('shortcut matcher', () => {
  it('finds a shortcut of one key and a sequence of two keys', () => {
    const matcher = new ShortcutMatcher()

    expect(matcher.press('/', 0)).toMatchObject({ description: 'search' })
    expect(matcher.press('g', 10)).toBe('pending')
    expect(matcher.press('d', 20)).toMatchObject({ description: 'decks' })
  })

  it('forgets the first key of a sequence after the timeout', () => {
    const matcher = new ShortcutMatcher()

    expect(matcher.press('g', 0)).toBe('pending')
    expect(matcher.press('d', sequenceTimeout + 1)).toBeNull()
  })

  it('starts a new sequence after an unknown second key', () => {
    const matcher = new ShortcutMatcher()

    expect(matcher.press('g', 0)).toBe('pending')
    expect(matcher.press('n', 10)).toMatchObject({ description: 'newDeck' })
    expect(matcher.press('x', 20)).toBeNull()
  })
})

describe('keyboard shortcuts', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('lets a user add a card to a deck with the keyboard only', async () => {
    window.history.replaceState(null, '', '/')
    const calls = signedInApi({
      'GET /cards/autocomplete': () => Response.json([{ oracleId: 'bolt', name: 'Lightning Bolt', matchedName: 'Lightning Bolt', lang: 'en' }]),
      [`POST /decks/${deckId}/lines`]: () => Response.json(testDeck({ version: 4 })),
    })
    const user = userEvent.setup()
    render(<App />)
    await screen.findByRole('link', { name: 'Decks' })

    // "g" then "d" opens the deck list.
    await user.keyboard('gd')
    const deckLink = await screen.findByRole('link', { name: 'Atraxa superfriends' })
    expect(window.location.pathname).toBe('/decks')

    // Tab moves to the link of the deck, and Enter opens the deck.
    for (let index = 0; index < 60 && document.activeElement !== deckLink; index += 1) {
      await user.tab()
    }
    expect(deckLink).toHaveFocus()
    await user.keyboard('{Enter}')
    await screen.findByRole('heading', { level: 1, name: /Atraxa superfriends/ })

    // "a" moves the focus to the field "Add a card". The key does not type a character.
    await user.keyboard('a')
    const field = screen.getByRole('combobox', { name: 'Add a card' })
    expect(field).toHaveFocus()
    expect(field).toHaveValue('')

    await user.keyboard('bolt')
    await screen.findByRole('option', { name: /Lightning Bolt/ })
    await user.keyboard('{ArrowDown}{Enter}')

    await waitFor(() => expect(calls.find((call) => call.key === `POST /decks/${deckId}/lines`)?.body).toEqual({ lines: [{ oracleId: 'bolt', zone: 'main' }] }))
  })

  it('opens the dialog of a new deck with "n" on the deck list', async () => {
    window.history.replaceState(null, '', '/decks')
    signedInApi()
    const user = userEvent.setup()
    render(<App />)
    await screen.findByRole('link', { name: 'Atraxa superfriends' })

    await user.keyboard('n')

    const dialog = screen.getByRole('dialog', { name: 'Make a deck' })
    expect(within(dialog).getByLabelText('Name')).toHaveFocus()
    expect(within(dialog).getByLabelText('Name')).toHaveValue('')
  })

  it('opens the card search and focuses the search field with "/"', async () => {
    window.history.replaceState(null, '', '/')
    signedInApi()
    const user = userEvent.setup()
    render(<App />)
    await screen.findByRole('link', { name: 'Decks' })

    await user.keyboard('/')

    expect(window.location.pathname).toBe('/cards')
    expect(await screen.findByRole('combobox', { name: 'Search' })).toHaveFocus()
  })

  it('does not start a shortcut in a field or with a modifier key', async () => {
    window.history.replaceState(null, '', '/decks')
    signedInApi()
    const user = userEvent.setup()
    render(<App />)
    await screen.findByRole('link', { name: 'Atraxa superfriends' })

    await user.click(screen.getByLabelText('New folder'))
    await user.keyboard('gcn')
    expect(screen.getByLabelText('New folder')).toHaveValue('gcn')
    expect(window.location.pathname).toBe('/decks')

    await user.click(document.body)
    await user.keyboard('{Control>}n{/Control}{Alt>}n{/Alt}{Meta>}n{/Meta}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('shows the list of the shortcuts with "?", and Escape closes it', async () => {
    window.history.replaceState(null, '', '/')
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /status': () => Response.json({ name: 'Reliquary', version: '0.1.0', components: [], coreApi: '1.0.0' }),
    })
    const user = userEvent.setup()
    render(<App />)
    const footerButton = screen.getByRole('button', { name: 'Keyboard shortcuts' })
    footerButton.focus()

    await user.keyboard('?')

    const dialog = screen.getByRole('dialog', { name: 'Keyboard shortcuts' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(within(dialog).getByText('Open your decks')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Close' })).toHaveFocus()

    // The focus stays in the dialog, and the shortcuts do not work behind the dialog.
    await user.tab()
    expect(within(dialog).getByRole('button', { name: 'Close' })).toHaveFocus()
    await user.keyboard('gc')
    expect(window.location.pathname).toBe('/')

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(footerButton).toHaveFocus()
  })
})
