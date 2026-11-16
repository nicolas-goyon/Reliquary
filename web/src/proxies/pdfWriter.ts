import { helveticaWidths } from './helveticaWidths'
import { readJpegInfo } from './jpeg'

/**
 * A small PDF 1.7 writer (ISO 32000-1) for the proxy PDF. It replaces a general PDF library.
 * It writes lines, rectangles, rounded rectangles, Helvetica text and JPEG images, and nothing more.
 *
 * All the coordinates are in points, from the top left corner of the page, with y down.
 * The writer changes them to the PDF coordinates, which have y up from the bottom left corner.
 */

export type FontName = keyof typeof helveticaWidths
export type PaintMode = 'stroke' | 'fill' | 'fillAndStroke'
/** A color as red, green and blue values from 0 to 1. */
export type Color = readonly [number, number, number]

interface Page {
  width: number
  height: number
  content: string[]
  fonts: Set<FontName>
  images: Set<number>
}

/** The cubic Bézier factor for a quarter circle: 4/3 × (√2 − 1). */
const kappa = 0.5522847498
/** The cap height of Helvetica, in 1/1000 of the font size, from the AFM file. */
const capHeight = 718

/** The Unicode characters of the WinAnsi codes 128 to 159. */
const winAnsiHigh = '€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ'
const winAnsiCodes = new Map([...winAnsiHigh].map((character, index) => [character, 128 + index]).filter(([character]) => !/[\u0081\u008d\u008f\u0090\u009d]/.test(character as string)) as [string, number][])

/** Gets the WinAnsi code of a character, or undefined when WinAnsi has no glyph for it. */
function winAnsiCode(character: string): number | undefined {
  const code = character.codePointAt(0) ?? 0
  if ((code >= 32 && code <= 126) || (code >= 160 && code <= 255)) {
    return code
  }
  return winAnsiCodes.get(character)
}

/**
 * Changes a text to WinAnsi codes, the encoding of the standard fonts.
 * A character with an accent that WinAnsi does not have loses the accent (ő gives o). Other characters give "?".
 */
export function toWinAnsi(text: string): number[] {
  const codes: number[] = []
  for (const character of text.replace(/[\t\n\r]/g, ' ')) {
    const code = winAnsiCode(character) ?? winAnsiCode(character.normalize('NFD')[0])
    codes.push(code ?? 63)
  }
  return codes
}

/** Writes WinAnsi codes as a PDF literal string. It escapes ( ) and \, and writes the codes over 126 as octal values. */
export function pdfString(codes: readonly number[]): string {
  let result = '('
  for (const code of codes) {
    if (code === 0x28 || code === 0x29 || code === 0x5c) {
      result += `\\${String.fromCharCode(code)}`
    } else if (code < 32 || code > 126) {
      result += `\\${code.toString(8).padStart(3, '0')}`
    } else {
      result += String.fromCharCode(code)
    }
  }
  return `${result})`
}

/** Writes a number with 3 decimals or fewer. PDF does not accept the exponent notation. */
function num(value: number): string {
  const text = (Math.round(value * 1000) / 1000).toFixed(3).replace(/\.?0+$/, '')
  return text === '-0' ? '0' : text
}

function latin1(text: string): Uint8Array<ArrayBuffer> {
  const bytes = new Uint8Array(text.length)
  for (let index = 0; index < text.length; index += 1) {
    bytes[index] = text.charCodeAt(index) & 0xff
  }
  return bytes
}

export class PdfWriter {
  private readonly pages: Page[] = []
  /** The bodies of the indirect objects. The object number is the index + 1. Objects 1 and 2 are the catalog and the page tree. */
  private readonly objects: (Uint8Array | string)[][] = [[], []]
  private readonly fontObjects = new Map<FontName, number>()
  private readonly imageObjects = new Map<unknown, number>()
  private lineWidth = 1
  private strokeColor: Color = [0, 0, 0]
  private fillColor: Color = [0, 0, 0]
  private font: FontName = 'Helvetica'
  private fontSize = 12

  /** Adds a page and makes it the current page. The size is in points. */
  addPage(width: number, height: number): void {
    this.pages.push({ width, height, content: [], fonts: new Set(), images: new Set() })
    // Each content stream starts with the default graphics state. The writer sets the current state again.
    this.write(`${num(this.lineWidth)} w`, `${this.strokeColor.map(num).join(' ')} RG`, `${this.fillColor.map(num).join(' ')} rg`)
  }

  get pageCount(): number {
    return this.pages.length
  }

  setLineWidth(width: number): void {
    this.lineWidth = width
    this.write(`${num(width)} w`)
  }

