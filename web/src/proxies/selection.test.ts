import { describe, expect, it, vi } from 'vitest'
import type { Deck, DeckLine } from '../api/decks'
import { testDeck } from '../test/fixtures'
import { cardTotal, expandItems, linesOfZones, printImage, proxyItems, proxyItemsWithBackFaces, selectedLines, type PrintingImages } from './selection'

const image = (id: string) => `https://cards.scryfall.io/normal/front/a/b/${id}.jpg`

function line(id: string, oracleId: string, zone: DeckLine['zone'], quantity: number, printingId: string | null = null): DeckLine {
  return { id, oracleId, printingId, finish: null, quantity, zone, note: '', tagIds: [], categories: [] }
}

function card(name: string, faces: string[] = []) {
  return {
    name,
    layout: faces.length > 1 ? 'transform' : 'normal',
    manaValue: 1,
    curveManaValue: 1,
    manaCost: null,
    typeLine: null,
    colorIdentity: [],
    cardTypes: [],
    isLand: false,
    defaultPrintingId: `p-${name}`,
    faces: faces.map((face) => ({ name: face, manaCost: null, typeLine: null })),
  }
}

function deck(): Deck {
  return testDeck({
    counts: { commander: 1, companion: 0, main: 5, sideboard: 1, considering: 1, tokens: 1 },
    lines: [
      line('l1', 'forest', 'main', 4),
      line('l2', 'atraxa', 'commander', 1),
      line('l3', 'duress', 'sideboard', 1),
      line('l4', 'delver', 'main', 1, 'p-delver-special'),
      line('l5', 'idea', 'considering', 1),
      line('l6', 'treasure', 'tokens', 1),
    ],
    cards: {
      forest: card('Forest'),
      atraxa: card('Atraxa'),
      duress: card('Duress'),
      delver: card('Delver of Secrets', ['Delver of Secrets', 'Insectile Aberration']),
      idea: card('Idea'),
      treasure: card('Treasure'),
    },
    printings: {
      'p-Forest': { setCode: 'lea', collectorNumber: '1', lang: 'en', imageUri: image('forest') },
      'p-Atraxa': { setCode: 'c16', collectorNumber: '2', lang: 'en', imageUri: image('atraxa') },
      'p-delver-special': { setCode: 'isd', collectorNumber: '3', lang: 'en', imageUri: image('delver') },
      'p-Treasure': { setCode: 'tafr', collectorNumber: '4', lang: 'en', imageUri: null },
    },
  })
}

describe('proxy selection', () => {
  it('takes the commander, companion and main zones, in the order of the deck page', () => {
    const lines = linesOfZones(deck(), [])
    expect(lines.map((value) => value.id)).toEqual(['l2', 'l4', 'l1'])
  })

  it('adds the zones that the user selects', () => {
    expect(linesOfZones(deck(), ['sideboard', 'tokens']).map((value) => value.id)).toEqual(['l2', 'l4', 'l1', 'l3', 'l6'])
    expect(linesOfZones(deck(), ['considering']).map((value) => value.id)).toEqual(['l2', 'l4', 'l1', 'l5'])
  })

  it('takes only the selected lines', () => {
    expect(selectedLines(deck(), new Set(['l6', 'l1'])).map((value) => value.id)).toEqual(['l1', 'l6'])
  })

  it('keeps the quantities and uses the large image', () => {
    const value = deck()
    const items = proxyItems(value, linesOfZones(value, ['tokens']))

    expect(items).toEqual([
      { imageUri: 'https://cards.scryfall.io/large/front/a/b/atraxa.jpg', quantity: 1, name: 'Atraxa' },
      { imageUri: 'https://cards.scryfall.io/large/front/a/b/delver.jpg', quantity: 1, name: 'Delver of Secrets' },
      { imageUri: 'https://cards.scryfall.io/large/front/a/b/forest.jpg', quantity: 4, name: 'Forest' },
      { imageUri: undefined, quantity: 1, name: 'Treasure' },
    ])
    expect(cardTotal(items)).toBe(7)
    expect(expandItems(items)).toHaveLength(7)
    expect(expandItems(items).filter((item) => item.name === 'Forest')).toHaveLength(4)
  })

  it('keeps the address of an image that is not on the Scryfall servers', () => {
    expect(printImage('https://example.org/normal/card.jpg')).toBe('https://example.org/normal/card.jpg')
    expect(printImage(undefined)).toBeUndefined()
  })

  it('adds the back face of a double-faced card after its front face', async () => {
    const value = deck()
    const printing: PrintingImages = {
      imageUris: {},
      faces: [
        { name: 'Delver of Secrets', printedName: null, imageUris: { normal: image('delver'), large: 'https://cards.scryfall.io/large/front/a/b/delver.jpg' } },
        { name: 'Insectile Aberration', printedName: null, imageUris: { normal: 'https://cards.scryfall.io/normal/back/a/b/delver.jpg' } },
      ],
    }
    const getPrinting = vi.fn<(printingId: string) => Promise<PrintingImages>>(async () => printing)

    const items = await proxyItemsWithBackFaces(value, linesOfZones(value, []), getPrinting)

    expect(getPrinting).toHaveBeenCalledTimes(1)
    expect(getPrinting).toHaveBeenCalledWith('p-delver-special')
    expect(items.map((item) => item.name)).toEqual(['Atraxa', 'Delver of Secrets', 'Insectile Aberration', 'Forest'])
    expect(items[2].imageUri).toBe('https://cards.scryfall.io/large/back/a/b/delver.jpg')
  })

  it('keeps the front face only when the printing does not load', async () => {
    const value = deck()
    const items = await proxyItemsWithBackFaces(value, linesOfZones(value, []), () => Promise.reject(new Error('offline')))
    expect(items.map((item) => item.name)).toEqual(['Atraxa', 'Delver of Secrets', 'Forest'])
  })
})
