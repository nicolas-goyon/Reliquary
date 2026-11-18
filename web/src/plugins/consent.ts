/**
 * The consent of the user for the access of a plugin: scopes, network origins, devices and linked account sites.
 * A plugin runs only when the user accepted all the access of its installed version. See issue #287.
 */

import type { Extension } from '../api/extensions'
import { pluginText } from '../slots/pluginText'
import type { PluginText } from '../slots/registry'

/** The access that a plugin version asks for, or that the user accepted. */
export interface PluginAccess {
  scopes: string[]
  network: string[]
  devices: string[]
  sites: string[]
}

/** Gets the access that the installed version of a plugin asks for. */
export function askedAccess(plugin: Pick<Extension, 'permissions' | 'network' | 'devices' | 'sites'>): PluginAccess {
  return { scopes: plugin.permissions ?? [], network: plugin.network ?? [], devices: plugin.devices ?? [], sites: plugin.sites ?? [] }
}

/** Gets the access that the user accepted, or no access. */
export function acceptedAccess(plugin: Pick<Extension, 'consent'>): PluginAccess {
  const consent = plugin.consent
  return consent ? { scopes: consent.scopes, network: consent.network, devices: consent.devices, sites: consent.sites } : emptyAccess()
}

export function emptyAccess(): PluginAccess {
  return { scopes: [], network: [], devices: [], sites: [] }
}

/** Gets the access of the installed version that the user did not accept. The comparison uses the same text, as the API. */
export function missingAccess(plugin: Extension): PluginAccess {
  const asked = askedAccess(plugin)
  const accepted = acceptedAccess(plugin)
  const missing = (values: string[], known: string[]) => values.filter((value) => !known.includes(value))
  return {
    scopes: missing(asked.scopes, accepted.scopes),
    network: missing(asked.network, accepted.network),
    devices: missing(asked.devices, accepted.devices),
    sites: missing(asked.sites, accepted.sites),
  }
}

export function isEmptyAccess(access: PluginAccess): boolean {
  return access.scopes.length === 0 && access.network.length === 0 && access.devices.length === 0 && access.sites.length === 0
}

/** Gets the access that the frame of a running plugin gets: the access of the installed version that the user accepted. */
export function frameAccess(plugin: Extension): Pick<PluginAccess, 'network' | 'devices'> {
  const accepted = acceptedAccess(plugin)
  return {
    network: (plugin.network ?? []).filter((origin) => accepted.network.includes(origin)),
    devices: (plugin.devices ?? []).filter((device) => accepted.devices.includes(device)),
  }
}

/** A network origin in plain words. */
export type NetworkTarget =
  | { kind: 'site'; host: string }
  | { kind: 'subdomains'; host: string }
  | { kind: 'computer'; port: string | null }

/**
 * Gets the plain-word form of a network origin of a plugin manifest:
 * a site, all the sub-domains of a site for a wildcard, or the computer of the user for `localhost` and `127.0.0.1`.
 */
export function networkTarget(origin: string): NetworkTarget {
  const match = /^(https?):\/\/(\*\.)?([^/:]+)(?::(\d+))?\/?$/.exec(origin)
  if (!match) {
    return { kind: 'site', host: origin }
  }
  const [, , wildcard, host, port] = match
  if (host === 'localhost' || host === '127.0.0.1') {
    return { kind: 'computer', port: port ?? null }
  }
  const shown = port ? `${host}:${port}` : host
  return wildcard ? { kind: 'subdomains', host: shown } : { kind: 'site', host: shown }
}

/** Gets the body of `PUT /me/extensions/{id}` that accepts all the access of the installed version and activates the plugin. */
export function consentBody(plugin: Extension) {
  return { enabled: true, consent: { version: plugin.version, ...askedAccess(plugin) } }
}

/** Gets the name of a plugin in the language of the interface, from the key `name` of the translations of its manifest. */
export function pluginName(plugin: Pick<Extension, 'name' | 'i18n'>, language: string | undefined): string {
  const texts: PluginText = { en: plugin.name }
  for (const [code, values] of Object.entries(plugin.i18n ?? {})) {
    if (code !== 'en' && typeof values?.name === 'string') {
      texts[code] = values.name
    }
  }
  return pluginText(texts, language)
}