  setStrokeColor(color: Color): void {
    this.strokeColor = color
    this.write(`${color.map(num).join(' ')} RG`)
  }

  setFillColor(color: Color): void {
    this.fillColor = color
    this.write(`${color.map(num).join(' ')} rg`)
  }

  setFont(font: FontName, size: number): void {
    this.font = font
    this.fontSize = size
  }

  line(x1: number, y1: number, x2: number, y2: number): void {
    this.write(`${num(x1)} ${this.y(y1)} m ${num(x2)} ${this.y(y2)} l S`)
  }

  rect(x: number, y: number, width: number, height: number, mode: PaintMode = 'stroke'): void {
    this.write(`${num(x)} ${this.y(y + height)} ${num(width)} ${num(height)} re ${paint(mode)}`)
  }

  /** Draws a rectangle with round corners. Each corner is a quarter circle with the given radius. */
  roundedRect(x: number, y: number, width: number, height: number, radius: number, mode: PaintMode = 'stroke'): void {
    const r = Math.min(radius, width / 2, height / 2)
    const c = r * (1 - kappa)
    const right = x + width
    const bottom = y + height
    const p = (px: number, py: number) => `${num(px)} ${this.y(py)}`
    this.write(
      [
        `${p(x + r, y)} m`,
        `${p(right - r, y)} l`,
        `${p(right - c, y)} ${p(right, y + c)} ${p(right, y + r)} c`,
        `${p(right, bottom - r)} l`,
        `${p(right, bottom - c)} ${p(right - c, bottom)} ${p(right - r, bottom)} c`,
        `${p(x + r, bottom)} l`,
        `${p(x + c, bottom)} ${p(x, bottom - c)} ${p(x, bottom - r)} c`,
        `${p(x, y + r)} l`,
        `${p(x, y + c)} ${p(x + c, y)} ${p(x + r, y)} c`,
        `h ${paint(mode)}`,
      ].join(' '),
    )
  }

  /** Gets the width of a text in points, with the current font and size. */
  textWidth(text: string): number {
    const widths = helveticaWidths[this.font]
    const units = toWinAnsi(text).reduce((sum, code) => sum + (widths[code - 32] ?? 0), 0)
    return (units * this.fontSize) / 1000
  }

  /** Divides a text into lines that are not wider than maxWidth. A word that is too wide gets a break between two characters. */
  splitText(text: string, maxWidth: number): string[] {
    const lines: string[] = []
    let current = ''
    for (const word of text.split(/\s+/).filter(Boolean)) {
      const candidate = current ? `${current} ${word}` : word
      if (this.textWidth(candidate) <= maxWidth) {
        current = candidate
        continue
      }
      if (current) {
        lines.push(current)
      }
      current = ''
      for (const character of word) {
        if (current && this.textWidth(current + character) > maxWidth) {
          lines.push(current)
          current = ''
        }
        current += character
      }
    }
    if (current) {
      lines.push(current)
    }
    return lines
  }

  /**
   * Writes one line of text with the fill color. The x value is the left edge, the center or the right edge of the text.
   * The y value is the baseline, or the middle of the capital letters for the baseline "middle".
   */
  text(text: string, x: number, y: number, options: { align?: 'left' | 'center' | 'right'; baseline?: 'alphabetic' | 'middle' } = {}): void {
    const page = this.current()
    page.fonts.add(this.font)
    this.fontObject(this.font)
    const width = this.textWidth(text)
    const left = options.align === 'center' ? x - width / 2 : options.align === 'right' ? x - width : x
    const baseline = options.baseline === 'middle' ? y + (capHeight * this.fontSize) / 2000 : y
    this.write(`BT /${fontResource(this.font)} ${num(this.fontSize)} Tf ${num(left)} ${this.y(baseline)} Td ${pdfString(toWinAnsi(text))} Tj ET`)
  }

  /**
   * Draws a JPEG image in a rectangle. The PDF contains the JPEG data with no change (DCTDecode).
   * The PDF contains each image once: a second call with the same key, or with the same data, uses the first image object.
   */
  image(data: Uint8Array, x: number, y: number, width: number, height: number, key: unknown = data): void {
    const page = this.current()
    let object = this.imageObjects.get(key)
    if (object === undefined) {
      const info = readJpegInfo(data)
      const colorSpace = info.components === 1 ? 'DeviceGray' : info.components === 3 ? 'DeviceRGB' : 'DeviceCMYK'
      const decode = info.adobeInverted ? ' /Decode [1 0 1 0 1 0 1 0]' : ''
      const header = `<< /Type /XObject /Subtype /Image /Width ${info.width} /Height ${info.height} /ColorSpace /${colorSpace} /BitsPerComponent 8${decode} /Filter /DCTDecode /Length ${data.length} >>\nstream\n`
      object = this.addObject([header, data, '\nendstream'])
      this.imageObjects.set(key, object)
    }
    page.images.add(object)
    this.write(`q ${num(width)} 0 0 ${num(height)} ${num(x)} ${this.y(y + height)} cm /Im${object} Do Q`)
  }

