import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { PurchaseList } from '../api/purchases'
import i18n from '../i18n'
import { seriousAccessibilityProblems } from '../test/axe'
import { atraxa, deckId, notLegal, solRing, testDeck } from '../test/fixtures'
import { mockApi, session, testAccount, type Handler } from '../test/mockApi'

const treasure = '0190a000-0000-7000-8000-0000000000c3'
const rack = '0190a000-0000-7000-8000-0000000000e1'
const binder = '0190a000-0000-7000-8000-0000000000e2'
const printingC21 = '0190a000-0000-7000-8000-0000000000f1'
const printingLea = '0190a000-0000-7000-8000-0000000000f2'

const copy = { sectionName: null, locationKind: 'rack', locked: false, printingId: null, finish: null, condition: null, reasons: [] as string[] }

function purchaseList(includeTokens = false): PurchaseList {
  const lines: PurchaseList['lines'] = [
    {
      oracleId: atraxa,
      zone: 'commander',
      quantity: 1,
      purchasePreferences: null,
      inDeck: 0,
      fromCollection: [{ ...copy, locationId: binder, locationName: 'Binder 1', locked: true, language: 'en', quantity: 1 }],
      toBuy: 0,
      closest: [],
    },
    {
      oracleId: solRing,
      zone: 'main',
      quantity: 2,
      purchasePreferences: { languages: ['fr'], foil: 'any' },
      inDeck: 0,
      fromCollection: [],
      toBuy: 2,
      closest: [{ ...copy, locationId: rack, locationName: 'Rack 2', language: 'en', quantity: 1, reasons: ['language'] }],
    },
  ]
  if (includeTokens) {
    lines.push({ oracleId: treasure, zone: 'tokens', quantity: 3, purchasePreferences: null, inDeck: 0, fromCollection: [], toBuy: 3, closest: [] })
  }
  return {
    deckId,
    includeTokens,
    lines,
    totalToBuy: includeTokens ? 5 : 2,
    cards: { ...testDeck().cards, [treasure]: { ...testDeck().cards[solRing], name: 'Treasure' } },
    printings: {},
  }
}

const printings = {
  items: [
    { id: printingC21, setCode: 'c21', setName: 'Commander 2021', collectorNumber: '263', lang: 'en', releasedAt: '2021-04-23', rarity: 'uncommon', printedName: null, imageUris: {}, faces: [] },
    { id: printingLea, setCode: 'lea', setName: 'Limited Edition Alpha', collectorNumber: '270', lang: 'en', releasedAt: '1993-08-05', rarity: 'uncommon', printedName: null, imageUris: {}, faces: [] },
  ],
  nextCursor: null,
}

function handlers(extra: Record<string, Handler> = {}): Record<string, Handler> {
  return {
    'POST /auth/refresh': () => session(),
    [`GET /decks/${deckId}`]: () => Response.json(testDeck()),
    [`GET /decks/${deckId}/legality`]: () => Response.json(notLegal),
    [`GET /decks/${deckId}/analysis`]: () => Response.json({}),
    [`GET /decks/${deckId}/recommendations`]: () => Response.json({ status: 'ok', source: { name: 'EDHREC', url: 'https://edhrec.com' }, commanders: [], cards: [] }),
    [`GET /decks/${deckId}/versions`]: () => Response.json({ versions: [], workingListChanged: true, headVersionId: null, assembledVersionId: null }),
    [`GET /decks/${deckId}/variants`]: () => Response.json([]),
    [`GET /decks/${deckId}/purchase-list`]: ({ url }) => Response.json(purchaseList(url.searchParams.get('includeTokens') === 'true')),
    [`GET /cards/${solRing}/printings`]: () => Response.json(printings),
    ...extra,
  }
}

async function openPurchaseList(extra: Record<string, Handler> = {}) {
  window.history.replaceState(null, '', `/decks/${deckId}/purchase-list`)
  const calls = mockApi(handlers(extra))
  render(<App />)
  await screen.findByRole('table', { name: 'Lines of the purchase list' })
  return calls
}

