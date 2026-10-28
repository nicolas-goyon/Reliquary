import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { seriousAccessibilityProblems } from '../test/axe'
import { atraxa, deckId, notLegal, testAnalysis, testDeck, testDeckSummary } from '../test/fixtures'
import { mockApi, problem, session, testAccount, type Handler } from '../test/mockApi'

const cardId = '0190a000-0000-7000-8000-00000000b017'

const bolt = {
  card: { id: cardId, name: 'Lightning Bolt', layout: 'normal', manaCost: '{R}', manaValue: 1, typeLine: 'Instant', oracleText: 'Lightning Bolt deals 3 damage to any target.', colors: ['R'], colorIdentity: ['R'], faces: [] },
  printing: { id: 'p1', setCode: 'm10', setName: 'Magic 2010', collectorNumber: '146', lang: 'en', releasedAt: '2009-07-17', rarity: 'common', printedName: null, imageUris: { normal: 'https://cards.scryfall.io/normal/front/bolt.jpg' }, faces: [] },
}

const status = () => Response.json({ name: 'Reliquary', version: '0.1.0', components: [], coreApi: '1.0.0' })

const signedIn: Record<string, Handler> = {
  'POST /auth/refresh': () => session(),
  'GET /status': status,
  'GET /decks': () => Response.json({ items: [testDeckSummary], nextCursor: null }),
  'GET /deck-folders': () => Response.json([{ id: 'f1', name: 'Commander', parentId: null, deckCount: 1 }]),
  'GET /decks/tags': () => Response.json([{ name: 'Casual', deckCount: 1 }]),
  [`GET /decks/${deckId}`]: () => Response.json(testDeck()),
  [`GET /decks/${deckId}/legality`]: () => Response.json(notLegal),
  [`GET /decks/${deckId}/analysis`]: () => Response.json(testAnalysis()),
  [`GET /decks/${deckId}/recommendations`]: () => Response.json({ status: 'ok', source: { name: 'EDHREC', url: 'https://edhrec.com' }, commanders: [atraxa], cards: [] }),
  [`GET /public-decks/${deckId}/social`]: () => problem(404, 'not_found'),
  'GET /cards/search': () => Response.json({ items: [bolt], nextCursor: null, total: 1, warnings: [] }),
  'GET /me': () => Response.json({ account: testAccount, externalLogins: ['github'] }),
  'GET /me/api-keys': () => Response.json([{ id: 'k1', name: 'Script', prefix: 'abc', createdAt: '2026-10-26T10:00:00Z', lastUsedAt: null }]),
  'GET /me/exports': () => Response.json([]),
  'GET /me/deletion': () => Response.json({ requestedAt: null, deleteAfter: null }),
  'GET /auth/providers': () => Response.json({ providers: ['github'] }),
  'GET /public-decks': () =>
    Response.json({
      items: [{ id: deckId, name: 'Atraxa superfriends', format: 'commander', colorIdentity: 'WUBG', commanders: [{ oracleId: atraxa, name: "Atraxa, Praetors' Voice", imageUri: null }], author: { id: 'a1', displayName: 'Ana' }, cardCount: 100, copyCount: 3, favoriteCount: 1, updatedAt: '2026-10-27T10:00:00Z' }],
      nextCursor: null,
    }),
  [`GET /cards/${cardId}`]: () =>
    Response.json({ ...bolt.card, keywords: [], legalities: { commander: 'legal', modern: 'legal' }, reserved: false, gameChanger: false, edhrecRank: 1, extra: false, defaultPrinting: bolt.printing, names: [] }),
  [`GET /cards/${cardId}/rulings`]: () => Response.json([]),
  [`GET /cards/${cardId}/printings`]: () => Response.json({ items: [bolt.printing], nextCursor: null }),
}

const anonymous: Record<string, Handler> = {
  ...signedIn,
  'POST /auth/refresh': () => problem(401, 'session_expired'),
}

async function openPage(path: string, handlers: Record<string, Handler>, ready: () => Promise<unknown>) {
  window.history.replaceState(null, '', path)
  mockApi(handlers)
  render(<App />)
  await ready()
}

describe('accessibility of the main pages', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('home page', async () => {
    await openPage('/', anonymous, () => screen.findByText('The API is available. Version 0.1.0.'))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('sign-in page', async () => {
    await openPage('/sign-in', anonymous, () => screen.findByRole('heading', { level: 1 }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('registration page', async () => {
    await openPage('/register', anonymous, () => screen.findByRole('heading', { level: 1 }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('card search page with results', async () => {
    await openPage('/cards?q=bolt', anonymous, () => screen.findByRole('img', { name: 'Lightning Bolt' }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('card search page with the open filter panel', async () => {
    await openPage('/cards', anonymous, () => screen.findByRole('combobox', { name: 'Search' }))
    await userEvent.setup().click(screen.getByRole('button', { name: /Filters/ }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('card page', async () => {
    await openPage(`/cards/${cardId}`, anonymous, () => screen.findByRole('heading', { level: 1, name: 'Lightning Bolt' }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('explore page', async () => {
    await openPage('/explore', anonymous, () => screen.findByText('Atraxa superfriends'))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('deck list', async () => {
    await openPage('/decks', signedIn, () => screen.findByRole('link', { name: 'Atraxa superfriends' }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('deck list with the dialog of a new deck and a selected deck', async () => {
    await openPage('/decks', signedIn, () => screen.findByRole('link', { name: 'Atraxa superfriends' }))
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Make a deck' }))
    await user.click(screen.getByRole('checkbox', { name: /Select Atraxa superfriends/ }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('deck page of the owner', async () => {
    await openPage(`/decks/${deckId}`, signedIn, () => screen.findByRole('tab', { name: 'Mana curve' }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('deck page in table mode', async () => {
    await openPage(`/decks/${deckId}`, signedIn, () => screen.findByRole('heading', { level: 1, name: /Atraxa superfriends/ }))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Table mode' }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('deck page of another user', async () => {
    await openPage(`/decks/${deckId}`, { ...anonymous, [`GET /decks/${deckId}`]: () => Response.json(testDeck({ isOwner: false, visibility: 'public' })) }, () =>
      screen.findByRole('heading', { level: 1, name: /Atraxa superfriends/ }),
    )
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('account page', async () => {
    await openPage('/account', signedIn, () => screen.findByRole('cell', { name: 'Script' }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('dialog of the keyboard shortcuts', async () => {
    await openPage('/', anonymous, () => screen.findByText('The API is available. Version 0.1.0.'))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Keyboard shortcuts' }))
    expect(await seriousAccessibilityProblems()).toEqual([])
  })
})
