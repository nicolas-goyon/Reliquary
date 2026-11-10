import { useTranslation } from 'react-i18next'
import { pluginText } from '../slots/pluginText'
import { isPluginPanel, useSlotContributions } from '../slots/registry'
import { SlotItem } from '../slots/Slot'

/**
 * A page of a plugin at /plugins/<plugin ID>/<page>: the consumer of the slot "plugin.page".
 * It shows the panel of the plugin with this ID. The page has no code for a specific plugin.
 */
export function PluginPage({ pluginId, page }: { pluginId: string; page: string }) {
  const { t, i18n } = useTranslation()
  const contribution = useSlotContributions('plugin.page').find(
    (item) => isPluginPanel(item) && item.plugin.id === pluginId && item.panelId === page,
  )
  if (!contribution || !isPluginPanel(contribution)) {
    return (
      <section className="page">
        <h1>{t('plugins.pageTitle')}</h1>
        <p role="status">{t('plugins.pageUnavailable')}</p>
      </section>
    )
  }
  return (
    <section className="page plugin-page">
      <h1>{pluginText(contribution.title, i18n.resolvedLanguage)}</h1>
      <SlotItem key={contribution.id} slot="plugin.page" contribution={contribution} context={{ pluginId, page }} />
    </section>
  )
}
