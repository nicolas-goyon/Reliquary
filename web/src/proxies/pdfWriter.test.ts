import { describe, expect, it } from 'vitest'
import { fakeJpeg, parsePdf, streamOf } from './pdfTestParser'
import { PdfWriter, pdfString, toWinAnsi } from './pdfWriter'

describe('PDF writer', () => {
  it('writes a cross-reference table with the correct byte offset of each object', () => {
    const pdf = new PdfWriter()
    pdf.addPage(200, 100)
    pdf.line(0, 0, 10, 10)
    pdf.text('Hello', 10, 50)
    pdf.image(fakeJpeg(4, 6), 0, 0, 40, 60)
    pdf.addPage(300, 400)
    const parsed = parsePdf(pdf.toBytes())

    expect(parsed.text.startsWith('%PDF-1.7\n')).toBe(true)
    // Catalog, page tree, font, image, 2 × (content, page), information.
    expect(parsed.offsets).toHaveLength(1 + 9)
    expect(parsed.trailer).toContain('/Size 10')
    expect(parsed.trailer).toContain('/Root 1 0 R')
    parsed.offsets.slice(1).forEach((offset, index) => {
      expect(parsed.text.slice(offset).startsWith(`${index + 1} 0 obj\n`)).toBe(true)
    })
    expect(parsed.objects.get(1)).toContain('/Type /Catalog /Pages 2 0 R')
    expect(parsed.objects.get(2)).toContain('/Count 2')
  })

  it('gives each stream a length that agrees with its data', () => {
    const pdf = new PdfWriter()
    pdf.addPage(100, 100)
    pdf.text('Æther Vial (ü) \\ €', 0, 50)
    const parsed = parsePdf(pdf.toBytes())
    const streams = [...parsed.objects.values()].filter((object) => object.includes('stream\n'))
    expect(streams.length).toBeGreaterThan(0)
    for (const object of streams) {
      expect(streamOf(object)).toHaveLength(Number(/\/Length (\d+)/.exec(object)?.[1]))
    }
  })

  it('writes the image dimensions from the JPEG data, and contains the same image once', () => {
    const pdf = new PdfWriter()
    pdf.addPage(612, 792)
    const jpeg = fakeJpeg(672, 936)
    pdf.image(jpeg, 10, 20, 100, 140, 'card-a')
    pdf.image(jpeg, 110, 20, 100, 140, 'card-a')
    pdf.image(fakeJpeg(8, 8, 1), 0, 0, 8, 8)
    const parsed = parsePdf(pdf.toBytes())
    const images = [...parsed.objects.values()].filter((object) => object.includes('/Subtype /Image'))

    expect(images).toHaveLength(2)
    expect(images[0]).toContain('/Width 672 /Height 936 /ColorSpace /DeviceRGB /BitsPerComponent 8')
    expect(images[0]).toContain('/Filter /DCTDecode')
    expect(images[1]).toContain('/ColorSpace /DeviceGray')
    const content = streamOf([...parsed.objects.values()].find((object) => object.includes(' cm /Im'))!)
    // The PDF origin is at the bottom left: the y value is 792 − 20 − 140 = 632.
    expect(content).toContain('q 100 0 0 140 10 632 cm /Im3 Do Q')
    expect(content).toContain('q 100 0 0 140 110 632 cm /Im3 Do Q')
  })

  it('marks the values of an Adobe CMYK image as inverted', () => {
    const pdf = new PdfWriter()
    pdf.addPage(100, 100)
    pdf.image(fakeJpeg(2, 2, 4, { adobe: true }), 0, 0, 2, 2)
    const image = [...parsePdf(pdf.toBytes()).objects.values()].find((object) => object.includes('/Subtype /Image'))
    expect(image).toContain('/ColorSpace /DeviceCMYK')
    expect(image).toContain('/Decode [1 0 1 0 1 0 1 0]')
  })

  it('escapes the parentheses and the backslash, and writes the codes over 126 as octal values', () => {
    expect(pdfString(toWinAnsi('a(b)c\\d'))).toBe('(a\\(b\\)c\\\\d)')
    expect(pdfString(toWinAnsi('Æ€'))).toBe('(\\306\\200)')
  })

  it('changes the characters that WinAnsi does not have', () => {
    expect(toWinAnsi('Lim-Dûl')).toEqual([...'Lim-Dûl'].map((character) => character.charCodeAt(0)))
    // ő has no WinAnsi code. The accent goes, and the letter stays.
    expect(String.fromCharCode(...toWinAnsi('Győr'))).toBe('Gyor')
    expect(String.fromCharCode(...toWinAnsi('神 🐉'))).toBe('? ?')
    expect(toWinAnsi('’—')).toEqual([0x92, 0x97])
    expect(toWinAnsi('a\nb')).toEqual([0x61, 0x20, 0x62])
  })

  it('measures and centers text with the Helvetica widths', () => {
    const pdf = new PdfWriter()
    pdf.addPage(200, 100)
    pdf.setFont('Helvetica', 10)
    // H = 722, i = 222.
    expect(pdf.textWidth('Hi')).toBeCloseTo(9.44, 6)
    pdf.setFont('Helvetica-Bold', 10)
    // H = 722, i = 278.
    expect(pdf.textWidth('Hi')).toBeCloseTo(10, 6)
    pdf.text('Hi', 100, 50, { align: 'center' })
    const parsed = parsePdf(pdf.toBytes())
    const content = streamOf([...parsed.objects.values()].find((object) => object.includes(' Tj ET'))!)
    expect(content).toContain('BT /F2 10 Tf 95 50 Td (Hi) Tj ET')
    expect([...parsed.objects.values()].some((object) => object.includes('/BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding'))).toBe(true)
  })

  it('divides a long text into lines that fit', () => {
    const pdf = new PdfWriter()
    pdf.addPage(100, 100)
    pdf.setFont('Helvetica-Bold', 12)
    const lines = pdf.splitText('Asmoranomardicadaistinaculdacar the Long Name', 80)
    expect(lines.length).toBeGreaterThan(1)
    for (const line of lines) {
      expect(pdf.textWidth(line)).toBeLessThanOrEqual(80)
    }
    expect(lines.join(' ').replace(/ /g, '')).toBe('AsmoranomardicadaistinaculdacartheLongName')
  })

  it('draws a rounded rectangle with 4 Bézier curves', () => {
    const pdf = new PdfWriter()
    pdf.addPage(100, 100)
    pdf.roundedRect(10, 10, 50, 30, 5)
    const content = streamOf([...parsePdf(pdf.toBytes()).objects.values()].find((object) => object.includes(' h S'))!)
    expect(content.match(/ c /g)).toHaveLength(4)
    expect(content.startsWith('1 w')).toBe(true)
  })

  it('refuses a PDF with no page', () => {
    expect(() => new PdfWriter().toBytes()).toThrow('no page')
  })
})
