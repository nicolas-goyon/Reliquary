import { describe, expect, it } from 'vitest'
import type { DeckCard } from '../api/decks'
import type { PurchaseList, PurchaseListLine } from '../api/purchases'
import { cardmarketText, cardsToBuy, exportFileName, exportText, plainText, purchaseProxyItems, tcgplayerText } from './exports'

const bolt = 'c0000000-0000-7000-8000-000000000001'
const counterspell = 'c0000000-0000-7000-8000-000000000002'
const solRing = 'c0000000-0000-7000-8000-000000000003'
const delver = 'c0000000-0000-7000-8000-000000000004'
const boltM10 = 'p0000000-0000-7000-8000-000000000001'
const boltM11 = 'p0000000-0000-7000-8000-000000000002'

function card(name: string): DeckCard {
  return { name, layout: 'normal', manaValue: 1, curveManaValue: 1, manaCost: null, typeLine: null, colorIdentity: [], cardTypes: [], isLand: false, defaultPrintingId: null, faces: [] }
}

function line(oracleId: string, zone: PurchaseListLine['zone'], quantity: number, toBuy: number, preferences: PurchaseListLine['purchasePreferences'] = null): PurchaseListLine {
  return { oracleId, zone, quantity, purchasePreferences: preferences, inDeck: 0, fromCollection: [], toBuy, closest: [] }
}

/** The reference list: a card in two zones, a card with nothing to buy, a card with one wanted printing and a double-faced card. */
function referenceList(): PurchaseList {
  return {
    deckId: 'd1',
    includeTokens: false,
    lines: [
      line(bolt, 'main', 4, 3, { wantedPrintingIds: [boltM10], maximumPrice: { amount: 2, currency: 'EUR' } }),
      line(solRing, 'main', 1, 0),
      line(counterspell, 'main', 2, 2),
      line(delver, 'main', 4, 4, { languages: ['fr'] }),
      line(bolt, 'sideboard', 2, 1, { wantedPrintingIds: [boltM10], maximumPrice: { amount: 2.5, currency: 'EUR' } }),
      line(counterspell, 'sideboard', 1, 1),
    ],
    totalToBuy: 11,
    cards: {
      [bolt]: card('Lightning Bolt'),
      [counterspell]: card('Counterspell'),
      [solRing]: card('Sol Ring'),
      [delver]: card('Delver of Secrets // Insectile Aberration'),
    },
    printings: {
      [boltM10]: { setCode: 'm10', collectorNumber: '146', lang: 'en', imageUri: 'https://cards.scryfall.io/normal/front/bolt.jpg' },
      [boltM11]: { setCode: 'm11', collectorNumber: '149', lang: 'en', imageUri: null },
    },
  }
}

describe('purchase list exports', () => {
  it('gives the Cardmarket wants list of the reference list, with one line for each card', () => {
    expect(cardmarketText(cardsToBuy(referenceList()))).toBe(['4 Lightning Bolt', '3 Counterspell', '4 Delver of Secrets // Insectile Aberration'].join('\n'))
  })

  it('keeps the quantities of the cards to buy, not the quantities of the deck', () => {
    const cards = cardsToBuy(referenceList())
    expect(cards.map((item) => [item.name, item.quantity])).toEqual([
      ['Lightning Bolt', 4],
      ['Counterspell', 3],
      ['Delver of Secrets // Insectile Aberration', 4],
    ])
    expect(cards.reduce((total, item) => total + item.quantity, 0)).toBe(referenceList().totalToBuy)
  })

  it('gives the set code to TCGplayer when the card wants exactly one printing', () => {
    expect(tcgplayerText(cardsToBuy(referenceList()))).toBe(['4 Lightning Bolt [M10]', '3 Counterspell', '4 Delver of Secrets // Insectile Aberration'].join('\n'))
  })

  it('gives no set code when the lines of a card want different printings', () => {
    const list = referenceList()
    list.lines[4] = line(bolt, 'sideboard', 2, 1, { wantedPrintingIds: [boltM11] })
    expect(cardsToBuy(list)[0]).toMatchObject({ name: 'Lightning Bolt', quantity: 4, setCode: null })
  })

  it('gives no set code when a line wants more than one printing', () => {
    const list = referenceList()
    list.lines = [line(bolt, 'main', 1, 1, { wantedPrintingIds: [boltM10, boltM11] })]
    expect(tcgplayerText(cardsToBuy(list))).toBe('1 Lightning Bolt')
  })

  it('gives the printing and the highest price in the plain text', () => {
    expect(plainText(cardsToBuy(referenceList()))).toBe(['4 Lightning Bolt (M10 146, max 2.50 EUR)', '3 Counterspell', '4 Delver of Secrets // Insectile Aberration'].join('\n'))
  })

  it('gives an empty text when there is no card to buy, and a final line break otherwise', () => {
    const list = referenceList()
    expect(exportText('cardmarket', cardsToBuy(list)).endsWith('Aberration\n')).toBe(true)
    list.lines = [line(solRing, 'main', 1, 0)]
    expect(exportText('cardmarket', cardsToBuy(list))).toBe('')
  })

  it('makes a safe file name', () => {
    expect(exportFileName('Atraxa: "super" friends', 'cardmarket')).toBe('Atraxa super friends - purchases - cardmarket.txt')
    expect(exportFileName('///', 'text')).toBe('purchases - text.txt')
  })

  it('makes the proxies of the cards to buy with the image of the wanted printing', () => {
    const list = referenceList()
    expect(purchaseProxyItems(list, cardsToBuy(list))).toEqual([
      { imageUri: 'https://cards.scryfall.io/large/front/bolt.jpg', quantity: 4, name: 'Lightning Bolt' },
      { imageUri: undefined, quantity: 3, name: 'Counterspell' },
      { imageUri: undefined, quantity: 4, name: 'Delver of Secrets // Insectile Aberration' },
    ])
  })
})
