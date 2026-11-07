/** A face of a card. */
export interface CardFace {
  name: string
  manaCost: string | null
  manaValue: number
  typeLine: string | null
  oracleText: string | null
  colors: string[]
  power: string | null
  toughness: string | null
  loyalty: string | null
  defense: string | null
}

/** A short form of an Oracle card, for the lists. */
export interface CardSummary {
  id: string
  name: string
  layout: string
  manaCost: string | null
  manaValue: number
  typeLine: string | null
  oracleText: string | null
  colors: string[]
  colorIdentity: string[]
  faces: CardFace[]
}

export interface PrintingFace {
  name: string
  printedName: string | null
  imageUris: Record<string, string>
}

/** A short form of a printing, for the lists. */
export interface PrintingSummary {
  id: string
  setCode: string
  setName: string
  collectorNumber: string
  lang: string
  releasedAt: string
  rarity: string
  printedName: string | null
  imageUris: Record<string, string>
  faces: PrintingFace[]
}

export interface SearchWarning {
  code: string
  message: string
  position: number
  term: string
}

export interface CardSearchResponse {
  items: { card: CardSummary; printing: PrintingSummary }[]
  nextCursor: string | null
  total: number
  warnings: SearchWarning[]
}

export interface Printing extends PrintingSummary {
  cardId: string
  finishes: string[]
  artist: string | null
  prices: Record<string, string>
  externalIds: Record<string, string>
}

export interface CardName {
  name: string
  lang: string
  kind: 'oracle' | 'face' | 'printed' | 'flavor'
}

/** An Oracle card with all its data. */
export interface CardDetail extends CardSummary {
  keywords: string[]
  legalities: Record<string, string>
  reserved: boolean
  gameChanger: boolean
  edhrecRank: number | null
  extra: boolean
  defaultPrinting: Printing | null
  names: CardName[]
}

export interface Ruling {
  source: string
  publishedAt: string
  comment: string
}

export interface Page<T> {
  items: T[]
  nextCursor: string | null
}

export interface AutocompleteItem {
  oracleId: string
  name: string
  matchedName: string
  lang: string
}

/** Gets the address of an image of a printing: the image of the printing, or of its first face. */
export function imageOf(printing: Pick<PrintingSummary, 'imageUris' | 'faces'> | null | undefined, size = 'normal'): string | undefined {
  if (!printing) {
    return undefined
  }
  return printing.imageUris[size] ?? printing.faces[0]?.imageUris[size]
}
