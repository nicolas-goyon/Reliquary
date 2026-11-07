import { zones, type Deck, type DeckLine, type Zone } from '../../api/decks'

/** The columns that the user can sort. */
export const sortKeys = ['name', 'zone', 'quantity', 'manaValue', 'type'] as const
export type SortKey = (typeof sortKeys)[number]

export interface TableSort {
  key: SortKey
  direction: 'ascending' | 'descending'
}

/** The filters of the table. An empty value shows all the lines. */
export interface TableFilters {
  zone: Zone | ''
  /** "tag:<tag ID>" or "category:<category>". */
  label: string
  /** A part of the card name. The comparison ignores the case. */
  text: string
}

export const noFilters: TableFilters = { zone: '', label: '', text: '' }

/** The body of POST /decks/{id}/lines/group. */
export interface LineGroupBody {
  lineIds: string[]
  action: 'move' | 'tag' | 'untag' | 'printing' | 'quantity' | 'remove'
  zone?: Zone
  tags?: string[]
  globalTags?: string[]
  categories?: string[]
  quantityDelta?: number
  finish?: string
  clearPrinting?: boolean
  expectedVersion?: number
}

/** The body of a group action, without the lines and the version. The table adds them. */
export type GroupChange = Omit<LineGroupBody, 'lineIds' | 'expectedVersion'>

/** Gets the name of the card of a line, or its Oracle ID when the deck has no data for the card. */
export function lineName(deck: Deck, line: DeckLine): string {
  return deck.cards[line.oracleId]?.name ?? line.oracleId
}

function compare(deck: Deck, key: SortKey, left: DeckLine, right: DeckLine): number {
  const leftCard = deck.cards[left.oracleId]
  const rightCard = deck.cards[right.oracleId]
  switch (key) {
    case 'zone':
      return zones.indexOf(left.zone) - zones.indexOf(right.zone)
    case 'quantity':
      return left.quantity - right.quantity
    case 'manaValue':
      return (leftCard?.manaValue ?? 0) - (rightCard?.manaValue ?? 0)
    case 'type':
      return (leftCard?.typeLine ?? '').localeCompare(rightCard?.typeLine ?? '')
    case 'name':
      return 0
  }
}

/** Gets the lines that the filters accept, in the order of the sort. Lines with the same value use the card name. */
export function visibleLines(deck: Deck, filters: TableFilters, sort: TableSort): DeckLine[] {
  const text = filters.text.trim().toLocaleLowerCase()
  const [labelKind, labelValue] = filters.label.split(/:(.*)/s)
  const sign = sort.direction === 'ascending' ? 1 : -1
  return deck.lines
    .filter((line) => !filters.zone || line.zone === filters.zone)
    .filter((line) => !filters.label || (labelKind === 'tag' ? line.tagIds.includes(labelValue) : line.categories.includes(labelValue)))
    .filter((line) => !text || lineName(deck, line).toLocaleLowerCase().includes(text))
    .sort((left, right) => sign * (compare(deck, sort.key, left, right) || lineName(deck, left).localeCompare(lineName(deck, right))))
}

export interface SelectionClick {
  /** Shift: select the lines from the last clicked line to this line. */
  range: boolean
  /** Ctrl or Cmd, or a click on the checkbox: add or remove this line only. */
  toggle: boolean
}

export interface Selection {
  ids: ReadonlySet<string>
  anchor: string | null
}

export const emptySelection: Selection = { ids: new Set(), anchor: null }

/**
 * Applies a click on a line to the selection.
 * A click selects only the line. Shift+click selects the range from the anchor. Ctrl+click or Cmd+click toggles the line.
 * @param order The IDs of the visible lines, in the order of the table.
 */
export function clickLine(selection: Selection, order: readonly string[], id: string, click: SelectionClick): Selection {
  const anchor = selection.anchor && order.includes(selection.anchor) ? selection.anchor : null
  if (click.range && anchor) {
    const from = order.indexOf(anchor)
    const to = order.indexOf(id)
    const range = order.slice(Math.min(from, to), Math.max(from, to) + 1)
    return { ids: new Set(click.toggle ? [...selection.ids, ...range] : range), anchor }
  }
  if (click.toggle) {
    const ids = new Set(selection.ids)
    if (ids.has(id)) {
      ids.delete(id)
    } else {
      ids.add(id)
    }
    return { ids, anchor: id }
  }
  return { ids: new Set([id]), anchor: id }
}
