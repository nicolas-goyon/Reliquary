import type { CountEntry, CurvePoint } from './analysis'
import { zones, type DeckCard, type DeckPrinting, type Zone } from './decks'
import type { PurchasePreferences } from './purchases'

/**
 * The deck versions, the variants and the comparison of two lists.
 * See docs/decisions/0033-deck-versions-and-variants.md and docs/architecture/decks.md.
 */

/** The kinds of a version. */
export const versionKinds = ['commit', 'merge', 'revert', 'restore'] as const
export type VersionKind = (typeof versionKinds)[number]

/** The error code of a commit when the working list did not change since the last version. */
export const noChangesCode = 'deck_no_changes'

/** The largest number of characters in the message of a version. */
export const maxMessageLength = 500

/** The largest number of characters in the name of a variant. */
export const maxVariantNameLength = 100

/** A version of a deck: an item of `GET /decks/{id}/versions`. */
export interface DeckVersion {
  id: string
  number: number
  kind: string
  message: string
  parentIds: string[]
  createdAt: string
  cardCount: number
  assembled: boolean
}

/** The history of a deck: the response of `GET /decks/{id}/versions`. */
export interface DeckHistory {
  /** The versions, the most recent first. */
  versions: DeckVersion[]
  workingListChanged: boolean
  headVersionId: string | null
  assembledVersionId: string | null
}

/** A frozen line of a version. */
export interface DeckVersionLine {
  oracleId: string
  printingId: string | null
  finish: string | null
  quantity: number
  zone: string
  note: string
  categories: string[]
  tags: string[]
  globalTags: string[]
  /** The purchase preferences, or null when the line accepts each copy. */
  purchasePreferences?: PurchasePreferences | null
}

/** A version with its lines: the response of `GET /decks/{id}/versions/{versionId}`. */
export interface DeckVersionDetail {
  version: DeckVersion
  lines: DeckVersionLine[]
  cards: Record<string, DeckCard>
  printings: Record<string, DeckPrinting>
}

export interface QuantityChange {
  oracleId: string
  zone: string
  before: number
  after: number
}

export interface PrintingCopies {
  printingId: string | null
  finish: string | null
  quantity: number
}

export interface PrintingChange {
  oracleId: string
  zone: string
  before: PrintingCopies[]
  after: PrintingCopies[]
}

export interface NoteChange {
  oracleId: string
  zone: string
  before: string
  after: string
}

/** The differences from a first list to a second list. */
export interface ListChanges {
  quantities: QuantityChange[]
  printings: PrintingChange[]
  notes: NoteChange[]
  cards: Record<string, DeckCard>
  printingData: Record<string, DeckPrinting>
}

/** A variant of a deck: an item of `GET /decks/{id}/variants`. */
export interface DeckVariant {
  id: string
  name: string
  baseVersionId: string | null
  headVersionId: string | null
  updatedAt: string
}

/** One list of a comparison: the working list of a deck, or one of its versions. */
export interface ListReference {
  deckId: string
  versionId: string | null
}

/** A list that the user chooses on the comparison page: a main deck, the main deck or one of its variants, and a version or the working list. */
export interface ListChoice {
  mainId: string
  /** The main deck or one of its variants. */
  listId: string
  /** A version of the list, or null for the working list. */
  versionId: string | null
}

/** The numbers of one list in a comparison. */
export interface ListSummary {
  cardCount: number
  landCount: number
  averageManaValue: number
  manaCurve: CurvePoint[]
  types: CountEntry[]
  categories: CountEntry[]
}

/** The response of `POST /lists/compare`. */
export interface ListComparison {
  changes: ListChanges
  left: ListSummary
  right: ListSummary
}

/** Gets the translation key of a version kind. An unknown kind uses the key of a commit. */
export function versionKindKey(kind: string): VersionKind {
  return versionKinds.find((known) => known === kind) ?? 'commit'
}

