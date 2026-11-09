import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import enAccount from '../locales/en/account.json'
import enAuth from '../locales/en/auth.json'
import enCards from '../locales/en/cards.json'
import enCollection from '../locales/en/collection.json'
import enCommon from '../locales/en/common.json'
import enDecks from '../locales/en/decks.json'
import enModeration from '../locales/en/moderation.json'
import enPlaytest from '../locales/en/playtest.json'
import enPurchases from '../locales/en/purchases.json'
import enVersions from '../locales/en/versions.json'
import frAccount from '../locales/fr/account.json'
import frAuth from '../locales/fr/auth.json'
import frCards from '../locales/fr/cards.json'
import frCollection from '../locales/fr/collection.json'
import frCommon from '../locales/fr/common.json'
import frDecks from '../locales/fr/decks.json'
import frModeration from '../locales/fr/moderation.json'
import frPlaytest from '../locales/fr/playtest.json'
import frPurchases from '../locales/fr/purchases.json'
import frVersions from '../locales/fr/versions.json'
import { browserStore, detectLanguage, storeLanguage } from './detectLanguage'

/** The interface languages. English is the source language. */
export const supportedLanguages = ['en', 'fr'] as const
export type SupportedLanguage = (typeof supportedLanguages)[number]

export const defaultNamespace = 'common'

/** The translation resources. English gives the TypeScript types of the keys. */
export const resources = {
  en: { common: enCommon, auth: enAuth, account: enAccount, cards: enCards, decks: enDecks, moderation: enModeration, versions: enVersions, collection: enCollection, purchases: enPurchases, playtest: enPlaytest },
  fr: { common: frCommon, auth: frAuth, account: frAccount, cards: frCards, decks: frDecks, moderation: frModeration, versions: frVersions, collection: frCollection, purchases: frPurchases, playtest: frPlaytest },
} as const

/** The local storage key that keeps the language that the user chose. */
export const languageStorageKey = 'reliquary.language'

export function isSupportedLanguage(language: string | undefined): language is SupportedLanguage {
  return supportedLanguages.some((supported) => supported === language)
}

const store = browserStore()

i18n.on('languageChanged', (language) => {
  document.documentElement.lang = language
  // "cimode" shows the keys. It is not a language of the user.
  if (language !== 'cimode') storeLanguage(store, languageStorageKey, language)
})

void i18n
  .use(initReactI18next)
  .init({
    resources,
    lng: detectLanguage({
      supported: supportedLanguages,
      fallback: 'en',
      storageKey: languageStorageKey,
      store,
      source: typeof navigator === 'undefined' ? undefined : navigator,
    }),
    supportedLngs: supportedLanguages,
    fallbackLng: 'en',
    nonExplicitSupportedLngs: true,
    defaultNS: defaultNamespace,
    ns: [defaultNamespace, 'auth', 'account', 'cards', 'decks', 'moderation', 'versions', 'collection', 'purchases', 'playtest'],
    initAsync: false,
    interpolation: {
      // React escapes the values. Escaping them twice shows HTML entities.
      escapeValue: false,
    },
  })

export default i18n
