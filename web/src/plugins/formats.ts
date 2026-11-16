import type { ExtensionFormat } from '../api/extensions'
import type { ConvertedFile, PluginFormatSlot, PluginText, SlotRegistry } from '../slots/registry'
import type { HostPeer } from './connection'
import { errorCodes } from './protocol'
import { RpcError } from './rpc'

/** The time limit of a conversion, in milliseconds. A plugin can need more time for a large file. */
export const conversionTimeoutMs = 30_000

/** The media types that an export format can give. Other types become "text/plain". */
export const exportContentTypes = ['text/plain', 'text/csv', 'text/tab-separated-values', 'application/json', 'application/xml', 'text/xml'] as const

/** The data of a plugin that its formats need. */
export interface FormatPlugin {
  id: string
  name: string
  development: boolean
  formats: ExtensionFormat[]
  i18n: Record<string, Record<string, string>> | null
}

/** The plugin formats come in the select after the formats of the core. */
const pluginOrderBase = 1000
const extensionPattern = /^\.[a-z0-9]{1,10}$/

function formatTitle(plugin: FormatPlugin, format: ExtensionFormat): PluginText {
  const title: PluginText = { en: format.title }
  for (const [language, texts] of Object.entries(plugin.i18n ?? {})) {
    const text = texts?.[`formats.${format.id}.title`]
    if (language !== 'en' && typeof text === 'string' && text.trim()) {
      title[language] = text
    }
  }
  return title
}

/** Makes a safe file name: no folder, 100 characters at most. */
export function safeFileName(value: unknown, fallback: string): string {
  const text = typeof value === 'string' ? value : ''
  // Replace the folder separators, the characters that file systems refuse, and the control characters.
  const name = [...text].map((character) => ('\\/:*?"<>|'.includes(character) || character.charCodeAt(0) < 32 ? '_' : character)).join('').trim()
  return (name && name !== '.' && name !== '..' ? name : fallback).slice(0, 100)
}

/** Checks the answer of the plugin to `format.import` or `format.export`. */
export function readConversion(direction: ExtensionFormat['direction'], formatId: string, value: unknown): ConvertedFile {
  const result = (value ?? {}) as Record<string, unknown>
  if (typeof result.content !== 'string') {
    throw new RpcError(errorCodes.internalError, `The format "${formatId}" did not give a text.`)
  }
  if (direction === 'import') {
    return { content: result.content, fileName: '', contentType: 'text/plain' }
  }
  const contentType = exportContentTypes.find((type) => type === result.contentType) ?? 'text/plain'
  return { content: result.content, fileName: safeFileName(result.fileName, `${formatId}.txt`), contentType }
}

/**
 * Adds the formats of the plugin manifest to the format slots.
 * @returns A function that removes the formats.
 */
export function registerPluginFormats(peer: HostPeer, plugin: FormatPlugin, registry: SlotRegistry): () => void {
  const removers = plugin.formats.map((format, index) => {
    const slot = `${format.target}.${format.direction}-formats` as PluginFormatSlot
    return registry.register(slot, {
      kind: 'plugin-format',
      id: `plugin.${plugin.id}.${format.id}`,
      order: pluginOrderBase + index,
      plugin: { id: plugin.id, name: plugin.name, development: plugin.development },
      title: formatTitle(plugin, format),
      extensions: format.extensions.filter((extension) => extensionPattern.test(extension)),
      convert: async (content, fileName) => {
        const result =
          format.direction === 'import'
            ? await peer.call('format.import', { id: format.id, content, fileName }, { timeoutMs: conversionTimeoutMs })
            : await peer.call('format.export', { id: format.id, content }, { timeoutMs: conversionTimeoutMs })
        return readConversion(format.direction, format.id, result)
      },
    })
  })
  return () => {
    for (const remove of removers) {
      remove()
    }
  }
}
