import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { mockApi, problem } from '../test/mockApi'

const bolt = {
  card: {
    id: '0190a000-0000-7000-8000-00000000b017',
    name: 'Lightning Bolt',
    layout: 'normal',
    manaCost: '{R}',
    manaValue: 1,
    typeLine: 'Instant',
    oracleText: 'Lightning Bolt deals 3 damage to any target.',
    colors: ['R'],
    colorIdentity: ['R'],
    faces: [],
  },
  printing: {
    id: 'p1',
    setCode: 'm10',
    setName: 'Magic 2010',
    collectorNumber: '146',
    lang: 'fr',
    releasedAt: '2009-07-17',
    rarity: 'common',
    printedName: 'Foudre',
    imageUris: { normal: 'https://cards.scryfall.io/normal/front/bolt.jpg' },
    faces: [],
  },
}

const bears = {
  card: { ...bolt.card, id: '0190a000-0000-7000-8000-0000000be425', name: 'Grizzly Bears', manaCost: '{1}{G}', manaValue: 2, typeLine: 'Creature — Bear', oracleText: '', colors: ['G'], colorIdentity: ['G'] },
  printing: { ...bolt.printing, id: 'p2', lang: 'en', printedName: null, imageUris: { normal: 'https://cards.scryfall.io/normal/front/bears.jpg' } },
}

describe('card search', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('searches with the query of the address and shows the results and the warnings', async () => {
    window.history.replaceState(null, '', '/cards?q=foudre%20foo%3Abar')
    const calls = mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /cards/search': () =>
        Response.json({ items: [bolt], nextCursor: null, total: 1, warnings: [{ code: 'unknown_keyword', message: '', position: 7, term: 'foo:bar' }] }),
    })
    render(<App />)

    expect(await screen.findByRole('img', { name: 'Foudre' })).toHaveAttribute('src', bolt.printing.imageUris.normal)
    expect(screen.getByText('1 card')).toBeInTheDocument()
    expect(screen.getByText('The search does not know the keyword of "foo:bar". It ignored this term.')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Search' })).toHaveValue('foudre foo:bar')
    expect(calls.find((call) => call.key === 'GET /cards/search')).toBeDefined()
  })

  it('shows the position of a syntax error', async () => {
    window.history.replaceState(null, '', '/cards?q=t%3Acreature%20(')
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /cards/search': () => Response.json({ status: 400, code: 'search_syntax_error', detail: 'x', position: 11 }, { status: 400 }),
    })
    render(<App />)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The search has a syntax error at the character 12.')
    expect(within(alert).getByText('(')).toContainHTML('mark')
  })

  it('suggests the keywords and the card names, and opens the page of a card', async () => {
    window.history.replaceState(null, '', '/cards')
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /cards/autocomplete': ({ url }) =>
        Response.json(url.searchParams.get('q') === 'foud' ? [{ oracleId: bolt.card.id, name: 'Lightning Bolt', matchedName: 'Foudre', lang: 'fr' }] : []),
      [`GET /cards/${bolt.card.id}`]: () =>
        Response.json({ ...bolt.card, keywords: [], legalities: { commander: 'legal', modern: 'legal' }, reserved: false, gameChanger: false, edhrecRank: 1, extra: false, defaultPrinting: null, names: [{ name: 'Foudre', lang: 'fr', kind: 'printed' }] }),
      [`GET /cards/${bolt.card.id}/rulings`]: () => Response.json([]),
      [`GET /cards/${bolt.card.id}/printings`]: () => Response.json({ items: [], nextCursor: null }),
    })
    const user = userEvent.setup()
    render(<App />)
    const field = await screen.findByRole('combobox', { name: 'Search' })

    await user.type(field, 'otag')
    expect(await screen.findByRole('option', { name: /otag:/ })).toBeInTheDocument()

    await user.clear(field)
    await user.type(field, 'foud')
    await user.click(await screen.findByRole('option', { name: /Foudre/ }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Lightning Bolt' })).toBeInTheDocument()
    expect(window.location.pathname).toBe(`/cards/${bolt.card.id}`)
    expect(screen.getByText('Foudre')).toHaveAttribute('lang', 'fr')
    expect(screen.getByText('Commander').nextSibling).toHaveTextContent('Legal')
  })

  it('finds the legal green creatures with mana value 2 with the filter panel only', async () => {
    window.history.replaceState(null, '', '/cards')
    const calls = mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /cards/search': () => Response.json({ items: [bears], nextCursor: null, total: 1, warnings: [] }),
    })
    const user = userEvent.setup()
    render(<App />)
    const field = await screen.findByRole('combobox', { name: 'Search' })

    const toggle = screen.getByRole('button', { name: 'Filters' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    await user.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')

    await user.click(within(screen.getByRole('group', { name: 'Colors' })).getByRole('checkbox', { name: 'Green' }))
    await user.click(within(screen.getByRole('group', { name: 'Card types' })).getByRole('checkbox', { name: 'Creature' }))
    const manaValue = within(screen.getByRole('group', { name: 'Mana value' }))
    await user.selectOptions(manaValue.getByRole('combobox', { name: 'Operator' }), '=')
    await user.type(manaValue.getByRole('spinbutton', { name: 'Value' }), '2')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Legal in the format' }), 'modern')
    expect(field).toHaveValue('c:g t:creature mv=2 f:modern')

    await user.click(screen.getByRole('button', { name: 'Search' }))

    expect(await screen.findByRole('img', { name: 'Grizzly Bears' })).toBeInTheDocument()
    expect(window.location.search).toBe('?q=c%3Ag%20t%3Acreature%20mv%3D2%20f%3Amodern')
    expect(calls.find((call) => call.key === 'GET /cards/search')).toBeDefined()
    expect(screen.getByRole('button', { name: 'Filters' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(screen.getByRole('group', { name: 'Colors' })).getByRole('checkbox', { name: 'Green' })).toBeChecked()
  })

  it('reads the query of the address into the filter panel and keeps the other terms', async () => {
    window.history.replaceState(null, '', '/cards?q=goblin%20id%3Arg%20r%3Arare')
    mockApi({
      'POST /auth/refresh': () => problem(401, 'session_expired'),
      'GET /cards/search': () => Response.json({ items: [], nextCursor: null, total: 0, warnings: [] }),
    })
    const user = userEvent.setup()
    render(<App />)
    const field = await screen.findByRole('combobox', { name: 'Search' })

    const identity = within(screen.getByRole('group', { name: 'Color identity' }))
    expect(identity.getByRole('checkbox', { name: 'Red' })).toBeChecked()
    expect(identity.getByRole('checkbox', { name: 'Green' })).toBeChecked()
    expect(identity.getByRole('combobox', { name: 'Comparison' })).toHaveValue('atMost')
    expect(screen.getByRole('combobox', { name: 'Rarity' })).toHaveValue('rare')

    await user.selectOptions(identity.getByRole('combobox', { name: 'Comparison' }), 'exactly')
    expect(field).toHaveValue('goblin id=rg r:rare')

    await user.click(screen.getByRole('button', { name: 'Clear the filters' }))
    expect(field).toHaveValue('goblin')
  })
})
