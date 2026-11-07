/**
 * The filters of the card search panel, and the conversion between the filters and the query text.
 * The panel writes only keywords that the search parser of the Cards component supports.
 */

export const colorLetters = ['w', 'u', 'b', 'r', 'g'] as const
export type ColorLetter = (typeof colorLetters)[number]

/** How a color filter compares the colors of a card with the selected colors. */
export const colorModes = ['including', 'exactly', 'atMost'] as const
export type ColorMode = (typeof colorModes)[number]

export const cardTypes = ['creature', 'instant', 'sorcery', 'artifact', 'enchantment', 'planeswalker', 'land', 'battle'] as const
export type CardType = (typeof cardTypes)[number]

export const manaValueOperators = ['=', '<', '>', '<=', '>='] as const
export type ManaValueOperator = (typeof manaValueOperators)[number]

export const rarities = ['common', 'uncommon', 'rare', 'mythic', 'special', 'bonus'] as const
export type Rarity = (typeof rarities)[number]

/** The formats of the panel. The keys of "formats" in the cards namespace give their names. */
export const filterFormats = [
  'standard', 'pioneer', 'modern', 'legacy', 'vintage', 'pauper', 'commander', 'brawl', 'oathbreaker', 'paupercommander',
] as const
export type FilterFormat = (typeof filterFormats)[number]

export interface ColorFilter {
  mode: ColorMode
  colors: ColorLetter[]
}

export interface SearchFilters {
  /** The colors of the card. Empty colors give no term. */
  color: ColorFilter
  /** The color identity of the card. Empty colors give no term. */
  identity: ColorFilter
  types: CardType[]
  /** The mana value. An empty value gives no term. */
  manaValue: { operator: ManaValueOperator; value: string }
  rarity: Rarity | ''
  format: FilterFormat | ''
  /** The set code. An empty code gives no term. */
  set: string
}

export function emptyFilters(): SearchFilters {
  return {
    color: { mode: 'including', colors: [] },
    identity: { mode: 'atMost', colors: [] },
    types: [],
    manaValue: { operator: '=', value: '' },
    rarity: '',
    format: '',
    set: '',
  }
}

/** The filters that a query contains, and the terms of the query that the panel does not read. */
export interface ParsedQuery {
  filters: SearchFilters
  /** Which filters the query sets. A filter that the query does not set keeps the choice of the user in the panel. */
  present: { color: boolean; identity: boolean; manaValue: boolean }
  /** The other terms, in their order and with their text. */
  rest: string[]
}

/**
 * Splits a query into terms at the spaces.
 * A term keeps its quotation marks, its regular expression between slashes and its parentheses, with their spaces.
 */