  /** Writes the PDF file: the header, the objects, the cross-reference table and the trailer. */
  toBytes(): Uint8Array<ArrayBuffer> {
    if (this.pages.length === 0) {
      throw new Error('The PDF has no page. Add a page first.')
    }
    const objects = this.objects.map((parts) => [...parts])
    const add = (parts: (Uint8Array | string)[]) => objects.push(parts)
    const kids: number[] = []
    for (const page of this.pages) {
      const content = page.content.join('\n')
      const contentObject = add([`<< /Length ${content.length} >>\nstream\n${content}\nendstream`])
      const fonts = [...page.fonts].map((font) => `/${fontResource(font)} ${this.fontObjects.get(font)} 0 R`).join(' ')
      const images = [...page.images].map((object) => `/Im${object} ${object} 0 R`).join(' ')
      kids.push(
        add([
          `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${num(page.width)} ${num(page.height)}] /Resources << /ProcSet [/PDF /Text /ImageB /ImageC] /Font << ${fonts} >> /XObject << ${images} >> >> /Contents ${contentObject} 0 R >>`,
        ]),
      )
    }
    objects[0] = ['<< /Type /Catalog /Pages 2 0 R >>']
    objects[1] = [`<< /Type /Pages /Kids [${kids.map((kid) => `${kid} 0 R`).join(' ')}] /Count ${kids.length} >>`]
    const info = add(['<< /Producer (Reliquary) >>'])

    // The second line has 4 bytes over 127, so that file transfer tools keep the file as binary data (ISO 32000-1, 7.5.2).
    const chunks: Uint8Array[] = [latin1('%PDF-1.7\n%\xe2\xe3\xcf\xd3\n')]
    let length = chunks[0].length
    const offsets: number[] = []
    const push = (part: Uint8Array | string) => {
      const bytes = typeof part === 'string' ? latin1(part) : part
      chunks.push(bytes)
      length += bytes.length
    }
    objects.forEach((parts, index) => {
      offsets.push(length)
      push(`${index + 1} 0 obj\n`)
      parts.forEach(push)
      push('\nendobj\n')
    })
    const xref = length
    // Each entry of the cross-reference table has exactly 20 bytes, with a 2-byte end of line (ISO 32000-1, 7.5.4).
    push(`xref\n0 ${objects.length + 1}\n0000000000 65535 f\r\n`)
    push(offsets.map((offset) => `${String(offset).padStart(10, '0')} 00000 n\r\n`).join(''))
    push(`trailer\n<< /Size ${objects.length + 1} /Root 1 0 R /Info ${info} 0 R >>\nstartxref\n${xref}\n%%EOF\n`)

    const result = new Uint8Array(length)
    let position = 0
    for (const chunk of chunks) {
      result.set(chunk, position)
      position += chunk.length
    }
    return result
  }

  toBlob(): Blob {
    return new Blob([this.toBytes()], { type: 'application/pdf' })
  }

  private current(): Page {
    const page = this.pages.at(-1)
    if (!page) {
      throw new Error('The PDF has no page. Add a page first.')
    }
    return page
  }

  private write(...operators: string[]): void {
    this.current().content.push(...operators)
  }

  /** Changes a y value from the top of the page to the PDF coordinate from the bottom of the page. */
  private y(value: number): string {
    return num(this.current().height - value)
  }

  private addObject(parts: (Uint8Array | string)[]): number {
    return this.objects.push(parts)
  }

  private fontObject(font: FontName): number {
    let object = this.fontObjects.get(font)
    if (object === undefined) {
      object = this.addObject([`<< /Type /Font /Subtype /Type1 /BaseFont /${font} /Encoding /WinAnsiEncoding >>`])
      this.fontObjects.set(font, object)
    }
    return object
  }
}

function paint(mode: PaintMode): string {
  return mode === 'fill' ? 'f' : mode === 'fillAndStroke' ? 'B' : 'S'
}

function fontResource(font: FontName): string {
  return font === 'Helvetica' ? 'F1' : 'F2'
}
