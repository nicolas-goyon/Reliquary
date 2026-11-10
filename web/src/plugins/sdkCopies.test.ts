import { describe, expect, it } from 'vitest'
import { pluginActionSlots, pluginIcons } from '../slots/registry'

/**
 * The web app keeps a copy of two files of the plugin SDK, because the container build of the web app sees only the folder web/.
 * This test fails when a copy is different from the file in sdk/src. To correct it, copy the file of sdk/src to web/src/plugins.
 * The test ignores the extension ".ts" in the relative imports.
 */
const copies = ['protocol.ts', 'rpc.ts']

// Vite does not serve files outside web/, and the web app has no types of Node.js.
// Thus the test loads node:fs with a dynamic import and gives its own type.
const fsModule = 'node:fs'
const { readFileSync } = (await import(/* @vite-ignore */ fsModule)) as { readFileSync(path: URL, encoding: 'utf8'): string }
const here = import.meta.url

function read(path: string): string {
  return readFileSync(new URL(path, here), 'utf8')
}

function normalize(text: string): string {
  return text.replace(/(from\s+'\.{1,2}\/[^']+?)\.ts'/g, "$1'").replace(/\r\n/g, '\n')
}

describe('copies of the plugin SDK', () => {
  it.each(copies)('web/src/plugins/%s is the same as the file in sdk/src', (file) => {
    const sdk = read(`../../../sdk/src/${file}`)
    const web = read(`./${file}`)

    expect(sdk.length).toBeGreaterThan(0)
    expect(normalize(web)).toBe(normalize(sdk))
  })

  it('gives the same icons as the plugin SDK', () => {
    const sdk = read('../../../sdk/src/slots.ts')
    const list = sdk.match(/export const pluginIcons = \[([^\]]*)\]/)?.[1] ?? ''

    expect([...list.matchAll(/'([^']+)'/g)].map((match) => match[1])).toEqual([...pluginIcons])
  })

  it('gives a context type in the plugin SDK for each slot with plugin data', () => {
    const sdk = read('../../../sdk/src/slots.ts')

    for (const slot of pluginActionSlots) {
      const key = slot.includes('.') ? `'${slot}'` : slot
      expect(sdk).toContain(`${key}: ActionContribution<'${slot}'>`)
    }
  })
})
