import { useContext, useState, useSyncExternalStore, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Extension } from '../api/extensions'
import { useAuth } from '../auth/session'
import { ErrorAlert, TextField } from '../components/Forms'
import { pluginText } from '../slots/pluginText'
import { contributionPlugin, layoutKey, slotNames, SlotRegistryContext, type PluginText, type SlotName } from '../slots/registry'
import { pluginName } from './consent'
import { notifyPluginsChanged } from './events'

/** The maximum distance of a position from 0. The API has the same limit. */
const maxPosition = 1000

/** The translation key of the name of each slot where the user can hide a contribution of a plugin. */
const slotKeys = {
  navigation: 'plugins.layout.slots.navigation',
  'app.menu': 'plugins.layout.slots.appMenu',
  'deck.actions': 'plugins.layout.slots.deckActions',
  'analysis.tabs': 'plugins.layout.slots.analysisTabs',
  'table.columns': 'plugins.layout.slots.tableColumns',
  'card.page': 'plugins.layout.slots.cardPage',
  'deck.import-formats': 'plugins.layout.slots.importFormats',
  'deck.export-formats': 'plugins.layout.slots.exportFormats',
  'collection.import-formats': 'plugins.layout.slots.importFormats',
  'collection.export-formats': 'plugins.layout.slots.exportFormats',
} as const satisfies Partial<Record<SlotName, string>>

interface Item {
  key: string
  slot: string
  text: string
}

/**
 * The layout of an active plugin for the user: the position of the plugin among the plugins, and the contributions that the user hides.
 * The list shows the contributions that the plugin has now in the slots, and the hidden contributions of the saved layout. See issue #278.
 */
export function PluginLayoutForm({ plugin, onSaved }: { plugin: Extension; onSaved(): void }) {
  const { t, i18n } = useTranslation('account')
  const { request } = useAuth()
  const registry = useContext(SlotRegistryContext)
  useSyncExternalStore(registry?.subscribe ?? (() => () => undefined), registry?.getVersion ?? (() => 0))
  const [position, setPosition] = useState(String(plugin.layout?.position ?? 0))
  const [hidden, setHidden] = useState<string[]>(plugin.layout?.hidden ?? [])
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const name = pluginName(plugin, i18n.resolvedLanguage)

  const items: Item[] = []
  for (const slot of slotNames) {
    for (const contribution of registry?.listAll(slot) ?? []) {
      const key = layoutKey(slot, contribution)
      if (key === null || contributionPlugin(contribution)?.id !== plugin.id) {
        continue
      }
      const text = ('label' in contribution ? contribution.label : 'title' in contribution ? contribution.title : null) as PluginText | null
      items.push({ key, slot, text: text ? pluginText(text, i18n.resolvedLanguage) : key })
    }
  }
  // A hidden contribution that the plugin does not have now stays in the list, so the user can show it again.
  for (const key of hidden) {
    if (!items.some((item) => item.key === key)) {
      const separator = key.indexOf(':')
      items.push({ key, slot: key.slice(0, separator), text: key.slice(separator + 1) })
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const number = Math.max(-maxPosition, Math.min(maxPosition, Math.trunc(Number(position)) || 0))
      await request(`/me/extensions/${encodeURIComponent(plugin.id)}/layout`, { method: 'PUT', body: { position: number, hidden } })
      notifyPluginsChanged()
      onSaved()
    } catch (caught) {
      setError(caught)
    } finally {
      setBusy(false)
    }
  }

  return (
    <details className="plugin-layout">
      <summary>{t('plugins.layout.title', { name })}</summary>
      <form onSubmit={(event) => void handleSubmit(event)}>
        <TextField
          label={t('plugins.layout.position')}
          hint={t('plugins.layout.positionHint')}
          type="number"
          min={-maxPosition}
          max={maxPosition}
          step={1}
          value={position}
          onChange={(event) => setPosition(event.target.value)}
        />
        <fieldset>
          <legend>{t('plugins.layout.visible')}</legend>
          {items.length === 0 && <p className="field-hint">{t('plugins.layout.none')}</p>}
          {items.map((item) => (
            <label key={item.key} className="filter-check">
              <input
                type="checkbox"
                checked={!hidden.includes(item.key)}
                onChange={() => setHidden(hidden.includes(item.key) ? hidden.filter((key) => key !== item.key) : [...hidden, item.key])}
              />
              {item.slot in slotKeys ? t('plugins.layout.item', { text: item.text, slot: t(slotKeys[item.slot as keyof typeof slotKeys]) }) : item.text}
            </label>
          ))}
        </fieldset>
        <ErrorAlert error={error} />
        <div className="form-actions">
          <button type="submit" disabled={busy}>
            {t('plugins.layout.save')}
          </button>
        </div>
      </form>
    </details>
  )
}