describe('purchase list page', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(async () => {
    vi.unstubAllGlobals()
    await i18n.changeLanguage('en')
  })

  it('shows each line, the closest copies and the total to buy', async () => {
    await openPurchaseList()

    expect(screen.getByRole('heading', { level: 1, name: 'Purchase list of Atraxa superfriends' })).toBeInTheDocument()
    expect(screen.getByText('2 cards to buy')).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /Sol Ring/ })
    expect(within(row).getByText('Not in French: 1 English copy in Rack 2')).toBeInTheDocument()
    expect(within(row).getByText('Purchase preferences')).toBeInTheDocument()
    const commander = screen.getByRole('row', { name: /Atraxa/ })
    expect(within(commander).getByText('1 in Binder 1')).toBeInTheDocument()
    expect(within(commander).getByText('Unlock Binder 1 before you move the cards.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Open the deck' })).toHaveAttribute('href', `/decks/${deckId}`)
    expect(await seriousAccessibilityProblems()).toEqual([])
  })

  it('asks for the tokens when the user includes them', async () => {
    const calls = await openPurchaseList()
    await userEvent.setup().click(screen.getByRole('checkbox', { name: 'Include the tokens' }))

    expect(await screen.findByText('5 cards to buy')).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /Treasure/ })).toBeInTheDocument()
    expect(calls.filter((call) => call.key === `GET /decks/${deckId}/purchase-list`)).toHaveLength(2)
  })

  it('exports the cards to buy for Cardmarket and TCGplayer, and copies the text', async () => {
    await openPurchaseList()
    const user = userEvent.setup()
    const writeText = vi.fn<(text: string) => Promise<void>>(() => Promise.resolve())
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })

    const text = screen.getByRole('textbox', { name: 'Text of the export' })
    expect(text).toHaveValue('2 Sol Ring\n')
    await user.selectOptions(screen.getByLabelText('Format'), 'tcgplayer')
    expect(text).toHaveValue('2 Sol Ring\n')
    await user.click(screen.getByRole('button', { name: 'Copy the text' }))

    expect(writeText).toHaveBeenCalledWith('2 Sol Ring\n')
    expect(await screen.findByText('The text is in the clipboard.')).toBeInTheDocument()
  })

  it('writes the closest copies in French', async () => {
    await i18n.changeLanguage('fr')
    window.history.replaceState(null, '', `/decks/${deckId}/purchase-list`)
    mockApi(handlers({ 'POST /auth/refresh': () => session('token-1', { ...testAccount, language: 'fr' }) }))
    render(<App />)

    // The default text matcher changes the non-breaking space before the colon to a space.
    expect(await screen.findByText('Pas en français : 1 exemplaire en anglais dans Rack 2')).toBeInTheDocument()
    expect(screen.getByText('2 cartes à acheter')).toBeInTheDocument()
  })
})

describe('purchase preferences', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('saves the preferences of a line and shows a marker on the line', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    const preferences = { wantedPrintingIds: [printingC21], avoidedPrintingIds: [printingLea], languages: ['fr', 'en'], minimumCondition: 'near_mint', foil: 'nonfoil' as const, maximumPrice: { amount: 2.5, currency: 'EUR' } }
    const saved = testDeck()
    saved.lines[1] = { ...saved.lines[1], purchasePreferences: preferences }
    const calls = mockApi(handlers({ [`PATCH /decks/${deckId}/lines/l2`]: () => Response.json(saved) }))
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByRole('link', { name: 'Purchase list' })).toHaveAttribute('href', `/decks/${deckId}/purchase-list`)
    await user.click(screen.getByRole('button', { name: 'Set the purchase preferences of Sol Ring' }))
    const dialog = await screen.findByRole('dialog', { name: 'Purchase preferences of Sol Ring' })
    await within(dialog).findByLabelText('Choice for the printing Commander 2021 (C21 263, en)')
    expect(await seriousAccessibilityProblems()).toEqual([])

    await user.click(within(dialog).getByRole('checkbox', { name: 'French' }))
    await user.click(within(dialog).getByRole('checkbox', { name: 'English' }))
    await user.selectOptions(within(dialog).getByLabelText('Foil'), 'nonfoil')
    await user.selectOptions(within(dialog).getByLabelText('Minimum condition'), 'near_mint')
    await user.selectOptions(within(dialog).getByLabelText('Choice for the printing Commander 2021 (C21 263, en)'), 'wanted')
    await user.selectOptions(within(dialog).getByLabelText('Choice for the printing Limited Edition Alpha (LEA 270, en)'), 'avoided')
    await user.type(within(dialog).getByLabelText('Amount'), '2,50')
    await user.click(within(dialog).getByRole('button', { name: 'Save the preferences' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(calls.find((call) => call.key === `PATCH /decks/${deckId}/lines/l2`)?.body).toEqual({ purchasePreferences: preferences })
    expect(screen.getByText('Sol Ring has purchase preferences')).toBeInTheDocument()
  })

  it('refuses an amount that is not a number', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    const calls = mockApi(handlers())
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Set the purchase preferences of Sol Ring' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Amount'), 'cheap')
    await user.click(within(dialog).getByRole('button', { name: 'Save the preferences' }))

    expect(within(dialog).getByText('The amount is not valid. Give an amount more than 0, for example 2.50.')).toBeInTheDocument()
    expect(calls.some((call) => call.key.startsWith('PATCH'))).toBe(false)
  })

  it('removes the preferences of a line', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    const deck = testDeck()
    deck.lines[1] = { ...deck.lines[1], purchasePreferences: { languages: ['fr'] } }
    const calls = mockApi(handlers({ [`GET /decks/${deckId}`]: () => Response.json(deck), [`PATCH /decks/${deckId}/lines/l2`]: () => Response.json(testDeck()) }))
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByText('Sol Ring has purchase preferences')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Set the purchase preferences of Sol Ring' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove the preferences' }))

    await waitFor(() => expect(screen.queryByText('Sol Ring has purchase preferences')).not.toBeInTheDocument())
    expect(calls.find((call) => call.key === `PATCH /decks/${deckId}/lines/l2`)?.body).toEqual({ clearPurchasePreferences: true })
  })
})
