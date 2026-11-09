/** Reads the header of a JPEG image (ITU-T T.81). The PDF writer and the image loader use it. */

export interface JpegInfo {
  width: number
  height: number
  /** 1 for gray, 3 for RGB, 4 for CMYK. */
  components: 1 | 3 | 4
  /** True for a CMYK image from Adobe software. These images keep the inverted values (APP14 marker). */
  adobeInverted: boolean
}

/** The SOF markers of the JPEG processes that the PDF DCTDecode filter accepts: baseline, extended and progressive, with Huffman coding. */
const supportedSof = new Set([0xc0, 0xc1, 0xc2])
/** The other SOF markers: lossless, hierarchical and arithmetic coding. */
const unsupportedSof = new Set([0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf])

/** Reads the dimensions and the color components of a JPEG image from its SOF marker. Throws for data that the writer cannot use. */
export function readJpegInfo(data: Uint8Array): JpegInfo {
  if (data[0] !== 0xff || data[1] !== 0xd8) {
    throw new Error('The data is not a JPEG image.')
  }
  let adobeInverted = false
  let offset = 2
  while (offset + 4 <= data.length) {
    if (data[offset] !== 0xff) {
      throw new Error('The JPEG image has a marker that is not correct.')
    }
    const marker = data[offset + 1]
    if (marker === 0xff) {
      offset += 1 // A fill byte.
      continue
    }
    const length = (data[offset + 2] << 8) | data[offset + 3]
    // APP14 "Adobe": the CMYK values of the image are inverted.
    if (marker === 0xee && String.fromCharCode(...data.subarray(offset + 4, offset + 9)) === 'Adobe') {
      adobeInverted = true
    }
    if (unsupportedSof.has(marker)) {
      throw new Error('The JPEG image uses a coding process that PDF does not accept.')
    }
    if (supportedSof.has(marker)) {
      if (offset + 10 > data.length) {
        break
      }
      const precision = data[offset + 4]
      const height = (data[offset + 5] << 8) | data[offset + 6]
      const width = (data[offset + 7] << 8) | data[offset + 8]
      const components = data[offset + 9]
      if (precision !== 8) {
        throw new Error(`The JPEG image has ${precision} bits for each component. The writer accepts only 8 bits.`)
      }
      if (components !== 1 && components !== 3 && components !== 4) {
        throw new Error(`The JPEG image has ${components} color components. The writer accepts 1, 3 or 4.`)
      }
      if (width === 0 || height === 0) {
        throw new Error('The JPEG image has no width or no height.')
      }
      return { width, height, components, adobeInverted: adobeInverted && components === 4 }
    }
    if (marker === 0xda || marker === 0xd9) {
      break // The image data starts before a SOF marker.
    }
    offset += 2 + length
  }
  throw new Error('The JPEG image has no SOF marker.')
}
