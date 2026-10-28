import type { DeckCard, DeckPrinting, Zone } from './decks'

/**
 * The purchase preferences of the deck lines and the purchase list of a deck.
 * See docs/architecture/purchase-list.md.
 */

/** The values of the foil preference. The finish `etched` is a foil finish. */
export const foilPreferences = ['any', 'foil', 'nonfoil'] as const
export type FoilPreference = (typeof foilPreferences)[number]

/** The reasons why a copy of the collection does not fit a line. */
export const purchaseReasons = ['printing', 'language', 'finish', 'condition', 'unavailable'] as const
export type PurchaseReason = (typeof purchaseReasons)[number]

/** The highest price of a purchase. */
export interface PriceLimit {
  amount: number
  /** The ISO 4217 currency code, for example `EUR`. */
  currency: string
}

/** The purchase preferences of a deck line. The API can send null for an empty field. */
export interface PurchasePreferences {
  wantedPrintingIds?: string[] | null
  avoidedPrintingIds?: string[] | null
  languages?: string[] | null
  minimumCondition?: string | null
  foil?: FoilPreference | null
  maximumPrice?: PriceLimit | null
}

/** Copies of a card in one location entry of the collection. */
export interface PurchaseCopy {
  locationId: string
  locationName: string
  sectionName: string | null
  locationKind: string
  /** The user must unlock a locked location before the cards move. */
  locked: boolean
  printingId: string | null
  language: string
  finish: string | null
  condition: string | null
  quantity: number
  /** For a closest copy: why the copy does not fit the line. */
  reasons: string[]
}

/** One line of the purchase list. */
export interface PurchaseListLine {
  oracleId: string
  zone: Zone
  quantity: number
  purchasePreferences: PurchasePreferences | null
  inDeck: number
  fromCollection: PurchaseCopy[]
  toBuy: number
  closest: PurchaseCopy[]
}

/** The response of `GET /decks/{id}/purchase-list`. */
export interface PurchaseList {
  deckId: string
  includeTokens: boolean
  lines: PurchaseListLine[]
  totalToBuy: number
  cards: Record<string, DeckCard>
  printings: Record<string, DeckPrinting>
}

/** The currencies that the preference dialog offers. */
export const currencies = ['EUR', 'USD', 'GBP', 'CAD', 'CHF', 'JPY'] as const

/** Gets the address of the purchase list page of a deck or a variant. */
export function purchaseListPath(deckId: string): string {
  return `/decks/${deckId}/purchase-list`
}

/** Tells whether the preferences limit the copies of a line. */
export function hasPreferences(preferences: PurchasePreferences | null | undefined): boolean {
  if (!preferences) {
    return false
  }
  return (
    (preferences.wantedPrintingIds?.length ?? 0) > 0 ||
    (preferences.avoidedPrintingIds?.length ?? 0) > 0 ||
    (preferences.languages?.length ?? 0) > 0 ||
    Boolean(preferences.minimumCondition) ||
    (Boolean(preferences.foil) && preferences.foil !== 'any') ||
    Boolean(preferences.maximumPrice)
  )
}

/** Gets the known reasons of a copy, in the order of `purchaseReasons`. The other values are not in the result. */
export function knownReasons(copy: PurchaseCopy): PurchaseReason[] {
  return purchaseReasons.filter((reason) => copy.reasons.includes(reason))
}

/** Gets the name of the location of a copy, with the name of its section. */
export function copyPlace(copy: PurchaseCopy): string {
  return copy.sectionName ? `${copy.locationName} › ${copy.sectionName}` : copy.locationName
}
