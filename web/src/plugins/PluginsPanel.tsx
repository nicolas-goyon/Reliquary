import { useCallback, useEffect, useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Extension, ExtensionList } from '../api/extensions'
import { useAuth } from '../auth/session'
import { ErrorAlert, SuccessAlert } from '../components/Forms'
import { ConsentDialog } from './ConsentDialog'
import { isEmptyAccess, missingAccess, pluginName } from './consent'
import { notifyPluginsChanged } from './events'
import { PluginLayoutForm } from './PluginLayoutForm'

type Message = { kind: 'activated' | 'deactivated' | 'layoutSaved'; name: string }

/**
 * The plugins of the account page: the plugins that the instance enables, with their state for the user.
 * The user activates a plugin through the consent dialog, accepts the new access of a paused plugin, and deactivates a plugin.
 * The panel does not show when the instance enables no plugin.
 */
export function PluginsPanel() {
  const { t, i18n } = useTranslation('account')
  const { request } = useAuth()
  const id = useId()
  const [plugins, setPlugins] = useState<Extension[]>([])
  const [consent, setConsent] = useState<Extension | null>(null)
  const [message, setMessage] = useState<Message | null>(null)
  const [error, setError] = useState<unknown>(null)

  const load = useCallback(
    () =>
      request<ExtensionList>('/extensions').then((list) => {
        setPlugins((list?.items ?? []).filter((item) => item.kind === 'plugin' && item.compatible))
      }),
    [request],
  )

  useEffect(() => {
    load().catch((caught: unknown) => setError(caught))
  }, [load])

  if (plugins.length === 0) {
    return null
  }
  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })

  function changed(kind: Message['kind'], plugin: Extension) {
    setMessage({ kind, name: pluginName(plugin, i18n.resolvedLanguage) })
    notifyPluginsChanged()
    return load()
  }

  async function handleActivate(plugin: Extension) {
    setError(null)
    setMessage(null)
    // The user must accept the access of the installed version that they did not accept before.
    if (plugin.consent === null || !isEmptyAccess(missingAccess(plugin))) {
      setConsent(plugin)
      return
    }
    try {
      await request(`/me/extensions/${encodeURIComponent(plugin.id)}`, { method: 'PUT', body: { enabled: true } })
      await changed('activated', plugin)
    } catch (caught) {
      setError(caught)
    }
  }

  async function handleDeactivate(plugin: Extension) {
    setError(null)
    setMessage(null)
    try {
      await request(`/me/extensions/${encodeURIComponent(plugin.id)}`, { method: 'PUT', body: { enabled: false } })
      await changed('deactivated', plugin)
    } catch (caught) {
      setError(caught)
    }
  }

  function handleAccepted(plugin: Extension) {
    setConsent(null)
    changed('activated', plugin).catch((caught: unknown) => setError(caught))
  }

  function handleCancel() {
    setConsent(null)
    // The installed version can change while the dialog is open.
    load().catch((caught: unknown) => setError(caught))
  }

  return (
    <section className="panel" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t('plugins.title')}</h2>
      <p>{t('plugins.description')}</p>
      {message && (
        <SuccessAlert>
          {t(message.kind === 'activated' ? 'plugins.activated' : message.kind === 'deactivated' ? 'plugins.deactivated' : 'plugins.layout.saved', { name: message.name })}
        </SuccessAlert>
      )}
      <ul className="plugin-list">
        {plugins.map((plugin) => {
          const name = pluginName(plugin, i18n.resolvedLanguage)
          const activated = plugin.active || plugin.paused
          const disabledByCore = activated ? null : (plugin.disabledByCoreAt ?? null)
          return (
            <li key={plugin.id} className="plugin-list-item">
              <div>
                <strong>{name}</strong> <span className="field-hint">{t('plugins.version', { version: plugin.version })}</span>
                <p className={plugin.paused || disabledByCore ? 'plugin-state plugin-state-paused' : 'plugin-state'}>
                  {plugin.paused
                    ? t('plugins.state.paused')
                    : plugin.active
                      ? t('plugins.state.active')
                      : disabledByCore
                        ? t('plugins.state.disabledByCore', { date: dates.format(new Date(disabledByCore)) })
                        : t('plugins.state.inactive')}
                </p>
              </div>
              <div className="plugin-list-actions">
                {plugin.paused && (
                  <button type="button" className="button-primary" onClick={() => void handleActivate(plugin)}>
                    {t('plugins.review')}{' '}
                    <span className="visually-hidden">{name}</span>
                  </button>
                )}
                {activated ? (
                  <button type="button" onClick={() => void handleDeactivate(plugin)}>
                    {t('plugins.deactivate')}{' '}
                    <span className="visually-hidden">{name}</span>
                  </button>
                ) : (
                  <button type="button" className="button-primary" onClick={() => void handleActivate(plugin)}>
                    {t('plugins.activate')}{' '}
                    <span className="visually-hidden">{name}</span>
                  </button>
                )}
              </div>
              {plugin.active && plugin.layout && (
                <PluginLayoutForm
                  key={`${plugin.layout.position} ${plugin.layout.hidden.join(' ')}`}
                  plugin={plugin}
                  onSaved={() => {
                    setMessage({ kind: 'layoutSaved', name })
                    load().catch((caught: unknown) => setError(caught))
                  }}
                />
              )}
            </li>
          )
        })}
      </ul>
      <ErrorAlert error={error} />
      {consent && <ConsentDialog plugin={consent} onCancel={handleCancel} onAccepted={() => handleAccepted(consent)} />}
    </section>
  )
}
