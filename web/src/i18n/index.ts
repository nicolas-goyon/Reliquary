import i18n from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { initReactI18next } from 'react-i18next'
import enCommon from '../locales/en/common.json'
import frCommon from '../locales/fr/common.json'

/** The interface languages. English is the source language. */
export const supportedLanguages = ['en', 'fr'] as const
export type SupportedLanguage = (typeof supportedLanguages)[number]

export const defaultNamespace = 'common'

/** The translation resources. English gives the TypeScript types of the keys. */
export const resources = {
  en: { common: enCommon },
  fr: { common: frCommon },
} as const

/** The local storage key that keeps the language that the user chose. */
export const languageStorageKey = 'reliquary.language'

export function isSupportedLanguage(language: string | undefined): language is SupportedLanguage {
  return supportedLanguages.some((supported) => supported === language)
}

i18n.on('languageChanged', (language) => {
  document.documentElement.lang = language
})

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    supportedLngs: supportedLanguages,
    fallbackLng: 'en',
    nonExplicitSupportedLngs: true,
    defaultNS: defaultNamespace,
    ns: [defaultNamespace],
    initAsync: false,
    interpolation: {
      // React escapes the values. Escaping them twice shows HTML entities.
      escapeValue: false,
    },
    detection: {
      order: ['localStorage', 'navigator'],
      caches: ['localStorage'],
      lookupLocalStorage: languageStorageKey,
    },
  })

export default i18n
