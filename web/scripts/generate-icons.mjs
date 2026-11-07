// Generates the icons of the web app manifest from one drawing.
// The drawing is a list of simple shapes. The script writes them as an SVG file,
// and it also draws them into PNG files with no external library.
//
// Usage, in the web folder: npm run icons
import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { crc32, deflateSync } from 'node:zlib'

const outputDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'public', 'icons')

// The drawing uses a 512 × 512 canvas.
const canvas = 512
const colors = {
  background: '#1d1b19',
  gem: '#f1d27a',
  lid: '#e3b341',
  body: '#b8862f',
  plaque: '#1d1b19',
}

// The mark: a small casket with a gem on its lid. The mark fits in a circle with a radius
// of 194 around the center. The safe zone of a maskable icon is a circle with a radius of 205.
const mark = [
  { type: 'polygon', color: colors.gem, points: [[256, 80], [300, 124], [256, 168], [212, 124]] },
  { type: 'rect', color: colors.lid, x: 112, y: 180, width: 288, height: 60, radius: 22 },
  { type: 'rect', color: colors.body, x: 136, y: 256, width: 240, height: 152, radius: 18 },
  { type: 'rect', color: colors.plaque, x: 230, y: 292, width: 52, height: 72, radius: 8 },
]

/**
 * The variants of the icon.
 * - "any" icons have rounded corners and a transparent area outside the corners.
 * - Maskable icons and the Apple touch icon fill the full square. The platform cuts the shape.
 */
const variants = [
  { file: 'icon-192.png', size: 192, cornerRadius: 96, scale: 1 },
  { file: 'icon-512.png', size: 512, cornerRadius: 96, scale: 1 },
  { file: 'icon-maskable-512.png', size: 512, cornerRadius: 0, scale: 0.8 },
  { file: 'apple-touch-icon.png', size: 180, cornerRadius: 0, scale: 0.85 },
]

function shapesOf(variant) {
  const background = { type: 'rect', color: colors.background, x: 0, y: 0, width: canvas, height: canvas, radius: variant.cornerRadius }
  return [background, ...mark.map((shape) => scaleShape(shape, variant.scale))]
}

function scaleShape(shape, scale) {
  const center = canvas / 2
  const move = (value) => center + (value - center) * scale
  if (shape.type === 'polygon') {
    return { ...shape, points: shape.points.map(([x, y]) => [move(x), move(y)]) }
  }
  return { ...shape, x: move(shape.x), y: move(shape.y), width: shape.width * scale, height: shape.height * scale, radius: shape.radius * scale }
}

// SVG output

function svgOf(shapes) {
  const elements = shapes.map((shape) =>
    shape.type === 'polygon'
      ? `  <polygon points="${shape.points.map((point) => point.join(',')).join(' ')}" fill="${shape.color}"/>`
      : `  <rect x="${shape.x}" y="${shape.y}" width="${shape.width}" height="${shape.height}" rx="${shape.radius}" fill="${shape.color}"/>`,
  )
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${canvas} ${canvas}">\n${elements.join('\n')}\n</svg>\n`
}

// PNG output

function inside(shape, x, y) {
  if (shape.type === 'polygon') {
    // A point is inside a convex polygon when it is on the same side of each edge.
    let sign = 0
    for (let index = 0; index < shape.points.length; index += 1) {
      const [x1, y1] = shape.points[index]
      const [x2, y2] = shape.points[(index + 1) % shape.points.length]
      const cross = (x2 - x1) * (y - y1) - (y2 - y1) * (x - x1)
      if (cross !== 0) {
        if (sign !== 0 && Math.sign(cross) !== sign) {
          return false
        }
        sign = Math.sign(cross)
      }
    }
    return true
  }
  if (x < shape.x || x > shape.x + shape.width || y < shape.y || y > shape.y + shape.height) {
    return false
  }
  const radius = shape.radius
  const nearestX = Math.min(Math.max(x, shape.x + radius), shape.x + shape.width - radius)
  const nearestY = Math.min(Math.max(y, shape.y + radius), shape.y + shape.height - radius)
  return (x - nearestX) ** 2 + (y - nearestY) ** 2 <= radius ** 2
}

function rgbOf(color) {
  return [1, 3, 5].map((start) => Number.parseInt(color.slice(start, start + 2), 16))
}

/** Draws the shapes with 4 × 4 samples in each pixel, so the edges are smooth. */
function draw(shapes, size) {
  const samples = 4
  const pixels = Buffer.alloc(size * size * 4)
  const colorsOfShapes = shapes.map((shape) => rgbOf(shape.color))
  for (let row = 0; row < size; row += 1) {
    for (let column = 0; column < size; column += 1) {
      let red = 0
      let green = 0
      let blue = 0
      let covered = 0
      for (let sampleY = 0; sampleY < samples; sampleY += 1) {
        for (let sampleX = 0; sampleX < samples; sampleX += 1) {
          const x = ((column + (sampleX + 0.5) / samples) * canvas) / size
          const y = ((row + (sampleY + 0.5) / samples) * canvas) / size
          for (let index = shapes.length - 1; index >= 0; index -= 1) {
            if (inside(shapes[index], x, y)) {
              const [r, g, b] = colorsOfShapes[index]
              red += r
              green += g
              blue += b
              covered += 1
              break
            }
          }
        }
      }
      const offset = (row * size + column) * 4
      pixels[offset] = covered ? Math.round(red / covered) : 0
      pixels[offset + 1] = covered ? Math.round(green / covered) : 0
      pixels[offset + 2] = covered ? Math.round(blue / covered) : 0
      pixels[offset + 3] = Math.round((covered / (samples * samples)) * 255)
    }
  }
  return pixels
}

function chunk(type, data) {
  const length = Buffer.alloc(4)
  length.writeUInt32BE(data.length)
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data])
  const checksum = Buffer.alloc(4)
  checksum.writeUInt32BE(crc32(body))
  return Buffer.concat([length, body, checksum])
}

function pngOf(pixels, size) {
  const header = Buffer.alloc(13)
  header.writeUInt32BE(size, 0)
  header.writeUInt32BE(size, 4)
  header[8] = 8 // bit depth
  header[9] = 6 // color type: RGBA
  const rows = Buffer.alloc(size * (size * 4 + 1))
  for (let row = 0; row < size; row += 1) {
    // Each row starts with the filter type 0 (no filter).
    pixels.copy(rows, row * (size * 4 + 1) + 1, row * size * 4, (row + 1) * size * 4)
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(rows, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ])
}

mkdirSync(outputDir, { recursive: true })
writeFileSync(join(outputDir, 'icon.svg'), svgOf(shapesOf(variants[0])))
writeFileSync(join(outputDir, 'icon-maskable.svg'), svgOf(shapesOf(variants[2])))
for (const variant of variants) {
  writeFileSync(join(outputDir, variant.file), pngOf(draw(shapesOf(variant), variant.size), variant.size))
}
console.log(`Wrote ${variants.length} PNG icons and 2 SVG icons to ${outputDir}.`)
