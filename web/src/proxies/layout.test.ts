import { describe, expect, it } from 'vitest'
import { cardHeight, cardWidth, cutMarks, defaultPaperSize, gridOrigin, layoutCards, mmToPoints, pageCount, pageDimensions, paperSizes } from './layout'

describe('proxy layout', () => {
  it('converts millimeters to points', () => {
    expect(mmToPoints(25.4)).toBe(72)
    expect(cardWidth).toBeCloseTo((63 * 72) / 25.4, 6)
    expect(cardHeight).toBeCloseTo((88 * 72) / 25.4, 6)
  })

  it.each(paperSizes)('puts 18 cards on 2 pages of %s, 9 cards on each page, at 63 × 88 mm', (paper) => {
    const positions = layoutCards(18, paper)

    expect(positions).toHaveLength(18)
    expect(pageCount(18)).toBe(2)
    expect(new Set(positions.map((position) => position.page))).toEqual(new Set([0, 1]))
    expect(positions.filter((position) => position.page === 0)).toHaveLength(9)
    expect(positions.filter((position) => position.page === 1)).toHaveLength(9)
    for (const position of positions) {
      expect(position.width).toBeCloseTo(mmToPoints(63), 6)
      expect(position.height).toBeCloseTo(mmToPoints(88), 6)
    }
    // The tenth card is the first card of the second page, at the same place as the first card.
    expect(positions[9]).toEqual({ ...positions[0], page: 1 })
  })

  it('fills each row from left to right, then the next row', () => {
    const [first, second, third, fourth] = layoutCards(4, 'a4')
    const origin = gridOrigin('a4')

    expect(first).toMatchObject({ x: origin.x, y: origin.y })
    expect(second.x).toBeCloseTo(origin.x + cardWidth, 6)
    expect(third.x).toBeCloseTo(origin.x + 2 * cardWidth, 6)
    expect(fourth).toMatchObject({ x: origin.x })
    expect(fourth.y).toBeCloseTo(origin.y + cardHeight, 6)
  })

  it('puts the grid in the center of an A4 page', () => {
    expect(pageDimensions.a4.width).toBeCloseTo(595.28, 2)
    expect(pageDimensions.a4.height).toBeCloseTo(841.89, 2)
    const origin = gridOrigin('a4')
    // A4 is 210 × 297 mm and the grid is 189 × 264 mm, so the margins are 10.5 mm and 16.5 mm.
    expect(origin.x).toBeCloseTo(mmToPoints(10.5), 6)
    expect(origin.y).toBeCloseTo(mmToPoints(16.5), 6)
  })

  it('puts the grid in the center of a Letter page', () => {
    expect(pageDimensions.letter).toEqual({ width: 612, height: 792 })
    const origin = gridOrigin('letter')
    expect(origin.x).toBeCloseTo((612 - 3 * cardWidth) / 2, 6)
    expect(origin.y).toBeCloseTo((792 - 3 * cardHeight) / 2, 6)
    expect(origin.x).toBeGreaterThan(0)
    expect(origin.y).toBeGreaterThan(0)
  })

  it.each(paperSizes)('puts the cut marks of %s in the margins, at each edge of the cards', (paper) => {
    const page = pageDimensions[paper]
    const origin = gridOrigin(paper)
    const marks = cutMarks(paper)

    expect(marks).toHaveLength(16)
    for (const mark of marks) {
      for (const x of [mark.x1, mark.x2]) {
        expect(x).toBeGreaterThanOrEqual(0)
        expect(x).toBeLessThanOrEqual(page.width)
      }
      for (const y of [mark.y1, mark.y2]) {
        expect(y).toBeGreaterThanOrEqual(0)
        expect(y).toBeLessThanOrEqual(page.height)
      }
      const vertical = mark.x1 === mark.x2
      const outsideGrid = vertical ? mark.y2 <= origin.y || mark.y1 >= origin.y + 3 * cardHeight : mark.x2 <= origin.x || mark.x1 >= origin.x + 3 * cardWidth
      expect(outsideGrid).toBe(true)
    }
  })

  it('uses Letter for the United States and A4 for other locales', () => {
    expect(defaultPaperSize('en-US')).toBe('letter')
    expect(defaultPaperSize('en-GB')).toBe('a4')
    expect(defaultPaperSize('fr-FR')).toBe('a4')
    expect(defaultPaperSize('fr')).toBe('a4')
    expect(defaultPaperSize(undefined)).toBe('a4')
  })
})
