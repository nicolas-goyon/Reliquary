/**
 * A small reader of the PDF files that PdfWriter makes, for the tests only.
 * It also makes JPEG headers for the tests.
 * It reads the cross-reference table and the objects, and does not trust the writer.
 */

export interface ParsedPdf {
  text: string
  /** The byte offset of each object, from the cross-reference table. Index 0 is the free entry. */
  offsets: number[]
  /** The text of each object, from "N 0 obj" to "endobj", by object number. */
  objects: Map<number, string>
  trailer: string
}

export function parsePdf(bytes: Uint8Array): ParsedPdf {
  const text = Array.from(bytes, (byte) => String.fromCharCode(byte)).join('')
  const startxref = /startxref\n(\d+)\n%%EOF\n$/.exec(text)
  if (!startxref) {
    throw new Error('The file has no startxref at the end.')
  }
  const xrefOffset = Number(startxref[1])
  const xref = /^xref\n0 (\d+)\n/.exec(text.slice(xrefOffset))
  if (!xref) {
    throw new Error('The startxref value does not point to the xref keyword.')
  }
  const count = Number(xref[1])
  const entriesStart = xrefOffset + xref[0].length
  const offsets: number[] = []
  for (let index = 0; index < count; index += 1) {
    const entry = text.slice(entriesStart + index * 20, entriesStart + (index + 1) * 20)
    if (!/^\d{10} \d{5} [nf]\r\n$/.test(entry)) {
      throw new Error(`The xref entry ${index} is not 20 bytes: ${JSON.stringify(entry)}`)
    }
    offsets.push(Number(entry.slice(0, 10)))
  }
  const objects = new Map<number, string>()
  offsets.slice(1).forEach((offset, index) => {
    const end = text.indexOf('\nendobj\n', offset)
    objects.set(index + 1, text.slice(offset, end + 8))
  })
  const trailer = text.slice(text.indexOf('trailer', entriesStart), text.lastIndexOf('startxref'))
  return { text, offsets, objects, trailer }
}

/** Gets the content stream of an object. */
export function streamOf(object: string): string {
  return object.slice(object.indexOf('stream\n') + 7, object.lastIndexOf('\nendstream'))
}

/** Makes the header of a JPEG image: SOI, optional segments, a SOF marker and EOI. The image data is not real. */
export function fakeJpeg(width: number, height: number, components = 3, { sof = 0xc0, precision = 8, adobe = false } = {}): Uint8Array<ArrayBuffer> {
  const app0 = [0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]
  const app14 = adobe ? [0xff, 0xee, 0x00, 0x0e, 0x41, 0x64, 0x6f, 0x62, 0x65, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x02] : []
  const sofLength = 8 + 3 * components
  const sofSegment = [0xff, sof, sofLength >> 8, sofLength & 0xff, precision, height >> 8, height & 0xff, width >> 8, width & 0xff, components]
  for (let index = 0; index < components; index += 1) {
    sofSegment.push(index + 1, 0x11, 0)
  }
  return new Uint8Array([0xff, 0xd8, ...app0, ...app14, ...sofSegment, 0xff, 0xd9])
}
