/** The zones of a deck, in the order of the deck page. */
export const zones = ['commander', 'companion', 'main', 'sideboard', 'considering', 'tokens'] as const
export type Zone = (typeof zones)[number]

export const visibilities = ['private', 'unlisted', 'public'] as const
export type Visibility = (typeof visibilities)[number]

/** The formats that the deck dialogs offer, Commander first. */
export const deckFormats = ['commander', 'brawl', 'standardbrawl', 'oathbreaker', 'paupercommander', 'standard', 'pioneer', 'modern', 'legacy', 'vintage', 'pauper', 'freeform'] as const

export const categories = ['ramp', 'card_draw', 'targeted_interaction', 'board_wipe', 'synergy', 'land', 'win_condition', 'protection'] as const

export interface DeckSummary {
  id: string
  name: string
  format: string
  visibility: Visibility
  folderId: string | null
  pinned: boolean
  archived: boolean
  cardCount: number
  commanderIds: string[]
  createdAt: string
  updatedAt: string
}

export interface DeckLine {
  id: string
  oracleId: string
  printingId: string | null
  finish: string | null
  quantity: number
  zone: Zone
  note: string
  tagIds: string[]
  categories: string[]
}

export interface DeckCard {
  name: string
  layout: string
  manaValue: number
  curveManaValue: number
  manaCost: string | null
  typeLine: string | null
  colorIdentity: string[]
  cardTypes: string[]
  isLand: boolean
  defaultPrintingId: string | null
  faces: { name: string; manaCost: string | null; typeLine: string | null }[]
}

export interface DeckPrinting {
  setCode: string
  collectorNumber: string
  lang: string
  imageUri: string | null
}

export interface Deck {
  id: string
  ownerId: string
  name: string
  format: string
  visibility: Visibility
  folderId: string | null
  description: string
  sourceDeckId: string | null
  pinned: boolean
  archived: boolean
  version: number
  createdAt: string
  updatedAt: string
  isOwner: boolean
  counts: Record<Zone, number>
  lines: DeckLine[]
  cards: Record<string, DeckCard>
  printings: Record<string, DeckPrinting>
  tags: Record<string, { name: string; global: boolean }>
}

export interface LegalityProblem {
  code: string
  oracleId: string | null
  count: number | null
  limit: number | null
}

export interface DeckLegality {
  format: string
  legal: boolean
  problems: LegalityProblem[]
}

export interface DeckTextLineError {
  line: number
  code: string
  text: string
}

/** Gets the image of a line: the image of its printing, or of the default printing of its card. */
export function lineImage(deck: Deck, line: DeckLine): string | undefined {
  const printingId = line.printingId ?? deck.cards[line.oracleId]?.defaultPrintingId
  return (printingId && deck.printings[printingId]?.imageUri) || undefined
}

/** Gets the translation key of a format. An unknown format uses the key of freeform. */
export function formatKey(format: string): (typeof deckFormats)[number] {
  return deckFormats.find((known) => known === format) ?? 'freeform'
}
