import { describe, expect, it } from 'vitest'

/**
 * Architecture test of the table engine. ADR 0040: the engine is a pure reducer.
 * The test reads the source of engine.ts and of each module that it imports, directly or through other modules.
 * Each of these modules must:
 * - import only modules of the playtest folder, with a relative path,
 * - not import React or another package,
 * - not use the browser, the network, the clock or a random source that is not seeded.
 */

const sources = import.meta.glob<string>(['./*.ts', '!./*.test.ts'], { query: '?raw', import: 'default', eager: true })

/** The entry point of the pure modules. */
const entry = './engine.ts'

/** Global names and calls that make a module impure. Each pattern applies to the code without comments and strings. */
const forbidden: [string, RegExp][] = [
  ['window', /\bwindow\b/],
  ['document', /\bdocument\b/],
  ['navigator', /\bnavigator\b/],
  ['globalThis', /\bglobalThis\b/],
  ['fetch', /\bfetch\s*\(/],
  ['XMLHttpRequest', /\bXMLHttpRequest\b/],
  ['WebSocket', /\bWebSocket\b/],
  ['BroadcastChannel', /\bBroadcastChannel\b/],
  ['localStorage', /\b(localStorage|sessionStorage|indexedDB)\b/],
  ['Date.now', /\bDate\s*\.\s*now\b/],
  ['new Date()', /\bnew\s+Date\s*\(\s*\)/],
  ['performance', /\bperformance\b/],
  ['Math.random', /\bMath\s*\.\s*random\b/],
  ['crypto', /\bcrypto\b/],
  ['timers', /\b(setTimeout|setInterval|requestAnimationFrame|queueMicrotask)\b/],
]

/** Gives the module paths of the static imports, the exports from other modules and the dynamic imports. */
function importsOf(source: string): string[] {
  const pattern = /\b(?:import|export)\s+(?:type\s+)?(?:[^'"`;]*?\s+from\s+)?['"]([^'"]+)['"]|\bimport\s*\(\s*['"]([^'"]+)['"]\s*\)/g
  return [...source.matchAll(pattern)].map((match) => match[1] ?? match[2])
}

/** Removes the comments, the strings and the template literals, so that a word in a comment or a text does not count. */
function codeOnly(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\/|\/\/[^\n]*|'(?:\\.|[^'\\\n])*'|"(?:\\.|[^"\\\n])*"|`(?:\\.|[^`\\])*`/g, ' ')
}

/** Gives the path of a relative import in the playtest folder, or null when the import is not in the folder. */
function resolve(specifier: string): string | null {
  if (!/^\.\/[\w.-]+$/.test(specifier)) return null
  const path = specifier.endsWith('.ts') ? specifier : `${specifier}.ts`
  return path in sources ? path : null
}

/** Finds the modules that engine.ts uses, and the rules that each module breaks. */
function check(): { modules: string[]; violations: string[] } {
  const modules: string[] = []
  const violations: string[] = []
  const queue = [entry]
  while (queue.length > 0) {
    const path = queue.shift()!
    if (modules.includes(path)) continue
    modules.push(path)
    const source = sources[path]
    for (const specifier of importsOf(source)) {
      const target = resolve(specifier)
      if (target === null) violations.push(`${path} imports '${specifier}'`)
      else queue.push(target)
    }
    const code = codeOnly(source)
    for (const [name, pattern] of forbidden) {
      if (pattern.test(code)) violations.push(`${path} uses ${name}`)
    }
  }
  return { modules, violations }
}

describe('table engine purity', () => {
  it('finds the source of the engine', () => {
    expect(Object.keys(sources)).toContain(entry)
    expect(sources[entry]).toContain('export function applyAction')
  })

  it('imports only pure modules of the playtest folder and uses no browser, network, clock or random global', () => {
    expect(check().violations).toEqual([])
  })

  it('finds an import of React, an import outside the folder and the impure globals', () => {
    const source = [
      "import { useState } from 'react'",
      "import type { Deck } from '../api/decks'",
      "export { lineImage } from '../api/decks'",
      "const later = import('./sync')",
      'const id = Math.random()',
      'const now = Date.now()',
      "const data = fetch('/api')",
      '// window is only a word in this comment',
      "const text = 'document'",
    ].join('\n')

    expect(importsOf(source)).toEqual(['react', '../api/decks', '../api/decks', './sync'])
    const code = codeOnly(source)
    const found = forbidden.filter(([, pattern]) => pattern.test(code)).map(([name]) => name)
    expect(found).toEqual(['fetch', 'Date.now', 'Math.random'])
  })
})
