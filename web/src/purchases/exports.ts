import type { PurchaseList, PurchaseListLine } from '../api/purchases'
import { printImage, type ProxyItem } from '../proxies/selection'

/**
 * The exports of the cards to buy. The functions run in the browser and do not call the API.
 * See docs/architecture/purchase-list.md.
 */

/** The text formats of the export. */
export const exportFormats = ['cardmarket', 'tcgplayer', 'text'] as const
export type ExportFormat = (typeof exportFormats)[number]

/** One card to buy. The lines of the same card in different zones give one item. */
export interface CardToBuy {
  oracleId: string
  /** The English name of the card. */
  name: string
  quantity: number
  /** The printing when all the lines of the card want exactly the same printing. */
  printingId: string | null
  /** The set code of the printing when all the lines of the card want exactly the same printing. */
  setCode: string | null
  /** The collector number of the same printing. */
  collectorNumber: string | null
  /** The highest price of the lines of the card, as text, for example `2.50 EUR`. */
  maximumPrice: string | null
}

/** Gets the printing that a line wants, when the line wants exactly one printing. */
function onlyWantedPrinting(line: PurchaseListLine): string | null {
  const wanted = line.purchasePreferences?.wantedPrintingIds ?? []
  return wanted.length === 1 ? wanted[0] : null
}

/**
 * Gets the cards to buy, in the order of the purchase list.
 * The function adds the quantities of the lines of the same card, for example a card in the main deck and in the sideboard.
 */
export function cardsToBuy(list: PurchaseList): CardToBuy[] {
  const groups = new Map<string, PurchaseListLine[]>()
  for (const line of list.lines) {
    if (line.toBuy <= 0) {
      continue
    }
    groups.set(line.oracleId, [...(groups.get(line.oracleId) ?? []), line])
  }
  return [...groups].map(([oracleId, lines]) => {
    const printingIds = new Set(lines.map(onlyWantedPrinting))
    const [printingId] = printingIds
    const printing = printingIds.size === 1 && printingId ? list.printings[printingId] : undefined
    const prices = lines.map((line) => line.purchasePreferences?.maximumPrice).filter((price) => price != null)
    const price = prices.length === lines.length && new Set(prices.map((value) => value.currency)).size === 1 ? prices.reduce((high, value) => (value.amount > high.amount ? value : high)) : null
    return {
      oracleId,
      name: list.cards[oracleId]?.name ?? oracleId,
      quantity: lines.reduce((total, line) => total + line.toBuy, 0),
      printingId: printing ? printingId : null,
      setCode: printing ? printing.setCode.toUpperCase() : null,
      collectorNumber: printing ? printing.collectorNumber : null,
      maximumPrice: price ? `${price.amount.toFixed(2)} ${price.currency}` : null,
    }
  })
}

/** The wants list of Cardmarket: one line `<quantity> <card name>` for each card. */
export function cardmarketText(cards: readonly CardToBuy[]): string {
  return cards.map((card) => `${card.quantity} ${card.name}`).join('\n')
}

/** The mass entry of TCGplayer: one line `<quantity> <card name>`, with `[SET]` when the card has one printing. */
export function tcgplayerText(cards: readonly CardToBuy[]): string {
  return cards.map((card) => `${card.quantity} ${card.name}${card.setCode ? ` [${card.setCode}]` : ''}`).join('\n')
}

/** The plain text: one line for each card, with the printing and the highest price when the line has them. */
export function plainText(cards: readonly CardToBuy[]): string {
  return cards
    .map((card) => {
      const details = [card.setCode ? `${card.setCode} ${card.collectorNumber ?? ''}`.trim() : null, card.maximumPrice ? `max ${card.maximumPrice}` : null].filter(Boolean)
      return `${card.quantity} ${card.name}${details.length > 0 ? ` (${details.join(', ')})` : ''}`
    })
    .join('\n')
}

/** Gets the text of an export format. The text ends with a line break, so that the file is a valid text file. */
export function exportText(format: ExportFormat, cards: readonly CardToBuy[]): string {
  const text = format === 'cardmarket' ? cardmarketText(cards) : format === 'tcgplayer' ? tcgplayerText(cards) : plainText(cards)
  return text ? `${text}\n` : ''
}

/** Gets the file name of an export, with no character that a file system refuses. */
export function exportFileName(deckName: string, format: ExportFormat): string {
  const safe = [...deckName]
    .map((character) => (character.charCodeAt(0) < 32 || '\\/:*?"<>|'.includes(character) ? ' ' : character))
    .join('')
    .replace(/\s+/g, ' ')
    .trim()
  return safe ? `${safe} - purchases - ${format}.txt` : `purchases - ${format}.txt`
}

/** Makes the proxy list of the cards to buy: the image of the wanted printing, or of the default printing of the card. */
export function purchaseProxyItems(list: PurchaseList, cards: readonly CardToBuy[]): ProxyItem[] {
  return cards.map((card) => {
    const printingId = card.printingId ?? list.cards[card.oracleId]?.defaultPrintingId
    const imageUri = printingId ? (list.printings[printingId]?.imageUri ?? undefined) : undefined
    return { imageUri: printImage(imageUri), quantity: card.quantity, name: card.name }
  })
}
