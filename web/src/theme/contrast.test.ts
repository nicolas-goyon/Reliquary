import { describe, expect, it } from 'vitest'
import styles from '../styles.css?raw'
import { cssBlock } from '../test/css'

/** The relative luminance of a color, as WCAG 2.2 defines it. */
function luminance(hex: string): number {
  const [red, green, blue] = [1, 3, 5].map((start) => {
    const channel = Number.parseInt(hex.slice(start, start + 2), 16) / 255
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * red + 0.7152 * green + 0.0722 * blue
}

/** The contrast ratio of two colors, from 1 to 21. */
function contrast(first: string, second: string): number {
  const [light, dark] = [luminance(first), luminance(second)].sort((left, right) => right - left)
  return (light + 0.05) / (dark + 0.05)
}

const themes = {
  light: cssBlock(styles, ':root'),
  dark: cssBlock(styles, ":root[data-theme='dark']"),
}

function token(theme: Map<string, string>, name: string): string {
  const value = theme.get(`--color-${name}`)
  if (!value || !/^#[0-9a-f]{6}$/i.test(value)) {
    throw new Error(`The token --color-${name} is not a color with six hexadecimal digits: ${value}.`)
  }
  return value
}

// WCAG 2.2, level AA: 4.5:1 for normal text (1.4.3), and 3:1 for the parts of a control and for the focus indicator (1.4.11).
const textColors = ['text', 'text-muted', 'link', 'primary', 'danger', 'success', 'warning']
const backgrounds = ['background', 'surface']
const textPairs = [
  ...textColors.flatMap((text) => backgrounds.map((background) => [text, background])),
  ['on-primary', 'primary'],
  ['on-primary', 'danger'],
  ['text-muted', 'chart-track'],
]
const controlPairs = [
  ...['focus', 'control-border'].flatMap((part) => backgrounds.map((background) => [part, background])),
  ['chart-bar', 'chart-track'],
]

describe('color contrast of the theme tokens', () => {
  for (const [name, theme] of Object.entries(themes)) {
    it(`gives 4.5:1 or more to each text color in the ${name} theme`, () => {
      const failures = textPairs
        .map(([text, background]) => ({ pair: `${text} on ${background}`, ratio: Math.round(contrast(token(theme, text), token(theme, background)) * 100) / 100 }))
        .filter(({ ratio }) => ratio < 4.5)
      expect(failures).toEqual([])
    })

    it(`gives 3:1 or more to the controls and the focus indicator in the ${name} theme`, () => {
      const failures = controlPairs
        .map(([part, background]) => ({ pair: `${part} on ${background}`, ratio: Math.round(contrast(token(theme, part), token(theme, background)) * 100) / 100 }))
        .filter(({ ratio }) => ratio < 3)
      expect(failures).toEqual([])
    })
  }

  it('gives 4.5:1 or more to the text of the mana symbols', () => {
    const text = cssBlock(styles, '.mana-symbol').get('color') ?? ''
    const failures = ['.mana-symbol', '.mana-white', '.mana-blue', '.mana-black', '.mana-red', '.mana-green']
      .map((selector) => ({ selector, ratio: contrast(text, cssBlock(styles, selector).get('background-color') ?? '') }))
      .filter(({ ratio }) => ratio < 4.5)
    expect(failures).toEqual([])
  })
})
