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
