import type { Deck } from '../api/decks'
import {
  commanderImages,
  gridCard,
  gridColumns,
  gridLines,
  headerHeight,
  imageGroups,
  imageHeight,
  imageWidth,
  listMetrics,
  margin,
  placeGroups,
  type DeckImageStyle,
  type ImageGroup,
} from './deckImageLayout'

/** The texts of the image. The dialog gives them in the language of the interface. */
export interface DeckImageTexts {
  /** The second line of the band at the top, for example "Commander · 100 cards · by Ana". */
  subtitle: string
  /** The heading of a group, with its card count. */
  groupTitle(group: ImageGroup): string
  /** The text at the bottom right, for example "reliquary.example.org". */
  footer: string
}

export interface DeckImage {
  blob: Blob
  width: number
  height: number
  /** The names of the cards whose image did not load. The grid shows a frame with the name in their place. */
  failed: string[]
}

/** The colors of the image. The image has one dark theme, so it is readable in the dark theme of Discord and on a white forum. */
const colors = {
  background: '#161a23',
  panel: '#1f2430',
  text: '#e8ecf3',
  muted: '#a3adbf',
  accent: '#ffc9a6',
  frame: '#3a4252',
} as const

const font = '"Manrope Variable", "Segoe UI", system-ui, sans-serif'

/** The number of image requests at the same time. */
const parallelRequests = 6

