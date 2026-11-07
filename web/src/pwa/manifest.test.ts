import { describe, expect, it } from 'vitest'
import indexHtml from '../../index.html?raw'
import manifestText from '../../public/manifest.webmanifest?raw'

interface ManifestIcon {
  src: string
  sizes: string
  type: string
  purpose?: string
}

const manifest = JSON.parse(manifestText) as Record<string, unknown> & { icons: ManifestIcon[] }

// Each PNG icon of the public folder, as a data URL.
const pngIcons = import.meta.glob<string>('../../public/icons/*.png', { eager: true, query: '?inline', import: 'default' })
const svgIcons = import.meta.glob<string>('../../public/icons/*.svg', { eager: true, query: '?raw', import: 'default' })

function publicPath(src: string): string {
  return `../../public${src}`
}

/** Reads the width and the height from the IHDR chunk of a PNG file. */
function pngSize(dataUrl: string): { width: number; height: number } {
  const bytes = Uint8Array.from(atob(dataUrl.slice(dataUrl.indexOf(',') + 1)), (character) => character.charCodeAt(0))
  const signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]
  expect([...bytes.slice(0, 8)]).toEqual(signature)
  const view = new DataView(bytes.buffer)
  return { width: view.getUint32(16), height: view.getUint32(20) }
}

describe('web app manifest', () => {
  it('has the fields that make the web app installable', () => {
    expect(manifest.name).toBe('Reliquary')
    expect(manifest.short_name).toBe('Reliquary')
    expect(manifest.start_url).toBe('/')
    expect(manifest.scope).toBe('/')
    expect(manifest.id).toBe('/')
    expect(['standalone', 'fullscreen', 'minimal-ui']).toContain(manifest.display)
    expect(manifest.theme_color).toMatch(/^#[0-9a-f]{6}$/)
    expect(manifest.background_color).toMatch(/^#[0-9a-f]{6}$/)
  })

  it('has a 192 px icon, a 512 px icon and a maskable icon', () => {
    const pngs = manifest.icons.filter((icon) => icon.type === 'image/png')
    const purposes = (icon: ManifestIcon) => (icon.purpose ?? 'any').split(' ')
    expect(pngs.some((icon) => icon.sizes === '192x192' && purposes(icon).includes('any'))).toBe(true)
    expect(pngs.some((icon) => icon.sizes === '512x512' && purposes(icon).includes('any'))).toBe(true)
    expect(pngs.some((icon) => icon.sizes === '512x512' && purposes(icon).includes('maskable'))).toBe(true)
  })

  it('gives the correct size for each PNG icon', () => {
    for (const icon of manifest.icons.filter((candidate) => candidate.type === 'image/png')) {
      const file = pngIcons[publicPath(icon.src)]
      expect(file).toBeDefined()
      const { width, height } = pngSize(file)
      expect({ src: icon.src, sizes: `${width}x${height}` }).toEqual({ src: icon.src, sizes: icon.sizes })
    }
  })

  it('refers only to icon files that exist', () => {
    for (const icon of manifest.icons) {
      expect({ src: icon.src, exists: (pngIcons[publicPath(icon.src)] ?? svgIcons[publicPath(icon.src)]) !== undefined }).toEqual({ src: icon.src, exists: true })
    }
  })
})

describe('index.html', () => {
  const document = new DOMParser().parseFromString(indexHtml, 'text/html')

  it('links the manifest, the theme color and the Apple touch icon', () => {
    expect(document.querySelector('link[rel="manifest"]')?.getAttribute('href')).toBe('/manifest.webmanifest')
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute('content')).toBe(manifest.theme_color)
    const touchIcon = document.querySelector('link[rel="apple-touch-icon"]')?.getAttribute('href') ?? ''
    expect(pngSize(pngIcons[publicPath(touchIcon)])).toEqual({ width: 180, height: 180 })
  })

  it('has a viewport for small screens', () => {
    expect(document.querySelector('meta[name="viewport"]')?.getAttribute('content')).toContain('width=device-width')
  })
})
