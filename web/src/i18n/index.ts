import i18n from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { initReactI18next } from 'react-i18next'
import enAccount from '../locales/en/account.json'
import enAuth from '../locales/en/auth.json'
import enCards from '../locales/en/cards.json'
import enCommon from '../locales/en/common.json'
import enDecks from '../locales/en/decks.json'
import frAccount from '../locales/fr/account.json'
import frAuth from '../locales/fr/auth.json'
import frCards from '../locales/fr/cards.json'
import frCommon from '../locales/fr/common.json'
import frDecks from '../locales/fr/decks.json'

/** The interface languages. English is the source language. */
export const supportedLanguages = ['en', 'fr'] as const
export type SupportedLanguage = (typeof supportedLanguages)[number]

export const defaultNamespace = 'common'

/** The translation resources. English gives the TypeScript types of the keys. */
export const resources = {
  en: { common: enCommon, auth: enAuth, account: enAccount, cards: enCards, decks: enDecks },
  fr: { common: frCommon, auth: frAuth, account: frAccount, cards: frCards, decks: frDecks },
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
    ns: [defaultNamespace, 'auth', 'account', 'cards', 'decks'],
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
