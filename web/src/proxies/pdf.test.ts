import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mmToPoints, pageDimensions } from './layout'
import { makeProxyPdf, proxyFileName } from './pdf'
import type { ProxyItem } from './selection'

interface Call {
  method: string
  args: unknown[]
}

const documents: { options: unknown; calls: Call[] }[] = []

// The test replaces the PDF library with a recorder of the calls.
vi.mock('jspdf', () => {
  class jsPDF {
    calls: Call[] = []
    constructor(options: unknown) {
      documents.push({ options, calls: this.calls })
    }
    private record(method: string, args: unknown[]) {
      this.calls.push({ method, args })
      return this
    }
    addPage(...args: unknown[]) { return this.record('addPage', args) }
    addImage(...args: unknown[]) { return this.record('addImage', args) }
    line(...args: unknown[]) { return this.record('line', args) }
    roundedRect(...args: unknown[]) { return this.record('roundedRect', args) }
    text(...args: unknown[]) { return this.record('text', args) }
    setLineWidth(...args: unknown[]) { return this.record('setLineWidth', args) }
    setDrawColor(...args: unknown[]) { return this.record('setDrawColor', args) }
    setFont(...args: unknown[]) { return this.record('setFont', args) }
    setFontSize(...args: unknown[]) { return this.record('setFontSize', args) }
    splitTextToSize(text: string) { return [text] }
    output() { return new Blob(['%PDF-1.3'], { type: 'application/pdf' }) }
  }
  return { jsPDF, default: jsPDF }
})

const jpeg = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0, 0])

function stubImages(failing: string[] = []) {
  const fetchMock = vi.fn<(input: string) => Promise<Response>>(async (input) => (failing.includes(input) ? new Response('', { status: 404 }) : new Response(jpeg, { headers: { 'Content-Type': 'image/jpeg' } })))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const calls = (method: string) => documents[0].calls.filter((call) => call.method === method)

describe('proxy PDF', () => {
  beforeEach(() => {
    documents.length = 0
  })

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
    expect(documents).toHaveLength(1)
    expect(documents[0].options).toMatchObject({ unit: 'pt', format: [pageDimensions.a4.width, pageDimensions.a4.height] })
    expect(calls('addPage')).toHaveLength(1)
    const images = calls('addImage')
    expect(images).toHaveLength(18)
    for (const { args } of images) {
      expect(args[1]).toBe('JPEG')
      expect(args[4]).toBeCloseTo(mmToPoints(63), 6)
      expect(args[5]).toBeCloseTo(mmToPoints(88), 6)
    }
    // Each image address loads once, also for 3 copies of a card.
    expect(fetchMock).toHaveBeenCalledTimes(6)
    expect(progress[0]).toEqual([0, 6])
    expect(progress.at(-1)).toEqual([6, 6])
    // 16 cut marks on each page.
    expect(calls('line')).toHaveLength(32)
  })

  it('uses the Letter size', async () => {
    stubImages()
    await makeProxyPdf([{ imageUri: 'https://cards.scryfall.io/large/front/a.jpg', quantity: 1, name: 'A' }], { paper: 'letter' })
    expect(documents[0].options).toMatchObject({ format: [612, 792] })
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
    expect(calls('addImage')).toHaveLength(1)
    expect(calls('roundedRect')).toHaveLength(3)
    expect(calls('text').map((call) => call.args[0])).toEqual([['Broken card'], ['Broken card'], ['Treasure']])
  })

  it('gives a file name with no reserved character', () => {
    expect(proxyFileName('Atraxa: "superfriends" 2/3')).toBe('Atraxa superfriends 2 3 - proxies.pdf')
    expect(proxyFileName('  ')).toBe('proxies.pdf')
  })
})
