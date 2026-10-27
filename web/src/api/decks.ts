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
  tags: string[]
  colorIdentity: string
}

export interface DeckFolder {
  id: string
  parentId: string | null
  name: string
  deckCount: number
}

export interface DeckTagSummary {
  name: string
  deckCount: number
}

/** The group actions on several decks. See docs/architecture/decks.md. */
export const groupActions = ['move', 'tag', 'untag', 'pin', 'unpin', 'archive', 'unarchive', 'delete'] as const
export type GroupAction = (typeof groupActions)[number]

/** The sorts of the deck list. */
export const deckSorts = ['updated', 'created', 'name', 'format'] as const
export type DeckSort = (typeof deckSorts)[number]

/** The colors of a color identity, in the WUBRG order. */
export const identityColors = ['W', 'U', 'B', 'R', 'G'] as const

/** The filters of the deck list. */
export interface DeckListFilters {
  /** A folder ID, `none` for the decks with no folder, or empty for all the decks. */
  folderId: string
  format: string
  /** The letters of the exact color identity, `C` for colorless, or empty for all the colors. */
  colors: string
  tag: string
  archived: 'exclude' | 'include' | 'only'
  sort: DeckSort
}

/** Gets the query of the deck list for the filters. */
export function deckListQuery(filters: DeckListFilters): string {
  const query = new URLSearchParams({ limit: '100' })
  for (const key of ['folderId', 'format', 'colors', 'tag'] as const) {
    if (filters[key]) {
      query.set(key, filters[key])
    }
  }
  if (filters.archived !== 'exclude') {
    query.set('archived', filters.archived)
  }
  if (filters.sort !== 'updated') {
    query.set('sort', filters.sort)
  }
  return query.toString()
}

/** Puts the folders in tree order, with the depth of each folder. A folder at the top has the depth 0. */
export function folderTree(folders: DeckFolder[]): { folder: DeckFolder; depth: number }[] {
  const result: { folder: DeckFolder; depth: number }[] = []
  const visit = (parentId: string | null, depth: number) => {
    for (const folder of folders.filter((candidate) => candidate.parentId === parentId)) {
      result.push({ folder, depth })
      visit(folder.id, depth + 1)
    }
  }
  visit(null, 0)
  return result
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
