import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { PluginActionContribution } from '../slots/registry'
import { PluginIconImage } from './icons'
import { NotCheckedBanner } from './NotCheckedBanner'
import { pluginText } from '../slots/pluginText'

interface PluginActionProps {
  contribution: PluginActionContribution
  context: unknown
  /** The style of the button: a normal button in an action bar, or a link style in a menu. */
  appearance: 'button' | 'link'
}

/**
 * A button or a menu item of a plugin. The core renders the data of the plugin with this component.
 * The button stays disabled while the plugin runs the action.
 */
export function PluginAction({ contribution, context, appearance }: PluginActionProps) {
  const { t, i18n } = useTranslation()
  const [running, setRunning] = useState(false)
  const [failed, setFailed] = useState(false)
  const label = pluginText(contribution.label, i18n.resolvedLanguage)

  async function handleClick() {
    setRunning(true)
    setFailed(false)
    try {
      await contribution.run(context)
    } catch (error) {
      console.warn(`The action "${contribution.id}" of the plugin "${contribution.plugin.id}" failed.`, error)
      setFailed(true)
    } finally {
      setRunning(false)
    }
  }

  return (
    <span className="plugin-action" data-plugin={contribution.plugin.id}>
      <button type="button" className={appearance === 'link' ? 'button-link' : undefined} disabled={running} aria-busy={running || undefined} onClick={() => void handleClick()}>
        {contribution.icon && <PluginIconImage icon={contribution.icon} />}
        {label}
      </button>
      {contribution.plugin.development && <NotCheckedBanner name={contribution.plugin.name} compact />}
      {failed && (
        <span className="field-error" role="alert">
          {t('plugins.actionFailed', { name: contribution.plugin.name })}
        </span>
      )}
    </span>
  )
}
