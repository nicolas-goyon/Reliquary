import { useEffect, useEffectEvent, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { pluginText } from '../slots/pluginText'
import type { PluginCell, PluginColumnContribution, TableColumnContext } from '../slots/registry'
import { NotCheckedBanner } from './NotCheckedBanner'

interface PluginColumnProps {
  contribution: PluginColumnContribution
  context: TableColumnContext
}

/**
 * The header or a cell of a plugin column. The core renders the text of the plugin: a cell has no iframe.
 * The cells of one deck version load in one call to the plugin.
 */
export function PluginColumn({ contribution, context }: PluginColumnProps) {
  const { i18n } = useTranslation()
  if (context.part === 'header') {
    return (
      <span className="plugin-column-header">
        {pluginText(contribution.title, i18n.resolvedLanguage)}
        {contribution.plugin.development && <NotCheckedBanner name={contribution.plugin.name} compact />}
      </span>
    )
  }
  return <PluginColumnCell contribution={contribution} context={context} />
}

type CellResult = { status: 'ready'; cell: PluginCell | null } | { status: 'failed' }

function PluginColumnCell({ contribution, context }: { contribution: PluginColumnContribution; context: Extract<TableColumnContext, { part: 'cell' }> }) {
  const { t } = useTranslation()
  // The cell depends only on the plugin column, the deck version and the line.
  const key = `${contribution.id} ${context.deckId} ${context.deckVersion} ${context.line.id}`
  const [result, setResult] = useState<{ key: string; value: CellResult } | null>(null)
  const load = useEffectEvent(() => contribution.cell(context))

  useEffect(() => {
    let cancelled = false
    load().then(
      (cell) => !cancelled && setResult({ key, value: { status: 'ready', cell } }),
      () => !cancelled && setResult({ key, value: { status: 'failed' } }),
    )
    return () => {
      cancelled = true
    }
  }, [key])

  const state = result?.key === key ? result.value : null
  if (!state) {
    return <span className="plugin-cell plugin-cell-loading" aria-label={t('plugins.cellLoading')} />
  }
  if (state.status === 'failed') {
    return (
      <span className="plugin-cell" title={t('plugins.cellFailed', { name: contribution.plugin.name })}>
        <span aria-hidden="true">—</span>
        <span className="visually-hidden">{t('plugins.cellFailed', { name: contribution.plugin.name })}</span>
      </span>
    )
  }
  if (!state.cell) {
    return null
  }
  const { text, tone = 'neutral', tooltip } = state.cell
  return (
    <span className={`plugin-cell plugin-cell-${tone}`} title={tooltip}>
      {text}
      {tooltip && <span className="visually-hidden"> {tooltip}</span>}
    </span>
  )
}
