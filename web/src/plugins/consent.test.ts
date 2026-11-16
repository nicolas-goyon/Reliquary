import { describe, expect, it } from 'vitest'
import type { Extension } from '../api/extensions'
import { consentBody, frameAccess, isEmptyAccess, missingAccess, networkTarget, pluginName } from './consent'

function plugin(fields: Partial<Extension> = {}): Extension {
  return {
    id: 'deck-sync',
    kind: 'plugin',
    name: 'Deck sync',
    version: '2.0.0',
    coreApi: '^1.0',
    permissions: ['decks:read', 'decks:write'],
    slots: [],
    enabled: true,
    compatible: true,
    active: false,
    panels: [],
    i18n: null,
    formats: [],
    network: ['https://api.example.com', 'https://*.example.org'],
    devices: ['camera'],
    sites: ['example.com'],
    consent: null,
    paused: false,
    ...fields,
  }
}

describe('plugin consent', () => {
  it('gives all the access when the user accepted nothing', () => {
    expect(missingAccess(plugin())).toEqual({
      scopes: ['decks:read', 'decks:write'],
      network: ['https://api.example.com', 'https://*.example.org'],
      devices: ['camera'],
      sites: ['example.com'],
    })
  })

  it('gives only the new access of a new version', () => {
    const consent = { version: '1.0.0', scopes: ['decks:read'], network: ['https://api.example.com'], devices: ['camera'], sites: ['example.com'], acceptedAt: '2026-11-01T10:00:00Z' }
    const missing = missingAccess(plugin({ consent, paused: true }))

    expect(missing).toEqual({ scopes: ['decks:write'], network: ['https://*.example.org'], devices: [], sites: [] })
    expect(isEmptyAccess(missing)).toBe(false)
    expect(frameAccess(plugin({ consent }))).toEqual({ network: ['https://api.example.com'], devices: ['camera'] })
  })

  it('sends the version and all the access of the version in the consent', () => {
    expect(consentBody(plugin())).toEqual({
      enabled: true,
      consent: { version: '2.0.0', scopes: ['decks:read', 'decks:write'], network: ['https://api.example.com', 'https://*.example.org'], devices: ['camera'], sites: ['example.com'] },
    })
  })

  it('gives network origins in plain words', () => {
    expect(networkTarget('https://api.example.com')).toEqual({ kind: 'site', host: 'api.example.com' })
    expect(networkTarget('https://api.example.com:8443')).toEqual({ kind: 'site', host: 'api.example.com:8443' })
    expect(networkTarget('https://*.example.org')).toEqual({ kind: 'subdomains', host: 'example.org' })
    expect(networkTarget('http://localhost:3000')).toEqual({ kind: 'computer', port: '3000' })
    expect(networkTarget('http://127.0.0.1')).toEqual({ kind: 'computer', port: null })
  })

  it('gives the translated name of the plugin', () => {
    const translated = plugin({ i18n: { fr: { name: 'Synchro de decks' } } })
    expect(pluginName(translated, 'fr')).toBe('Synchro de decks')
    expect(pluginName(translated, 'en')).toBe('Deck sync')
  })
})
