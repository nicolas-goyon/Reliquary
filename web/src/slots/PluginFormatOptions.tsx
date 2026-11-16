import { useTranslation } from 'react-i18next'
import { pluginFormatValue } from './pluginFormats'
import { pluginText } from './pluginText'
import type { PluginFormatContribution } from './registry'

/** The options of the plugin formats in a format select. A format of a development plugin shows "not checked". */
export function PluginFormatOptions({ formats }: { formats: PluginFormatContribution[] }) {
  const { t, i18n } = useTranslation()
  if (formats.length === 0) {
    return null
  }
  return (
    <optgroup label={t('plugins.formatGroup')}>
      {formats.map((format) => {
        const title = pluginText(format.title, i18n.resolvedLanguage)
        return (
          <option key={format.id} value={pluginFormatValue(format)}>
            {format.plugin.development ? t('plugins.notCheckedFormat', { title }) : title}
          </option>
        )
      })}
    </optgroup>
  )
}
