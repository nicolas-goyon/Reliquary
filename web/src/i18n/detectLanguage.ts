/** Finds the interface language before i18next starts. It replaces i18next-browser-languagedetector. */

/** The part of the Web Storage API that the detection uses. */
export interface LanguageStore {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
}

/** The part of the Navigator API that the detection uses. */
export interface LanguageSource {
  readonly languages?: readonly string[]
  readonly language?: string
}

/**
 * Gives the supported language for a BCP 47 code, or undefined.
 * The comparison ignores the case and the region: "fr-CA" and "FR" give "fr".
 */
export function matchLanguage<T extends string>(code: string | null | undefined, supported: readonly T[]): T | undefined {
  if (!code) return undefined
  const base = code.trim().toLowerCase().split(/[-_]/)[0]
  return supported.find((language) => language === base)
}

/**
 * Gives the interface language, in this order:
 * 1. the language in the store (the choice of the user),
 * 2. the languages of the browser, in the order of preference,
 * 3. the fallback language.
 */
export function detectLanguage<T extends string>(options: {
  supported: readonly T[]
  fallback: T
  storageKey: string
  store?: LanguageStore
  source?: LanguageSource
}): T {
  const { supported, fallback, storageKey, store, source } = options
  const stored = matchLanguage(readStored(store, storageKey), supported)
  if (stored) return stored
  const browserLanguages = [...(source?.languages ?? []), ...(source?.language ? [source.language] : [])]
  for (const code of browserLanguages) {
    const match = matchLanguage(code, supported)
    if (match) return match
  }
  return fallback
}

/** Keeps the language in the store. It does nothing when the store is not available (for example, blocked storage). */
export function storeLanguage(store: LanguageStore | undefined, storageKey: string, language: string): void {
  try {
    store?.setItem(storageKey, language)
  } catch {
    // The language then comes from the browser at the next start.
  }
}

function readStored(store: LanguageStore | undefined, storageKey: string): string | null {
  try {
    return store?.getItem(storageKey) ?? null
  } catch {
    return null
  }
}

/** Gives window.localStorage, or undefined when the browser blocks it. */
export function browserStore(): LanguageStore | undefined {
  try {
    return typeof window === 'undefined' ? undefined : window.localStorage
  } catch {
    return undefined
  }
}
