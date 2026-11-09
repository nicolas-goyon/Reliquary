import { describe, expect, it } from 'vitest'
import { readJpegInfo } from './jpeg'
import { fakeJpeg } from './pdfTestParser'

describe('JPEG header', () => {
  it('reads the width, the height and the components from the SOF marker', () => {
    expect(readJpegInfo(fakeJpeg(672, 936))).toEqual({ width: 672, height: 936, components: 3, adobeInverted: false })
    expect(readJpegInfo(fakeJpeg(10, 20, 1, { sof: 0xc2 }))).toMatchObject({ width: 10, height: 20, components: 1 })
  })

  it('finds the inverted CMYK values of an Adobe image', () => {
    expect(readJpegInfo(fakeJpeg(5, 5, 4, { adobe: true }))).toMatchObject({ components: 4, adobeInverted: true })
    expect(readJpegInfo(fakeJpeg(5, 5, 4))).toMatchObject({ components: 4, adobeInverted: false })
  })

  it('refuses data that PDF cannot contain with no change', () => {
    expect(() => readJpegInfo(new Uint8Array([0x89, 0x50, 0x4e, 0x47]))).toThrow('not a JPEG')
    expect(() => readJpegInfo(fakeJpeg(5, 5, 3, { precision: 12 }))).toThrow('8 bits')
    expect(() => readJpegInfo(fakeJpeg(5, 5, 2))).toThrow('2 color components')
    expect(() => readJpegInfo(fakeJpeg(5, 5, 3, { sof: 0xc9 }))).toThrow('coding process')
    expect(() => readJpegInfo(new Uint8Array([0xff, 0xd8, 0xff, 0xd9]))).toThrow('no SOF')
  })
})
