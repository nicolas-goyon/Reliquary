/**
 * Gives the declarations of the first CSS block that starts with the selector, for the tests of the theme tokens.
 * The function reads only flat blocks, for example ":root { --color-text: #000; }".
 */
export function cssBlock(css: string, selector: string): Map<string, string> {
  const start = css.indexOf(`${selector} {`)
  if (start < 0) {
    throw new Error(`The CSS has no block for the selector "${selector}".`)
  }
  const body = css.slice(start + selector.length + 2, css.indexOf('}', start))
  return new Map(
    body
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .split(';')
      .map((declaration) => declaration.trim())
      .filter(Boolean)
      .map((declaration) => {
        const [name, ...value] = declaration.split(':')
        return [name.trim(), value.join(':').trim()] as const
      }),
  )
}