export function splitTerms(query: string): string[] {
  const terms: string[] = []
  let current = ''
  let depth = 0
  let quoted = false
  let regex = false
  for (let index = 0; index < query.length; index++) {
    const character = query[index]
    const previous = current.at(-1)
    if (quoted) {
      quoted = character !== '"'
    } else if (regex) {
      if (character === '\\' && index + 1 < query.length) {
        current += character + query[++index]
        continue
      }
      regex = character !== '/'
    } else if (character === '"') {
      quoted = true
    } else if (character === '/' && (previous === undefined || /[:=<>!(-]/.test(previous))) {
      regex = true
    } else if (character === '(') {
      depth++
    } else if (character === ')') {
      depth = Math.max(0, depth - 1)
    } else if (/\s/.test(character) && depth === 0) {
      if (current) {
        terms.push(current)
      }
      current = ''
      continue
    }
    current += character
  }
  if (current) {
    terms.push(current)
  }
  return terms
}

const colorPattern = /^(c|color|colors|id|identity|ci)(:|=|>=|<=)([wubrg]+)$/i
const typePattern = /^(t|type):([a-z]+)$/i
const manaValuePattern = /^(mv|manavalue|cmc)(:|=|<=|>=|<|>)(\d+(?:\.\d+)?)$/i
const rarityPattern = /^(r|rarity)(:|=)([a-z]+)$/i
const formatPattern = /^(f|format|legal):([a-z]+)$/i
const setPattern = /^(s|e|set|edition)(:|=)([a-z0-9]+)$/i

const rarityLetters: Record<string, Rarity> = { c: 'common', u: 'uncommon', r: 'rare', m: 'mythic', s: 'special', b: 'bonus' }

function isOneOf<T extends string>(values: readonly T[], value: string): value is T {
  return (values as readonly string[]).includes(value)
}

function colorMode(identity: boolean, operator: string): ColorMode {
  if (operator === '=') {
    return 'exactly'
  }
  if (operator === '<=') {
    return 'atMost'
  }
  if (operator === '>=') {
    return 'including'
  }
  // The colon means "including" for the colors and "at most" for the color identity, as on Scryfall.
  return identity ? 'atMost' : 'including'
}

function sortedColors(letters: string): ColorLetter[] {
  return colorLetters.filter((letter) => letters.includes(letter))
}

/**
 * Reads the filters of the panel from a query.
 * The panel reads only the simple terms that it writes. All the other terms stay in the rest, with their text.
 * When the query has an "or" outside parentheses, the panel reads no term, because the terms are not all required.
 */
export function parseQuery(query: string): ParsedQuery {
  const filters = emptyFilters()
  const present = { color: false, identity: false, manaValue: false }
  const terms = splitTerms(query)
  if (terms.some((term) => term.toLowerCase() === 'or')) {
    return { filters, present, rest: terms }
  }

  const rest: string[] = []
  for (const term of terms) {
    if (!readTerm(term.toLowerCase(), filters, present)) {
      rest.push(term)
    }
  }
  return { filters, present, rest }
}

/** Puts one term into the filters. Returns false when the panel does not read the term, or already has a value for it. */
function readTerm(term: string, filters: SearchFilters, present: ParsedQuery['present']): boolean {
  let match = colorPattern.exec(term)
  if (match) {
    const identity = match[1].startsWith('i') || match[1] === 'ci'
    const key = identity ? 'identity' : 'color'
    if (present[key]) {
      return false
    }
    present[key] = true
    filters[key] = { mode: colorMode(identity, match[2]), colors: sortedColors(match[3]) }
    return true
  }

  match = typePattern.exec(term)
  if (match) {
    const type = match[2]
    if (!isOneOf(cardTypes, type) || filters.types.includes(type)) {
      return false
    }
    filters.types.push(type)
    return true
  }

  match = manaValuePattern.exec(term)
  if (match) {
    if (present.manaValue) {
      return false
    }
    present.manaValue = true
    filters.manaValue = { operator: match[2] === ':' ? '=' : (match[2] as ManaValueOperator), value: match[3] }
    return true
  }

  match = rarityPattern.exec(term)
  if (match) {
    const rarity = rarityLetters[match[3]] ?? match[3]
    if (filters.rarity || !isOneOf(rarities, rarity)) {
      return false
    }
    filters.rarity = rarity
    return true
  }

  match = formatPattern.exec(term)
  if (match) {
    if (filters.format || !isOneOf(filterFormats, match[2])) {
      return false
    }
    filters.format = match[2]
    return true
  }

  match = setPattern.exec(term)
  if (match) {
    if (filters.set) {
      return false
    }
    filters.set = match[3]
    return true
  }
  return false
}

const colorOperators: Record<'color' | 'identity', Record<ColorMode, string>> = {
  color: { including: ':', exactly: '=', atMost: '<=' },
  identity: { including: '>=', exactly: '=', atMost: ':' },
}

/** Gets the query terms of the filters, in a fixed order. */
export function filterTerms(filters: SearchFilters): string[] {
  const terms: string[] = []
  for (const key of ['color', 'identity'] as const) {
    const { mode, colors } = filters[key]
    if (colors.length > 0) {
      terms.push(`${key === 'color' ? 'c' : 'id'}${colorOperators[key][mode]}${sortedColors(colors.join('')).join('')}`)
    }
  }
  for (const type of cardTypes.filter((type) => filters.types.includes(type))) {
    terms.push(`t:${type}`)
  }
  const manaValue = filters.manaValue.value.trim()
  if (/^\d+(\.\d+)?$/.test(manaValue)) {
    terms.push(`mv${filters.manaValue.operator}${manaValue}`)
  }
  if (filters.rarity) {
    terms.push(`r:${filters.rarity}`)
  }
  if (filters.format) {
    terms.push(`f:${filters.format}`)
  }
  const set = filters.set.trim().toLowerCase()
  if (/^[a-z0-9]+$/.test(set)) {
    terms.push(`s:${set}`)
  }
  return terms
}

/**
 * Writes a query from the other terms and the filters.
 * When the other terms contain an "or" outside parentheses, parentheses go around them, so that the filters apply to all the results.
 */
export function buildQuery(rest: string[], filters: SearchFilters): string {
  const filterPart = filterTerms(filters)
  let restPart = rest.join(' ')
  if (filterPart.length > 0 && rest.some((term) => term.toLowerCase() === 'or')) {
    restPart = `(${restPart})`
  }
  return [restPart, ...filterPart].filter(Boolean).join(' ')
}
