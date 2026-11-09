import { afterEach, describe, expect, it, vi } from 'vitest'
import { cardHeight, cardWidth, gridOrigin, mmToPoints, pageDimensions } from './layout'
import { makeProxyPdf, proxyFileName } from './pdf'
import { fakeJpeg, parsePdf, streamOf } from './pdfTestParser'
import type { ProxyItem } from './selection'

const jpeg = fakeJpeg(672, 936)

function stubImages(failing: string[] = []) {
  const fetchMock = vi.fn<(input: string) => Promise<Response>>(async (input) => (failing.includes(input) ? new Response('', { status: 404 }) : new Response(jpeg, { headers: { 'Content-Type': 'image/jpeg' } })))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

interface ParsedProxyPdf {
  pages: { mediaBox: number[]; content: string }[]
  images: string[]
}

/** Reads the produced bytes: the pages in the order of the page tree, their content streams and the image objects. */
async function readPdf(blob: Blob): Promise<ParsedProxyPdf> {
  const parsed = parsePdf(new Uint8Array(await blob.arrayBuffer()))
  const kids = /\/Kids \[([^\]]*)\]/.exec(parsed.objects.get(2)!)![1].match(/\d+(?= 0 R)/g)!.map(Number)
  const pages = kids.map((kid) => {
    const page = parsed.objects.get(kid)!
    const mediaBox = /\/MediaBox \[([^\]]*)\]/.exec(page)![1].split(' ').map(Number)
    const contents = Number(/\/Contents (\d+) 0 R/.exec(page)![1])
    return { mediaBox, content: streamOf(parsed.objects.get(contents)!) }
  })
  const images = [...parsed.objects.values()].filter((object) => object.includes('/Subtype /Image'))
  return { pages, images }
}

/** Gets the image placements of a page as [width, height, x, y] in PDF coordinates. */
function placements(content: string): number[][] {
  return [...content.matchAll(/q (\S+) 0 0 (\S+) (\S+) (\S+) cm \/Im\d+ Do Q/g)].map((match) => match.slice(1).map(Number))
}

const count = (content: string, pattern: RegExp) => content.match(pattern)?.length ?? 0

describe('proxy PDF', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('makes 2 pages for 18 cards, and each card is 63 by 88 mm', async () => {
    const fetchMock = stubImages()
    const items: ProxyItem[] = Array.from({ length: 6 }, (_, index) => ({ imageUri: `https://cards.scryfall.io/large/front/${index}.jpg`, quantity: 3, name: `Card ${index}` }))
    const progress: [number, number][] = []

    const pdf = await makeProxyPdf(items, { paper: 'a4', onProgress: (loaded, total) => progress.push([loaded, total]) })

    expect(pdf.pages).toBe(2)
    expect(pdf.failed).toEqual([])
    expect(pdf.blob.type).toBe('application/pdf')
    const parsed = await readPdf(pdf.blob)
    expect(parsed.pages).toHaveLength(2)
    for (const page of parsed.pages) {
      expect(page.mediaBox[2]).toBeCloseTo(pageDimensions.a4.width, 2)
      expect(page.mediaBox[3]).toBeCloseTo(pageDimensions.a4.height, 2)
      expect(placements(page.content)).toHaveLength(9)
      // 16 cut marks on each page.
      expect(count(page.content, / l S/g)).toBe(16)
    }
    // The PDF contains each image once, also for 3 copies of a card.
    expect(parsed.images).toHaveLength(6)
    expect(parsed.images[0]).toContain('/Width 672 /Height 936')
    const origin = gridOrigin('a4')
    const first = placements(parsed.pages[0].content)
    for (const [width, height] of first) {
      expect(width).toBeCloseTo(mmToPoints(63), 2)
      expect(height).toBeCloseTo(mmToPoints(88), 2)
    }
    // The first card is at the top left of the grid. The PDF origin is at the bottom left of the page.
    expect(first[0][2]).toBeCloseTo(origin.x, 2)
    expect(first[0][3]).toBeCloseTo(pageDimensions.a4.height - origin.y - cardHeight, 2)
    // The fifth card is in the center of the grid.
    expect(first[4][2]).toBeCloseTo(origin.x + cardWidth, 2)
    expect(first[4][3]).toBeCloseTo(pageDimensions.a4.height - origin.y - 2 * cardHeight, 2)
    // Each image address loads once, also for 3 copies of a card.
    expect(fetchMock).toHaveBeenCalledTimes(6)
    expect(progress[0]).toEqual([0, 6])
    expect(progress.at(-1)).toEqual([6, 6])
  })

  it('uses the Letter size', async () => {
    stubImages()
    const pdf = await makeProxyPdf([{ imageUri: 'https://cards.scryfall.io/large/front/a.jpg', quantity: 1, name: 'A' }], { paper: 'letter' })
    expect((await readPdf(pdf.blob)).pages.map((page) => page.mediaBox)).toEqual([[0, 0, 612, 792]])
  })

  it('draws a frame with the card name for a missing image and for an image that does not load', async () => {
    stubImages(['https://cards.scryfall.io/large/front/broken.jpg'])
    const pdf = await makeProxyPdf(
      [
        { imageUri: 'https://cards.scryfall.io/large/front/ok.jpg', quantity: 1, name: 'Good card' },
        { imageUri: 'https://cards.scryfall.io/large/front/broken.jpg', quantity: 2, name: 'Broken card' },
        { imageUri: undefined, quantity: 1, name: 'Treasure' },
      ],
      { paper: 'a4' },
    )

    expect(pdf.pages).toBe(1)
    expect(pdf.failed).toEqual(['Broken card'])
    const { pages } = await readPdf(pdf.blob)
    expect(placements(pages[0].content)).toHaveLength(1)
    // A frame is a closed path with 4 curves.
    expect(count(pages[0].content, / h S/g)).toBe(3)
    expect([...pages[0].content.matchAll(/\((.*)\) Tj/g)].map((match) => match[1])).toEqual(['Broken card', 'Broken card', 'Treasure'])
  })

  it('gives a file name with no reserved character', () => {
    expect(proxyFileName('Atraxa: "superfriends" 2/3')).toBe('Atraxa superfriends 2 3 - proxies.pdf')
    expect(proxyFileName('  ')).toBe('proxies.pdf')
  })
})