/** Gets the zone of a change. An unknown zone uses the main deck. */
export function zoneOf(value: string): Zone {
  return zones.find((zone) => zone === value) ?? 'main'
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * Reads a list reference of the comparison page: "deckId" for the working list, or "deckId:versionId" for a version.
 * @returns The reference, or null when the value is not correct.
 */
export function parseListReference(value: string | null): ListReference | null {
  const [deckId, versionId, ...rest] = (value ?? '').split(':')
  if (rest.length > 0 || !deckId || !guidPattern.test(deckId) || (versionId !== undefined && !guidPattern.test(versionId))) {
    return null
  }
  return { deckId, versionId: versionId ?? null }
}

/** Writes a list reference for the query of the comparison page. */
export function listReferenceParam(reference: ListReference): string {
  return reference.versionId ? `${reference.deckId}:${reference.versionId}` : reference.deckId
}

/** Gets the path of the comparison page for two lists. */
export function compareUrl(left: ListReference, right: ListReference): string {
  return `/compare?left=${listReferenceParam(left)}&right=${listReferenceParam(right)}`
}

/** Splits the quantity changes into the added cards and the removed cards. */
export function splitQuantities(changes: QuantityChange[]): { added: QuantityChange[]; removed: QuantityChange[] } {
  return {
    added: changes.filter((change) => change.after > change.before),
    removed: changes.filter((change) => change.after < change.before),
  }
}

/** One row of a delta table: the count in each list. */
export interface DeltaRow {
  key: string
  before: number
  after: number
}

/** Puts two count lists side by side. The order is the order of the first list, then the new keys of the second list. */
export function countDeltas(before: CountEntry[], after: CountEntry[]): DeltaRow[] {
  const keys = [...new Set([...before.map((entry) => entry.key), ...after.map((entry) => entry.key)])]
  return keys.map((key) => ({
    key,
    before: before.find((entry) => entry.key === key)?.count ?? 0,
    after: after.find((entry) => entry.key === key)?.count ?? 0,
  }))
}

/** Puts two mana curves side by side, in the order of the mana values. */
export function curveDeltas(before: CurvePoint[], after: CurvePoint[]): DeltaRow[] {
  const values = [...new Set([...before.map((point) => point.manaValue), ...after.map((point) => point.manaValue)])].sort((left, right) => left - right)
  return values.map((manaValue) => ({
    key: String(manaValue),
    before: before.find((point) => point.manaValue === manaValue)?.count ?? 0,
    after: after.find((point) => point.manaValue === manaValue)?.count ?? 0,
  }))
}

/** Writes a difference with its sign, for example "+2", "−1" or "0". */
export function signed(value: number, language: string): string {
  const text = new Intl.NumberFormat(language, { maximumFractionDigits: 2 }).format(Math.abs(value))
  return value > 0 ? `+${text}` : value < 0 ? `−${text}` : text
}

/** Formats the date and the time of a version in the language of the user. */
export function formatVersionDate(value: string, language: string): string {
  return new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

/** Sorts changes by the zone order of the deck page, then by card name. */
export function sortByZoneAndName<T extends { oracleId: string; zone: string }>(changes: T[], cards: Record<string, DeckCard>): T[] {
  return [...changes].sort(
    (left, right) =>
      zones.indexOf(zoneOf(left.zone)) - zones.indexOf(zoneOf(right.zone)) ||
      (cards[left.oracleId]?.name ?? '').localeCompare(cards[right.oracleId]?.name ?? ''),
  )
}

/** The error code of a transition plan for a deck that has no assembled version. */
export const notAssembledCode = 'deck_not_assembled'

/** Copies of a card in one location entry of the collection. */
export interface TransitionCopy {
  locationId: string
  locationName: string
  sectionName: string | null
  locationKind: string
  locked: boolean
  printingId: string | null
  language: string
  finish: string | null
  condition: string | null
  quantity: number
  /** For a closest copy: printing, language, finish, condition or unavailable. */
  reasons: string[]
}

/** A card to add to the assembled deck. */
export interface TransitionAddition {
  oracleId: string
  quantity: number
  take: TransitionCopy[]
  toBuy: number
  closest: TransitionCopy[]
}

/** The transition plan from the assembled version to the working list of a deck or a variant. */
export interface TransitionPlan {
  deckId: string
  assembledDeckId: string
  assembledVersionId: string
  remove: { oracleId: string; quantity: number }[]
  add: TransitionAddition[]
  unlockFirst: { locationId: string; locationName: string; sectionName: string | null }[]
  totalToBuy: number
  cards: Record<string, DeckCard>
  printings: Record<string, DeckPrinting>
}

/** One step of a transition plan, with a key that stays the same while the plan does not change. */
export type TransitionStep =
  | { key: string; kind: 'remove'; oracleId: string; quantity: number }
  | { key: string; kind: 'take'; oracleId: string; quantity: number; copy: TransitionCopy }
  | { key: string; kind: 'buy'; oracleId: string; quantity: number; closest: TransitionCopy[] }

/** Gives the steps of a transition plan: the cards to remove, then the cards to take by location, then the cards to buy. */
export function transitionSteps(plan: TransitionPlan): TransitionStep[] {
  const name = (oracleId: string) => plan.cards[oracleId]?.name ?? oracleId
  const byName = <T extends { oracleId: string }>(left: T, right: T) => name(left.oracleId).localeCompare(name(right.oracleId))
  const removals = plan.remove.map((line): TransitionStep => ({ key: `remove:${line.oracleId}`, kind: 'remove', oracleId: line.oracleId, quantity: line.quantity })).sort(byName)
  const takes = plan.add
    .flatMap((line) =>
      line.take.map(
        (copy, index): TransitionStep => ({ key: `take:${line.oracleId}:${copy.locationId}:${index}`, kind: 'take', oracleId: line.oracleId, quantity: copy.quantity, copy }),
      ),
    )
    .sort((left, right) => {
      const leftCopy = left.kind === 'take' ? left.copy : null
      const rightCopy = right.kind === 'take' ? right.copy : null
      const location = (leftCopy?.locationName ?? '').localeCompare(rightCopy?.locationName ?? '') || (leftCopy?.sectionName ?? '').localeCompare(rightCopy?.sectionName ?? '')
      return location || byName(left, right)
    })
  const purchases = plan.add
    .filter((line) => line.toBuy > 0)
    .map((line): TransitionStep => ({ key: `buy:${line.oracleId}`, kind: 'buy', oracleId: line.oracleId, quantity: line.toBuy, closest: line.closest }))
    .sort(byName)
  return [...removals, ...takes, ...purchases]
}
