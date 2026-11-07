import { describe, expect, it } from 'vitest'
import { supportedLanguages } from '../i18n'

// Each file is ./<language>/<namespace>.json
const files = import.meta.glob<Record<string, unknown>>('./*/*.json', { eager: true, import: 'default' })

function namespacesOf(language: string): Map<string, Record<string, unknown>> {
  const namespaces = new Map<string, Record<string, unknown>>()
  for (const [path, content] of Object.entries(files)) {
    const [, fileLanguage, namespace] = /^\.\/([^/]+)\/([^/]+)\.json$/.exec(path) ?? []
    if (fileLanguage === language && namespace) {
      namespaces.set(namespace, content)
    }
  }
  return namespaces
}

function flatten(value: unknown, prefix = ''): Map<string, string> {
  const entries = new Map<string, string>()
  if (typeof value === 'string') {
    entries.set(prefix, value)
  } else if (value && typeof value === 'object') {
    for (const [key, child] of Object.entries(value)) {
      for (const [childKey, text] of flatten(child, prefix ? `${prefix}.${key}` : key)) {
        entries.set(childKey, text)
      }
    }
  }
  return entries
}

function variablesOf(text: string): string {
  return [...text.matchAll(/\{\{\s*([\w.]+)\s*\}\}/g)]
    .map((match) => match[1])
    .sort()
    .join(',')
}

const [sourceLanguage, ...otherLanguages] = supportedLanguages
const sourceNamespaces = namespacesOf(sourceLanguage)

describe('translations', () => {
  it('have at least one namespace in the source language', () => {
    expect(sourceNamespaces.size).toBeGreaterThan(0)
  })

  for (const language of otherLanguages) {
    describe(`in ${language}`, () => {
      const namespaces = namespacesOf(language)

      it('have the same namespaces as the source language', () => {
        expect([...namespaces.keys()].sort()).toEqual([...sourceNamespaces.keys()].sort())
      })

      for (const [namespace, sourceContent] of sourceNamespaces) {
        const sourceTexts = flatten(sourceContent)
        const texts = flatten(namespaces.get(namespace))

        it(`have the same keys as the source language in ${namespace}`, () => {
          expect([...texts.keys()].sort()).toEqual([...sourceTexts.keys()].sort())
        })

        it(`keep the interpolation variables of the source language in ${namespace}`, () => {
          const differentKeys = [...sourceTexts]
            .filter(([key, sourceText]) => variablesOf(texts.get(key) ?? '') !== variablesOf(sourceText))
            .map(([key]) => key)
          expect(differentKeys).toEqual([])
        })
      }
    })
  }

  for (const language of supportedLanguages) {
    it(`have no empty text in ${language}`, () => {
      const emptyKeys = [...namespacesOf(language)].flatMap(([namespace, content]) =>
        [...flatten(content)].filter(([, text]) => text.trim() === '').map(([key]) => `${namespace}:${key}`),
      )
      expect(emptyKeys).toEqual([])
    })
  }
})
