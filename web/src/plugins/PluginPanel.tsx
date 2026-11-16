import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { pluginText } from '../slots/pluginText'
import type { PanelHandle, PanelState, PluginPanelContribution } from '../slots/registry'
import { NotCheckedBanner } from './NotCheckedBanner'

interface PluginPanelProps {
  contribution: PluginPanelContribution
  context: unknown
  /** True for a panel that the user opens, for example on the card page. The core creates the frame only when the panel opens. */
  collapsible: boolean
}

/**
 * A panel of a plugin: a sandboxed iframe that the core creates when the panel opens, and removes when the panel closes.
 * A slot consumer renders a panel only when it is open, for example the selected tab of the analysis.
 */
export function PluginPanel({ contribution, context, collapsible }: PluginPanelProps) {
  const { i18n } = useTranslation()
  const [open, setOpen] = useState(!collapsible)
  const title = pluginText(contribution.title, i18n.resolvedLanguage)

  if (!collapsible) {
    return (
      <div className="plugin-panel" data-plugin={contribution.plugin.id}>
        <PanelFrame contribution={contribution} context={context} />
      </div>
    )
  }
  return (
    <details className="plugin-panel panel" data-plugin={contribution.plugin.id} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary>{title}</summary>
      {open && <PanelFrame contribution={contribution} context={context} />}
    </details>
  )
}

function PanelFrame({ contribution, context }: { contribution: PluginPanelContribution; context: unknown }) {
  const { t } = useTranslation()
  const container = useRef<HTMLDivElement>(null)
  const handle = useRef<PanelHandle | null>(null)
  const latest = useRef(context)
  const [state, setState] = useState<PanelState>('loading')

  useEffect(() => {
    latest.current = context
    handle.current?.update(context)
  }, [context])

  useEffect(() => {
    if (!container.current) {
      return
    }
    const opened = contribution.mount(container.current, latest.current, setState)
    handle.current = opened
    return () => {
      handle.current = null
      opened.close()
    }
  }, [contribution])

  return (
    <>
      {contribution.plugin.development && <NotCheckedBanner name={contribution.plugin.name} />}
      {state === 'loading' && (
        <p className="field-hint" role="status">
          {t('plugins.panelLoading', { name: contribution.plugin.name })}
        </p>
      )}
      {state === 'failed' && (
        <p className="alert alert-error" role="alert">
          {t('plugins.panelFailed', { name: contribution.plugin.name })}
        </p>
      )}
      <div ref={container} className="plugin-panel-frames" />
    </>
  )
}
