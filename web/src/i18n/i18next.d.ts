import 'i18next'
import type { defaultNamespace, resources } from './index'

// Typed translation keys: t('unknown.key') is a TypeScript error.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: typeof defaultNamespace
    resources: (typeof resources)['en']
  }
}
