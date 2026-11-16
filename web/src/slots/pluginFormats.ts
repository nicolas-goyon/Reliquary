import { ApiError } from '../api/client'
import { isPluginFormat, useSlotContributions, type PluginFormatContribution, type PluginFormatSlot } from './registry'

/**
 * The consumers of the format slots: the import and export forms show the formats of the plugins after the formats of the core.
 * The value of a plugin format in a select is "plugin:<contribution ID>".
 */

const prefix = 'plugin:'

/** The error code of a conversion that the plugin did not complete. */
export const pluginFormatFailed = 'plugin_format_failed'

/** Gets the plugin formats of a slot, in their order. */
export function usePluginFormats(slot: PluginFormatSlot): PluginFormatContribution[] {
  return useSlotContributions(slot).filter(isPluginFormat)
}

/** Gives the value of a plugin format in a select. */
export function pluginFormatValue(format: PluginFormatContribution): string {
  return `${prefix}${format.id}`
}

/** Finds the plugin format of a value of a select, or undefined for a format of the core. */
export function findPluginFormat(formats: PluginFormatContribution[], value: string): PluginFormatContribution | undefined {
  return value.startsWith(prefix) ? formats.find((format) => format.id === value.slice(prefix.length)) : undefined
}

/**
 * Finds the plugin format of a value of a select before a conversion.
 * @throws ApiError "plugin_format_failed" when the value is a plugin format that is no longer available, for example after the plugin stopped.
 */
export function requirePluginFormat(formats: PluginFormatContribution[], value: string): PluginFormatContribution | undefined {
  const format = findPluginFormat(formats, value)
  if (!format && value.startsWith(prefix)) {
    throw new ApiError(0, pluginFormatFailed, 'The plugin of the format is not available.')
  }
  return format
}

/** Finds the plugin format of a file from its extension. */
export function pluginFormatForFile(formats: PluginFormatContribution[], fileName: string): PluginFormatContribution | undefined {
  const name = fileName.toLowerCase()
  return formats.find((format) => format.extensions.some((extension) => name.endsWith(extension)))
}

/** Adds the extensions of the plugin formats to the `accept` attribute of a file input. */
export function acceptWithPluginFormats(accept: string, formats: PluginFormatContribution[]): string {
  return [...new Set([...accept.split(','), ...formats.flatMap((format) => format.extensions)])].join(',')
}

/**
 * Sends the content to the plugin of the format.
 * @throws ApiError with the code "plugin_format_failed" when the plugin fails or does not answer, so ErrorAlert can show it.
 */
export async function convertWithPlugin(format: PluginFormatContribution, content: string, fileName: string | null) {
  try {
    return await format.convert(content, fileName)
  } catch (error) {
    console.warn(`The format "${format.id}" of the plugin "${format.plugin.id}" did not convert the file.`, error)
    throw new ApiError(0, pluginFormatFailed, 'The plugin did not convert the file.')
  }
}
