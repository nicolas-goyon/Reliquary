import { lineImage, zones, type Deck, type DeckLine, type Zone } from '../api/decks'

/**
 * The layout of the deck image: a PNG file that a user posts on Discord or on a forum.
 * This module has no drawing code, so the tests check the layout without a canvas. See docs/decisions/0046-deck-image-and-embed.md.
 */

/** The two styles of the image: the commander art with a list grouped by card type, or a grid of the card images. */
export const deckImageStyles = ['list', 'grid'] as const
export type DeckImageStyle = (typeof deckImageStyles)[number]

/** The type groups of the list, in the order of the image. A card goes in the first group that agrees with its types. */
export const typeGroups = ['creature', 'planeswalker', 'battle', 'instant', 'sorcery', 'artifact', 'enchantment', 'land', 'other'] as const
export type TypeGroup = (typeof typeGroups)[number]

/** The zones in the image. The considering zone and the tokens are outside the deck, so the image does not show them. */
export const imageZones: readonly Zone[] = ['commander', 'companion', 'main', 'sideboard']

export interface ImageLine {
  oracleId: string
  name: string
  quantity: number
  imageUri: string | undefined
}

export interface ImageGroup {
  /** A zone for the commander, companion and sideboard zones, or a type group for the main zone. */
  key: Zone | TypeGroup
  count: number
  lines: ImageLine[]
}

/** The width of the image in pixels. Discord shows images up to this width with no reduction. */
export const imageWidth = 1600

/** The size of a card in the grid: the ratio of a card, 63 × 88 mm. */
export const gridCard = { width: 176, height: 246, gap: 12 } as const

/** The number of cards in each row of the grid. */
export const gridColumns = 8

/** The maximum number of distinct cards in the grid, so the image stays smaller than 8 MB, the limit of Discord. */
export const maxGridCards = 120

/** The height of one line of the list, and the space before a group. */
export const listMetrics = { lineHeight: 30, groupGap: 18, headingHeight: 38, columnGap: 40, columns: 3 } as const

/** The height of the band at the top, with the deck name and the commander art. */
export const headerHeight = 260

/** The margin around the content. */
export const margin = 48

/** Tells whether the key of a group is a zone. The other groups are the type groups of the main deck. */
export function isZoneGroup(key: Zone | TypeGroup): key is Zone {
  return zones.some((zone) => zone === key)
}

/** Gets the type group of a card from its card types. */
export function typeGroup(cardTypes: readonly string[]): TypeGroup {
  const lower = cardTypes.map((type) => type.toLowerCase())
  return typeGroups.find((group) => lower.includes(group)) ?? 'other'
}

function imageLine(deck: Deck, line: DeckLine): ImageLine {
  return { oracleId: line.oracleId, name: deck.cards[line.oracleId]?.name ?? line.oracleId, quantity: line.quantity, imageUri: lineImage(deck, line) }
}

function byName(left: ImageLine, right: ImageLine): number {
  return left.name.localeCompare(right.name)
}

function group(key: Zone | TypeGroup, lines: ImageLine[]): ImageGroup {
  return { key, count: lines.reduce((total, line) => total + line.quantity, 0), lines: [...lines].sort(byName) }
}

/**
 * Groups the lines of the deck for the list: the commanders, then the main deck by card type, then the companion and the sideboard.
 * An empty group is not in the result.
 */
export function imageGroups(deck: Deck): ImageGroup[] {
  const lines = deck.lines.filter((line) => line.quantity > 0)
  const ofZone = (zone: Zone) => lines.filter((line) => line.zone === zone).map((line) => imageLine(deck, line))
  const main = lines.filter((line) => line.zone === 'main')
  const groups: ImageGroup[] = [group('commander', ofZone('commander'))]
  for (const key of typeGroups) {
    groups.push(group(key, main.filter((line) => typeGroup(deck.cards[line.oracleId]?.cardTypes ?? []) === key).map((line) => imageLine(deck, line))))
  }
  groups.push(group('companion', ofZone('companion')), group('sideboard', ofZone('sideboard')))
  return groups.filter((value) => value.lines.length > 0)
}

/** Gets the card images of the commanders, for the art of the band at the top. */
export function commanderImages(deck: Deck): string[] {
  return deck.lines
    .filter((line) => line.zone === 'commander')
    .map((line) => lineImage(deck, line))
    .filter((uri): uri is string => Boolean(uri))
}

/** Gets the cards of the grid: one card for each line of the image zones, in the order of the zones, then by name. */
export function gridLines(deck: Deck): ImageLine[] {
  return zones
    .filter((zone) => imageZones.includes(zone))
    .flatMap((zone) =>
      deck.lines
        .filter((line) => line.zone === zone && line.quantity > 0)
        .map((line) => imageLine(deck, line))
        .sort(byName),
    )
    .slice(0, maxGridCards)
}

export interface ListPlacement {
  group: ImageGroup
  column: number
  /** The top of the group heading, from the top of the content area. */
  y: number
}

/**
 * Puts the groups of the list in columns. A group stays in one column.
 * The function fills the columns from left to right, and it keeps the columns at about the same height.
 * @returns The place of each group and the height of the content.
 */
export function placeGroups(groups: readonly ImageGroup[], columns: number = listMetrics.columns): { placements: ListPlacement[]; height: number } {
  const heights = groups.map((value) => listMetrics.headingHeight + value.lines.length * listMetrics.lineHeight)
  const total = heights.reduce((sum, height) => sum + height + listMetrics.groupGap, 0)
  const target = total / columns
  const placements: ListPlacement[] = []
  let column = 0
  let y = 0
  let height = 0
  groups.forEach((value, index) => {
    // Go to the next column when this column is full, but never leave a column empty.
    if (y > 0 && column < columns - 1 && y + heights[index] / 2 > target) {
      column += 1
      y = 0
    }
    placements.push({ group: value, column, y })
    y += heights[index] + listMetrics.groupGap
    height = Math.max(height, y - listMetrics.groupGap)
  })
  return { placements, height }
}

/** Gets the height of the image for a style. */
export function imageHeight(deck: Deck, style: DeckImageStyle): number {
  if (style === 'grid') {
    const rows = Math.max(1, Math.ceil(gridLines(deck).length / gridColumns))
    return headerHeight + margin + rows * gridCard.height + (rows - 1) * gridCard.gap + margin
  }
  return headerHeight + margin + Math.max(listMetrics.lineHeight, placeGroups(imageGroups(deck)).height) + margin
}

/** Gets a file name for the image of a deck, with no character that a file system refuses. */
export function imageFileName(deckName: string): string {
  const safe = [...deckName]
    .map((character) => (character.charCodeAt(0) < 32 || '\\/:*?"<>|'.includes(character) ? ' ' : character))
    .join('')
    .replace(/\s+/g, ' ')
    .trim()
  return safe ? `${safe}.png` : 'deck.png'
}
