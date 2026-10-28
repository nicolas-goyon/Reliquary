import type { DeckAnalysis } from '../api/analysis'
import type { Deck, DeckSummary } from '../api/decks'

/** Test data for the pages: one deck with a commander and one card in the main deck. */
export const deckId = '0190a000-0000-7000-8000-0000000000d1'
export const solRing = '0190a000-0000-7000-8000-0000000000c1'
export const atraxa = '0190a000-0000-7000-8000-0000000000c2'

export function testDeck(overrides: Partial<Deck> = {}): Deck {
  return {
    id: deckId,
    ownerId: 'owner',
    name: 'Atraxa superfriends',
    format: 'commander',
    visibility: 'private',
    folderId: null,
    description: '',
    sourceDeckId: null,
    pinned: false,
    archived: false,
    version: 3,
    createdAt: '2026-10-27T10:00:00Z',
    updatedAt: '2026-10-27T10:00:00Z',
    isOwner: true,
    counts: { commander: 1, companion: 0, main: 1, sideboard: 0, considering: 0, tokens: 0 },
    lines: [
      { id: 'l1', oracleId: atraxa, printingId: null, finish: null, quantity: 1, zone: 'commander', note: '', tagIds: [], categories: [] },
      { id: 'l2', oracleId: solRing, printingId: null, finish: 'foil', quantity: 1, zone: 'main', note: '', tagIds: ['t1'], categories: ['ramp'] },
    ],
    cards: {
      [atraxa]: { name: "Atraxa, Praetors' Voice", layout: 'normal', manaValue: 4, curveManaValue: 4, manaCost: '{G}{W}{U}{B}', typeLine: 'Legendary Creature', colorIdentity: ['W', 'U', 'B', 'G'], cardTypes: ['Creature'], isLand: false, defaultPrintingId: null, faces: [] },
      [solRing]: { name: 'Sol Ring', layout: 'normal', manaValue: 1, curveManaValue: 1, manaCost: '{1}', typeLine: 'Artifact', colorIdentity: [], cardTypes: ['Artifact'], isLand: false, defaultPrintingId: null, faces: [] },
    },
    printings: {},
    tags: { t1: { name: 'Fast mana', global: false } },
    ...overrides,
  }
}

export const testDeckSummary: DeckSummary = {
  id: deckId,
  name: 'Atraxa superfriends',
  format: 'commander',
  visibility: 'private',
  folderId: null,
  pinned: true,
  archived: false,
  colorIdentity: 'WUBG',
  cardCount: 2,
  commanderIds: [atraxa],
  tags: ['Casual'],
  createdAt: '2026-10-27T10:00:00Z',
  updatedAt: '2026-10-27T10:00:00Z',
}

export const notLegal = { format: 'commander', legal: false, problems: [{ code: 'deck_size', oracleId: null, count: 2, limit: 100 }] }

const atLeast = (values: number[]) => values.map((probability, index) => ({ lands: index + 1, probability }))

/** An analysis with values in each panel. */
export function testAnalysis(overrides: Partial<DeckAnalysis> = {}): DeckAnalysis {
  return {
    format: 'commander',
    cardCount: 100,
    landCount: 37,
    modalLandCount: 1,
    spellCount: 64,
    averageManaValue: 3.25,
    manaCurve: [0, 6, 12, 15, 14, 9, 5, 3].map((count, manaValue) => ({ manaValue, count })),
    types: [{ key: 'artifact', count: 10 }, { key: 'creature', count: 30 }, { key: 'land', count: 37 }],
    colors: [{ key: 'W', count: 20 }, { key: 'U', count: 18 }, { key: 'B', count: 15 }, { key: 'R', count: 0 }, { key: 'G', count: 22 }, { key: 'C', count: 8 }],
    categories: [{ key: 'ramp', count: 6 }, { key: 'land', count: 37 }, { key: 'uncategorized', count: 20 }],
    mana: {
      tableDeckSize: 99,
      colors: [{ color: 'W', symbols: 25, landSources: 16, otherSources: 3, sources: 17.5, required: 23, oracleId: atraxa, pips: 1, manaValue: 4, status: 'low' }],
    },
    draws: {
      librarySize: 99,
      lands: 37,
      checkpoints: [{ code: 'opening_hand', turn: 1, onThePlay: true, cards: 7, atLeast: atLeast([0.9632, 0.8312, 0.5938, 0.3166, 0.1213, 0.0316, 0.0045]) }],
    },
    needs: [{ category: 'ramp', count: 6, target: 10, min: 8, max: 15, status: 'low', difference: -4 }],
    bracket: { bracket: 3, reasons: [{ code: 'game_changers', bracket: 3, oracleIds: [solRing], comboIds: [] }], unchecked: [] },
    combos: { available: true, complete: [], near: [], nearCount: 0 },
    ...overrides,
  }
}
