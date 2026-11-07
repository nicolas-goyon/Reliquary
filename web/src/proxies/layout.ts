/**
 * The page layout of the proxies: 9 cards on each page (3 × 3), each card at the real size of 63 × 88 mm.
 * All the values are in PDF points. One point is 1/72 inch, thus 1 mm is 72 / 25.4 points.
 */

export const paperSizes = ['a4', 'letter'] as const
export type PaperSize = (typeof paperSizes)[number]

/** Converts millimeters to PDF points. */
export function mmToPoints(mm: number): number {
  return (mm * 72) / 25.4
}

/** The width and the height of each paper size, in points, in portrait orientation. */
export const pageDimensions: Record<PaperSize, { width: number; height: number }> = {
  a4: { width: mmToPoints(210), height: mmToPoints(297) },
  letter: { width: 612, height: 792 },
}

export const cardWidth = mmToPoints(63)
export const cardHeight = mmToPoints(88)
export const columns = 3
export const rows = 3
export const cardsPerPage = columns * rows

/** The distance between the card grid and a cut mark, and the length of a cut mark, in points. */
const markGap = mmToPoints(1)
const markLength = mmToPoints(4)

export interface CardPosition {
  /** The index of the page, from 0. */
  page: number
  /** The left edge of the card, from the left edge of the page. */
  x: number
  /** The top edge of the card, from the top edge of the page. */
  y: number
  width: number
  height: number
}

export interface CutMark {
  x1: number
  y1: number
  x2: number
  y2: number
}

/** Gets the margins that put the card grid in the center of the page. */
export function gridOrigin(paper: PaperSize): { x: number; y: number } {
  const page = pageDimensions[paper]
  return { x: (page.width - columns * cardWidth) / 2, y: (page.height - rows * cardHeight) / 2 }
}

/** Gets the position of each card. The cards fill each page from left to right, then from top to bottom. */
export function layoutCards(count: number, paper: PaperSize): CardPosition[] {
  const origin = gridOrigin(paper)
  return Array.from({ length: count }, (_, index) => {
    const slot = index % cardsPerPage
    return {
      page: Math.floor(index / cardsPerPage),
      x: origin.x + (slot % columns) * cardWidth,
      y: origin.y + Math.floor(slot / columns) * cardHeight,
      width: cardWidth,
      height: cardHeight,
    }
  })
}

/** Gets the number of pages for a number of cards. */
export function pageCount(count: number): number {
  return Math.ceil(count / cardsPerPage)
}

/**
 * Gets the cut marks of a page. The marks are in the margins, at each edge of the card grid,
 * so that they do not cover a card image.
 */
export function cutMarks(paper: PaperSize): CutMark[] {
  const origin = gridOrigin(paper)
  const right = origin.x + columns * cardWidth
  const bottom = origin.y + rows * cardHeight
  const marks: CutMark[] = []
  for (let column = 0; column <= columns; column += 1) {
    const x = origin.x + column * cardWidth
    marks.push({ x1: x, y1: origin.y - markGap - markLength, x2: x, y2: origin.y - markGap })
    marks.push({ x1: x, y1: bottom + markGap, x2: x, y2: bottom + markGap + markLength })
  }
  for (let row = 0; row <= rows; row += 1) {
    const y = origin.y + row * cardHeight
    marks.push({ x1: origin.x - markGap - markLength, y1: y, x2: origin.x - markGap, y2: y })
    marks.push({ x1: right + markGap, y1: y, x2: right + markGap + markLength, y2: y })
  }
  return marks
}

/** Gets the default paper size for a locale: Letter in the United States, A4 in other countries. */
export function defaultPaperSize(locale: string | undefined): PaperSize {
  return locale && /^[a-z]{2,3}-US$/i.test(locale) ? 'letter' : 'a4'
}
