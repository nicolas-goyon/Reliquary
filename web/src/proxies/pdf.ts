import { readJpegInfo } from './jpeg'
import type { PdfWriter } from './pdfWriter'
import { cutMarks, layoutCards, pageCount, pageDimensions, type PaperSize } from './layout'
import { expandItems, type ProxyItem } from './selection'

export interface ProxyPdfOptions {
  paper: PaperSize
  /** Receives the number of images that the function loaded, and the number of images to load. */
  onProgress?(loaded: number, total: number): void
}

export interface ProxyPdf {
  blob: Blob
  pages: number
  /** The names of the cards whose image did not load. The PDF shows a frame with the name in their place. */
  failed: string[]
}

/** The number of image requests at the same time. */
const parallelRequests = 6

/**
 * Makes the PDF of a proxy list: 9 cards on each page, each card at 63 × 88 mm, with cut marks.
 * The function loads the PDF writer only when it runs, so that the main bundle stays small.
 * A card with no image, or with an image that does not load, gets a frame with its name.
 */
export async function makeProxyPdf(items: readonly ProxyItem[], { paper, onProgress }: ProxyPdfOptions): Promise<ProxyPdf> {
  const cards = expandItems(items)
  const images = await loadImages(cards.map((card) => card.imageUri), onProgress)
  const { PdfWriter } = await import('./pdfWriter')
  const page = pageDimensions[paper]
  const pdf = new PdfWriter()
  const positions = layoutCards(cards.length, paper)
  const failed = new Set<string>()
  const pages = Math.max(1, pageCount(cards.length))

  for (let index = 0; index < pages; index += 1) {
    pdf.addPage(page.width, page.height)
    pdf.setLineWidth(0.25)
    pdf.setStrokeColor([0, 0, 0])
    for (const mark of cutMarks(paper)) {
      pdf.line(mark.x1, mark.y1, mark.x2, mark.y2)
    }
    positions.forEach((position, cardIndex) => {
      if (position.page !== index) {
        return
      }
      const card = cards[cardIndex]
      const image = card.imageUri ? images.get(card.imageUri) : undefined
      if (image) {
        // The address is the key, so the PDF contains each image once, also for 4 copies of a card.
        pdf.image(image, position.x, position.y, position.width, position.height, card.imageUri)
        return
      }
      if (card.imageUri) {
        failed.add(card.name)
      }
      drawPlaceholder(pdf, card.name, position)
    })
  }

  return { blob: pdf.toBlob(), pages, failed: [...failed] }
}

/** Draws a frame with the card name, for a card with no image. */
function drawPlaceholder(pdf: PdfWriter, name: string, position: { x: number; y: number; width: number; height: number }): void {
  const inset = 6
  const fontSize = 12
  const lineHeight = fontSize * 1.15
  pdf.setLineWidth(1)
  pdf.roundedRect(position.x + inset, position.y + inset, position.width - 2 * inset, position.height - 2 * inset, 8)
  pdf.setFont('Helvetica-Bold', fontSize)
  const lines = pdf.splitText(name, position.width - 4 * inset)
  const centerX = position.x + position.width / 2
  // The lines are in the vertical center of the card, as a block.
  const firstY = position.y + position.height / 2 - ((lines.length - 1) * lineHeight) / 2
  lines.forEach((line, index) => pdf.text(line, centerX, firstY + index * lineHeight, { align: 'center', baseline: 'middle' }))
  pdf.setLineWidth(0.25)
}

/** Loads each image address once, with some requests at the same time. An image that does not load is missing from the result. */
async function loadImages(uris: (string | undefined)[], onProgress?: (loaded: number, total: number) => void): Promise<Map<string, Uint8Array>> {
  const unique = [...new Set(uris.filter((uri): uri is string => Boolean(uri)))]
  const images = new Map<string, Uint8Array>()
  let next = 0
  let loaded = 0
  onProgress?.(0, unique.length)

  async function worker() {
    while (next < unique.length) {
      const uri = unique[next]
      next += 1
      try {
        images.set(uri, await loadJpeg(uri))
      } catch {
        // The PDF shows a frame with the card name for this image. The caller gets the card names.
      }
      loaded += 1
      onProgress?.(loaded, unique.length)
    }
  }

  await Promise.all(Array.from({ length: Math.min(parallelRequests, unique.length) }, worker))
  return images
}

/**
 * Gets an image as JPEG data that the PDF can contain with no change. The Scryfall images are JPEG files.
 * The function converts an image in a different format, or a JPEG image that PDF does not accept.
 */
export async function loadJpeg(uri: string): Promise<Uint8Array> {
  // The Scryfall image servers allow requests from other origins (CORS). The request sends no cookie.
  const response = await fetch(uri, { mode: 'cors', credentials: 'omit' })
  if (!response.ok) {
    throw new Error(`The image server returned the status ${response.status} for ${uri}.`)
  }
  const blob = await response.blob()
  const bytes = new Uint8Array(await blob.arrayBuffer())
  if (bytes[0] === 0xff && bytes[1] === 0xd8) {
    try {
      readJpegInfo(bytes)
      return bytes
    } catch {
      // For example a 12-bit image or an image with arithmetic coding. The browser can decode it, and the canvas makes a baseline JPEG.
    }
  }
  return convertToJpeg(blob)
}

async function convertToJpeg(blob: Blob): Promise<Uint8Array> {
  const bitmap = await createImageBitmap(blob)
  const canvas = document.createElement('canvas')
  canvas.width = bitmap.width
  canvas.height = bitmap.height
  const context = canvas.getContext('2d')
  if (!context) {
    throw new Error('The browser cannot draw the image.')
  }
  // JPEG has no transparency. A white background replaces the transparent parts.
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, canvas.width, canvas.height)
  context.drawImage(bitmap, 0, 0)
  bitmap.close()
  const jpeg = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.92))
  if (!jpeg) {
    throw new Error('The browser cannot convert the image to JPEG.')
  }
  return new Uint8Array(await jpeg.arrayBuffer())
}

/** Gives a PDF file to the user as a download. */
export function downloadFile(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  // Some browsers start the download after the click event. The delay keeps the address valid until then.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/** Gets a file name for the proxies of a list, with no character that a file system refuses. */
export function proxyFileName(listName: string): string {
  const safe = [...listName]
    .map((character) => (character.charCodeAt(0) < 32 || '\\/:*?"<>|'.includes(character) ? ' ' : character))
    .join('')
    .replace(/\s+/g, ' ')
    .trim()
  return safe ? `${safe} - proxies.pdf` : 'proxies.pdf'
}