/** Gets the art crop of Scryfall for a card image address. Other addresses do not change. */
export function artCrop(uri: string): string {
  return uri.replace(/^(https:\/\/cards\.scryfall\.io\/)(?:small|normal|large|png)\//, '$1art_crop/')
}

/**
 * Draws the image of a deck in the browser and gives a PNG file.
 * The browser gets the card images from Scryfall with CORS, so the canvas stays readable.
 */
export async function makeDeckImage(deck: Deck, style: DeckImageStyle, texts: DeckImageTexts): Promise<DeckImage> {
  const width = imageWidth
  const height = imageHeight(deck, style)
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const context = canvas.getContext('2d')
  if (!context) {
    throw new Error('The browser cannot draw the image.')
  }

  const commanders = commanderImages(deck)
  const grid = style === 'grid' ? gridLines(deck) : []
  const images = await loadImages([...commanders.map(artCrop), ...grid.map((line) => line.imageUri)])
  // A commander with no art crop uses its full image.
  const missingArt = commanders.filter((uri) => !images.has(artCrop(uri)))
  for (const [uri, image] of await loadImages(missingArt)) {
    images.set(artCrop(uri), image)
  }

  context.fillStyle = colors.background
  context.fillRect(0, 0, width, height)
  drawHeader(context, deck.name, texts.subtitle, commanders.map((uri) => images.get(artCrop(uri))).filter((image): image is ImageBitmap => Boolean(image)))

  const failed = new Set<string>()
  if (style === 'grid') {
    grid.forEach((line, index) => {
      const x = margin + (index % gridColumns) * (gridCard.width + gridCard.gap)
      const y = headerHeight + margin + Math.floor(index / gridColumns) * (gridCard.height + gridCard.gap)
      const image = line.imageUri ? images.get(line.imageUri) : undefined
      if (!image && line.imageUri) {
        failed.add(line.name)
      }
      drawGridCard(context, image, line.name, line.quantity, x, y)
    })
  } else {
    drawList(context, imageGroups(deck), texts)
  }

  context.font = `500 18px ${font}`
  context.fillStyle = colors.muted
  context.textAlign = 'right'
  context.textBaseline = 'alphabetic'
  context.fillText(texts.footer, width - margin, height - 18)
  context.textAlign = 'left'

  for (const image of images.values()) {
    image.close()
  }

  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/png'))
  if (!blob) {
    throw new Error('The browser cannot make the PNG file.')
  }
  return { blob, width, height, failed: [...failed] }
}

function drawHeader(context: CanvasRenderingContext2D, name: string, subtitle: string, arts: ImageBitmap[]): void {
  const width = context.canvas.width
  context.fillStyle = colors.panel
  context.fillRect(0, 0, width, headerHeight)
  // The art fills the band: one commander takes the full width, two commanders share it.
  arts.slice(0, 2).forEach((art, index, all) => {
    const slot = width / all.length
    drawCover(context, art, index * slot, 0, slot, headerHeight)
  })
  if (arts.length > 0) {
    const gradient = context.createLinearGradient(0, 0, 0, headerHeight)
    gradient.addColorStop(0, 'rgb(22 26 35 / 35%)')
    gradient.addColorStop(1, 'rgb(22 26 35 / 92%)')
    context.fillStyle = gradient
    context.fillRect(0, 0, width, headerHeight)
  }
  context.fillStyle = colors.text
  context.textBaseline = 'alphabetic'
  context.font = `800 64px ${font}`
  context.fillText(fit(context, name, width - 2 * margin), margin, headerHeight - 82)
  context.fillStyle = colors.accent
  context.font = `600 28px ${font}`
  context.fillText(fit(context, subtitle, width - 2 * margin), margin, headerHeight - 36)
}

/** Draws an image that covers the rectangle, cut at the center. */
function drawCover(context: CanvasRenderingContext2D, image: ImageBitmap, x: number, y: number, width: number, height: number): void {
  const scale = Math.max(width / image.width, height / image.height)
  const sourceWidth = width / scale
  const sourceHeight = height / scale
  context.drawImage(image, (image.width - sourceWidth) / 2, (image.height - sourceHeight) / 2, sourceWidth, sourceHeight, x, y, width, height)
}

function drawList(context: CanvasRenderingContext2D, groups: ImageGroup[], texts: DeckImageTexts): void {
  const columnWidth = (context.canvas.width - 2 * margin - (listMetrics.columns - 1) * listMetrics.columnGap) / listMetrics.columns
  const top = headerHeight + margin
  context.textBaseline = 'top'
  for (const placement of placeGroups(groups).placements) {
    const x = margin + placement.column * (columnWidth + listMetrics.columnGap)
    let y = top + placement.y
    context.fillStyle = colors.accent
    context.font = `700 24px ${font}`
    context.fillText(fit(context, texts.groupTitle(placement.group), columnWidth), x, y)
    y += listMetrics.headingHeight
    for (const line of placement.group.lines) {
      context.fillStyle = colors.muted
      context.font = `600 21px ${font}`
      context.fillText(String(line.quantity), x, y)
      context.fillStyle = colors.text
      context.font = `500 21px ${font}`
      context.fillText(fit(context, line.name, columnWidth - 36), x + 36, y)
      y += listMetrics.lineHeight
    }
  }
}

function drawGridCard(context: CanvasRenderingContext2D, image: ImageBitmap | undefined, name: string, quantity: number, x: number, y: number): void {
  const { width, height } = gridCard
  context.save()
  roundedRect(context, x, y, width, height, 9)
  context.clip()
  if (image) {
    context.drawImage(image, x, y, width, height)
  } else {
    context.fillStyle = colors.panel
    context.fillRect(x, y, width, height)
    context.fillStyle = colors.text
    context.font = `600 18px ${font}`
    context.textBaseline = 'middle'
    context.textAlign = 'center'
    wrap(context, name, width - 24).forEach((text, index, all) => context.fillText(text, x + width / 2, y + height / 2 + (index - (all.length - 1) / 2) * 24))
    context.textAlign = 'left'
  }
  context.restore()
  if (!image) {
    context.strokeStyle = colors.frame
    context.lineWidth = 2
    roundedRect(context, x, y, width, height, 9)
    context.stroke()
  }
  if (quantity > 1) {
    const label = `×${quantity}`
    context.font = `800 22px ${font}`
    const badge = context.measureText(label).width + 20
    context.fillStyle = 'rgb(0 0 0 / 78%)'
    roundedRect(context, x + width - badge - 8, y + height - 44, badge, 34, 17)
    context.fill()
    context.fillStyle = '#ffffff'
    context.textBaseline = 'middle'
    context.fillText(label, x + width - badge + 2, y + height - 27)
  }
}

function roundedRect(context: CanvasRenderingContext2D, x: number, y: number, width: number, height: number, radius: number): void {
  context.beginPath()
  context.moveTo(x + radius, y)
  context.arcTo(x + width, y, x + width, y + height, radius)
  context.arcTo(x + width, y + height, x, y + height, radius)
  context.arcTo(x, y + height, x, y, radius)
  context.arcTo(x, y, x + width, y, radius)
  context.closePath()
}

/** Cuts a text with an ellipsis so that it is not wider than the width. */
export function fit(context: Pick<CanvasRenderingContext2D, 'measureText'>, text: string, width: number): string {
  if (context.measureText(text).width <= width) {
    return text
  }
  let end = text.length
  while (end > 0 && context.measureText(`${text.slice(0, end)}…`).width > width) {
    end -= 1
  }
  return `${text.slice(0, end).trimEnd()}…`
}

/** Puts a text on lines that are not wider than the width. */
function wrap(context: CanvasRenderingContext2D, text: string, width: number): string[] {
  const lines: string[] = []
  let current = ''
  for (const word of text.split(/\s+/)) {
    const next = current ? `${current} ${word}` : word
    if (current && context.measureText(next).width > width) {
      lines.push(current)
      current = word
    } else {
      current = next
    }
  }
  if (current) {
    lines.push(current)
  }
  return lines.slice(0, 5)
}

/** Loads each image address once, with some requests at the same time. An image that does not load is missing from the result. */
async function loadImages(uris: (string | undefined)[]): Promise<Map<string, ImageBitmap>> {
  const unique = [...new Set(uris.filter((uri): uri is string => Boolean(uri)))]
  const images = new Map<string, ImageBitmap>()
  let next = 0
  async function worker() {
    while (next < unique.length) {
      const uri = unique[next]
      next += 1
      try {
        // The Scryfall image servers allow requests from other origins (CORS). The request sends no cookie.
        const response = await fetch(uri, { mode: 'cors', credentials: 'omit' })
        if (response.ok) {
          images.set(uri, await createImageBitmap(await response.blob()))
        }
      } catch {
        // The image shows a frame with the card name in its place.
      }
    }
  }
  await Promise.all(Array.from({ length: Math.min(parallelRequests, unique.length) }, worker))
  return images
}
