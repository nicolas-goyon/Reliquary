import type { SlotContexts, SlotContribution } from '../slots/registry'

/**
 * The analysis of a deck: the response of `GET /api/v1/decks/{id}/analysis`.
 * See docs/architecture/deck-analysis.md.
 */
export interface DeckAnalysis {
  format: string
  cardCount: number
  landCount: number
  modalLandCount: number
  spellCount: number
  averageManaValue: number
  manaCurve: CurvePoint[]
  types: CountEntry[]
  colors: CountEntry[]
  categories: CountEntry[]
  mana: ManaAnalysis
  draws: DrawAnalysis
  needs: CategoryNeed[] | null
  bracket: BracketAnalysis | null
  combos: ComboAnalysis
}

export interface CurvePoint {
  manaValue: number
  count: number
}

export interface CountEntry {
  key: string
  count: number
}

export interface ManaAnalysis {
  tableDeckSize: number
  colors: ColorMana[]
}

export interface ColorMana {
  color: string
  symbols: number
  landSources: number
  otherSources: number
  sources: number
  required: number | null
  oracleId: string | null
  pips: number | null
  manaValue: number | null
  status: string
}

export interface DrawAnalysis {
  librarySize: number
  lands: number
  checkpoints: DrawCheckpoint[]
}

export interface DrawCheckpoint {
  code: string
  turn: number
  onThePlay: boolean
  cards: number
  atLeast: { lands: number; probability: number }[]
}

export interface CategoryNeed {
  category: string
  count: number
  target: number | null
  min: number | null
  max: number | null
  status: string
  difference: number | null
}

export interface BracketAnalysis {
  bracket: number
  reasons: BracketReason[]
  unchecked: string[]
}

export interface BracketReason {
  code: string
  bracket: number
  oracleIds: string[]
  /** The IDs of the combos of the reason on Commander Spellbook. The list is empty for a reason that is not a combo. */
  comboIds: string[]
}

/** The combos of Commander Spellbook in the deck. See docs/decisions/0030-combos-from-commander-spellbook.md. */
export interface ComboAnalysis {
  /** False when the instance has no combo data. The lists are then empty. */
  available: boolean
  complete: DeckCombo[]
  /** The combos that need one more card, from the most popular. The list has 50 combos at most. */
  near: DeckCombo[]
  nearCount: number
}

export interface DeckCombo {
  id: string
  url: string
  cards: DeckComboCard[]
  /** The results, in English, as Commander Spellbook gives them. */
  results: string[]
  gameEnding: boolean
  manaNeeded: string | null
  manaValue: number
  missingOracleId: string | null
}

export interface DeckComboCard {
  oracleId: string
  name: string
  quantity: number
  inDeck: boolean
}

/** The home page of Commander Spellbook, for the credit of the combo data. */
export const commanderSpellbookUrl = 'https://commanderspellbook.com/'

/** Accepts only the pages of Commander Spellbook, so the page never shows a link to another site from the API data. */
export function comboUrl(combo: DeckCombo): string {
  return combo.url.startsWith(commanderSpellbookUrl) ? combo.url : `${commanderSpellbookUrl}combo/${encodeURIComponent(combo.id)}/`
}

/** Finds a known code in a list. An unknown code gives the fallback, so a new code of the API does not break the page. */
function known<const Codes extends readonly string[], Fallback extends string>(codes: Codes, code: string, fallback: Fallback): Codes[number] | Fallback {
  return codes.find((candidate) => candidate === code) ?? fallback
}

export const typeKeys = ['artifact', 'battle', 'creature', 'enchantment', 'instant', 'kindred', 'land', 'planeswalker', 'sorcery'] as const
export const colorKeys = ['W', 'U', 'B', 'R', 'G', 'C'] as const
export const categoryKeys = ['ramp', 'card_draw', 'targeted_interaction', 'board_wipe', 'synergy', 'land', 'win_condition', 'protection', 'uncategorized'] as const
export const manaStatuses = ['ok', 'low', 'no_requirement'] as const
export const needStatuses = ['low', 'ok', 'high', 'no_target'] as const
export const checkpointCodes = ['opening_hand', 'turn_2_play', 'turn_2_draw', 'turn_3_play', 'turn_3_draw', 'turn_4_play', 'turn_4_draw'] as const
export const reasonCodes = ['game_changers', 'mass_land_denial', 'extra_turns', 'two_card_combos', 'late_two_card_combos'] as const
export const uncheckedCodes = ['two_card_combos'] as const

export const typeKey = (key: string) => known(typeKeys, key, 'other')
export const colorKey = (key: string) => known(colorKeys, key, 'C')
export const categoryKey = (key: string) => known(categoryKeys, key, 'uncategorized')
export const manaStatus = (status: string) => known(manaStatuses, status, 'no_requirement')
export const needStatus = (status: string) => known(needStatuses, status, 'no_target')
export const checkpointCode = (code: string) => known(checkpointCodes, code, 'other')
export const reasonCode = (code: string) => known(reasonCodes, code, 'other')
export const uncheckedCode = (code: string) => known(uncheckedCodes, code, 'other')

const numberFormats = new Map<string, Intl.NumberFormat>()

/** Gets a number format from the cache, because a table formats many cells. */
function numberFormat(language: string, kind: 'percent' | 'decimal'): Intl.NumberFormat {
  const key = `${language}|${kind}`
  let format = numberFormats.get(key)
  if (!format) {
    format = kind === 'percent'
      ? new Intl.NumberFormat(language, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 })
      : new Intl.NumberFormat(language, { maximumFractionDigits: 2 })
    numberFormats.set(key, format)
  }
  return format
}

/** Formats a probability from 0 to 1 as a percentage with one decimal. */
export function formatPercent(probability: number, language: string): string {
  return numberFormat(language, 'percent').format(probability)
}

/** Formats a number with two decimals or fewer, for example an average mana value or a number of sources. */
export function formatDecimal(value: number, language: string): string {
  return numberFormat(language, 'decimal').format(value)
}

/** Gets the largest count of a list, with 1 as the lowest result, so a chart can divide by it. */
export function largestCount(values: readonly number[]): number {
  return Math.max(1, ...values)
}

/** Gets the tone of a status, for its color: good, bad, warn or none. The text of the status always gives the same information. */
export function statusTone(status: string): 'good' | 'bad' | 'warn' | 'none' {
  switch (status) {
    case 'ok':
      return 'good'
    case 'low':
      return 'bad'
    case 'high':
      return 'warn'
    default:
      return 'none'
  }
}

/**
 * Gets the name of an analysis tab: the title of the contribution, or its ID when it has no title.
 * A title that fails gives the ID, so a broken extension does not stop the page.
 * @returns The name, or null when the contribution hides its tab for this deck.
 */
export function tabTitle(contribution: SlotContribution<'analysis.tabs'>, context: SlotContexts['analysis.tabs']): string | null {
  if (!contribution.title) {
    return contribution.id
  }
  try {
    return contribution.title(context)
  } catch (error) {
    console.error(`The title of the contribution "${contribution.id}" of the slot "analysis.tabs" failed.`, error)
    return contribution.id
  }
}
