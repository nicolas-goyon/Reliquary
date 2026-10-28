import type { PrintingSummary } from '../api/cards'
import { lineImage, zones, type Deck, type DeckLine, type Zone } from '../api/decks'

/**
 * One card of a list of proxies. The deck page makes this list from the deck lines.
 * Other lists, for example the purchase list, can make the same list and use the same PDF function.
 */
export interface ProxyItem {
  /** The address of the card image, or undefined for a card with no image. */
  imageUri: string | undefined
  quantity: number
  /** The card name. The PDF shows it in the frame of a card with no image. */
  name: string
}

/** The zones that "all the cards of the deck" always contains. */
export const deckZones: readonly Zone[] = ['commander', 'companion', 'main']

/** The zones that the user can add to "all the cards of the deck". */
export const optionalZones = ['sideboard', 'considering', 'tokens'] as const satisfies readonly Zone[]
export type OptionalZone = (typeof optionalZones)[number]

/** Gets the larger image of Scryfall for a card image address, for a better print. Other addresses do not change. */
export function printImage(uri: string | undefined): string | undefined {
  return uri?.replace(/^(https:\/\/cards\.scryfall\.io\/)(?:small|normal)\//, '$1large/')
}

/** Puts the lines in the order of the deck page: by zone, then by card name. */
export function orderedLines(deck: Deck): DeckLine[] {
  const name = (line: DeckLine) => deck.cards[line.oracleId]?.name ?? ''
  return [...deck.lines].sort((left, right) => zones.indexOf(left.zone) - zones.indexOf(right.zone) || name(left).localeCompare(name(right)))
}

/** Gets the lines of all the cards of the deck, with the zones that the user added. */
export function linesOfZones(deck: Deck, added: readonly OptionalZone[]): DeckLine[] {
  const included = new Set<Zone>([...deckZones, ...added])
  return orderedLines(deck).filter((line) => included.has(line.zone))
}

/** Gets the lines that the user selected, in the order of the deck page. */
export function selectedLines(deck: Deck, lineIds: ReadonlySet<string>): DeckLine[] {
  return orderedLines(deck).filter((line) => lineIds.has(line.id))
}

/** Makes the proxy list of deck lines. Each line keeps its quantity. */
export function proxyItems(deck: Deck, lines: readonly DeckLine[]): ProxyItem[] {
  return lines
    .filter((line) => line.quantity > 0)
    .map((line) => ({ imageUri: printImage(lineImage(deck, line)), quantity: line.quantity, name: deck.cards[line.oracleId]?.name ?? line.oracleId }))
}

/** Gets one entry for each physical card: a list item with the quantity 3 gives 3 entries. */
export function expandItems(items: readonly ProxyItem[]): ProxyItem[] {
  return items.flatMap((item) => Array.from({ length: Math.max(0, Math.floor(item.quantity)) }, () => ({ ...item, quantity: 1 })))
}

/** Counts the physical cards of a proxy list. */
export function cardTotal(items: readonly ProxyItem[]): number {
  return items.reduce((total, item) => total + Math.max(0, Math.floor(item.quantity)), 0)
}

/** Tells whether a line has a card with more than one face, so that its printing can have a back face image. */
export function hasFaces(deck: Deck, line: DeckLine): boolean {
  return (deck.cards[line.oracleId]?.faces.length ?? 0) > 1
}

/** The part of a printing that the back faces need. */
export type PrintingImages = Pick<PrintingSummary, 'imageUris' | 'faces'>

/**
 * Makes the proxy list of deck lines, with the back face of each double-faced card after its front face.
 * Only a printing with one image for each face has a back face image, for example a transform card or a modal double-faced card.
 * A split card or an adventure card has one image, so it gives one proxy.
 * When the printing does not load, the list contains the front face only.
 */
export async function proxyItemsWithBackFaces(deck: Deck, lines: readonly DeckLine[], getPrinting: (printingId: string) => Promise<PrintingImages>): Promise<ProxyItem[]> {
  const fronts = proxyItems(deck, lines)
  const backs = await Promise.all(
    lines
      .filter((line) => line.quantity > 0)
      .map(async (line): Promise<ProxyItem[]> => {
        const printingId = line.printingId ?? deck.cards[line.oracleId]?.defaultPrintingId
        if (!printingId || !hasFaces(deck, line)) {
          return []
        }
        try {
          const printing = await getPrinting(printingId)
          return printing.faces
            .slice(1)
            .map((face) => ({ imageUri: printImage(face.imageUris.large ?? face.imageUris.normal), quantity: line.quantity, name: face.name }))
            .filter((item) => item.imageUri !== undefined)
        } catch {
          return []
        }
      }),
  )
  return fronts.flatMap((front, index) => [front, ...backs[index]])
}
