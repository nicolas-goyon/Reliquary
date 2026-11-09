import { describe, expect, it } from 'vitest'
import { detectLanguage, matchLanguage, storeLanguage, type LanguageStore } from './detectLanguage'

const supported = ['en', 'fr'] as const
const storageKey = 'reliquary.language'

function memoryStore(values: Record<string, string> = {}): LanguageStore & { values: Record<string, string> } {
  return {
    values,
    getItem: (key) => values[key] ?? null,
    setItem: (key, value) => {
      values[key] = value
    },
  }
}

const blockedStore: LanguageStore = {
  getItem: () => {
    throw new Error('blocked')
  },
  setItem: () => {
    throw new Error('blocked')
  },
}

function detect(store?: LanguageStore, source?: { languages?: readonly string[]; language?: string }) {
  return detectLanguage({ supported, fallback: 'en', storageKey, store, source })
}

describe('matchLanguage', () => {
  it('ignores the region and the case', () => {
    expect(matchLanguage('fr-CA', supported)).toBe('fr')
    expect(matchLanguage('FR', supported)).toBe('fr')
    expect(matchLanguage('en_GB', supported)).toBe('en')
  })

  it('gives undefined for an unsupported or empty code', () => {
    expect(matchLanguage('de-DE', supported)).toBeUndefined()
    expect(matchLanguage('', supported)).toBeUndefined()
    expect(matchLanguage(null, supported)).toBeUndefined()
  })
})

describe('detectLanguage', () => {
  it('uses the stored language first', () => {
    expect(detect(memoryStore({ [storageKey]: 'fr' }), { languages: ['en-US'] })).toBe('fr')
  })

  it('reads a stored value with a region, as the old detector wrote it', () => {
    expect(detect(memoryStore({ [storageKey]: 'fr-CA' }), { languages: ['en-US'] })).toBe('fr')
  })

  it('ignores an unsupported stored value and uses the browser languages', () => {
    expect(detect(memoryStore({ [storageKey]: 'de' }), { languages: ['fr-FR'] })).toBe('fr')
  })

  it('uses the first supported browser language, in the order of preference', () => {
    expect(detect(memoryStore(), { languages: ['de-DE', 'fr-CA', 'en'] })).toBe('fr')
  })

  it('uses navigator.language when navigator.languages is empty', () => {
    expect(detect(memoryStore(), { languages: [], language: 'fr-BE' })).toBe('fr')
  })

  it('uses the fallback when no language is supported', () => {
    expect(detect(memoryStore(), { languages: ['de', 'es'], language: 'it' })).toBe('en')
    expect(detect(undefined, undefined)).toBe('en')
  })

  it('uses the browser languages when the storage is blocked', () => {
    expect(detect(blockedStore, { languages: ['fr'] })).toBe('fr')
  })
})

describe('storeLanguage', () => {
  it('writes the language with the storage key', () => {
    const store = memoryStore()
    storeLanguage(store, storageKey, 'fr')
    expect(store.values[storageKey]).toBe('fr')
  })

  it('does not throw when the storage is blocked', () => {
    expect(() => storeLanguage(blockedStore, storageKey, 'fr')).not.toThrow()
    expect(() => storeLanguage(undefined, storageKey, 'fr')).not.toThrow()
  })
})
