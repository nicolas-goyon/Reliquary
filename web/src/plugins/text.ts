import type { PluginText } from '../slots/registry'

/** Gets the text of a plugin in the language of the interface, or in English. */
export function pluginText(text: PluginText, language: string | undefined): string {
  if (!language) {
    return text.en
  }
  return text[language] ?? text[language.split('-')[0]] ?? text.en
}
