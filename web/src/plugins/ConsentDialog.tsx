import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { Extension } from '../api/extensions'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { Dialog } from '../components/versions/Dialog'
import { askedAccess, consentBody, isEmptyAccess, missingAccess, networkTarget, pluginName, type PluginAccess } from './consent'

interface ConsentDialogProps {
  plugin: Extension
  /** Closes the dialog with no change. */
  onCancel(): void
  /** Called after the API activated the plugin with the consent. */
  onAccepted(): void
}

/**
 * The consent dialog of a plugin. The user reads the access of the plugin in plain words, then accepts it or cancels.
 * For a new version of a plugin that the user accepted before, the dialog shows only the new access.
 * See issue #287.
 */
export function ConsentDialog({ plugin, onCancel, onAccepted }: ConsentDialogProps) {
  const { t, i18n } = useTranslation('account')
  const { request } = useAuth()
  const id = useId()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const name = pluginName(plugin, i18n.resolvedLanguage)
  // After a consent for an earlier version, the dialog shows only the new access.
  const missing = missingAccess(plugin)
  const update = plugin.consent !== null && !isEmptyAccess(missing)
  const access = update ? missing : askedAccess(plugin)

  async function handleAccept() {
    setBusy(true)
    setError(null)
    try {
      await request(`/me/extensions/${encodeURIComponent(plugin.id)}`, { method: 'PUT', body: consentBody(plugin) })
      onAccepted()
    } catch (caught) {
      setError(caught)
      setBusy(false)
    }
  }

  return (
    <Dialog title={update ? t('plugins.consent.updateTitle', { name }) : t('plugins.consent.title', { name })} onClose={onCancel}>
      <p id={`${id}-intro`}>
        {update
          ? t('plugins.consent.updateIntro', { name, accepted: plugin.consent?.version, version: plugin.version })
          : t('plugins.consent.intro', { name, version: plugin.version })}
      </p>
      <AccessList access={access} />
      <ErrorAlert error={error} />
      <div className="form-actions">
        <button type="button" className="button-primary" disabled={busy} onClick={() => void handleAccept()} aria-describedby={`${id}-intro`}>
          {update ? t('plugins.consent.acceptUpdate') : t('plugins.consent.accept')}
        </button>
        <button type="button" onClick={onCancel} data-autofocus>
          {t('plugins.consent.cancel')}
        </button>
      </div>
    </Dialog>
  )
}

/** The access of a plugin in plain words, in four groups. A group with no access does not show. */
function AccessList({ access }: { access: PluginAccess }) {
  const { t } = useTranslation('account')
  if (isEmptyAccess(access)) {
    return <p>{t('plugins.consent.nothing')}</p>
  }
  return (
    <div className="plugin-consent">
      {access.scopes.length > 0 && (
        <AccessGroup title={t('plugins.consent.scopes')}>
          {access.scopes.map((scope) => (
            <li key={scope}>{scopeText(scope, t)}</li>
          ))}
        </AccessGroup>
      )}
      {access.network.length > 0 && (
        <AccessGroup title={t('plugins.consent.network')} hint={t('plugins.consent.networkHint')}>
          {access.network.map((origin) => (
            <li key={origin}>
              {networkText(origin, t)} <code className="plugin-consent-origin">{origin}</code>
            </li>
          ))}
        </AccessGroup>
      )}
      {access.devices.length > 0 && (
        <AccessGroup title={t('plugins.consent.devices')}>
          {access.devices.map((device) => (
            <li key={device}>{deviceText(device, t)}</li>
          ))}
        </AccessGroup>
      )}
      {access.sites.length > 0 && (
        <AccessGroup title={t('plugins.consent.sites')}>
          {access.sites.map((site) => (
            <li key={site}>{t('plugins.consent.site', { site })}</li>
          ))}
        </AccessGroup>
      )}
    </div>
  )
}

function AccessGroup({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  const id = useId()
  return (
    <section aria-labelledby={id}>
      <h3 id={id}>{title}</h3>
      {hint && <p className="field-hint">{hint}</p>}
      <ul>{children}</ul>
    </section>
  )
}

type Translate = ReturnType<typeof useTranslation<'account'>>['t']

/** Gets the plain words of a scope. The text of a delegation scope has the same meaning. */
function scopeText(scope: string, t: Translate): string {
  switch (scope) {
    case 'decks:read':
      return t('delegations.scope.decks_read')
    case 'decks:write':
      return t('delegations.scope.decks_write')
    case 'collection:read':
      return t('delegations.scope.collection_read')
    case 'collection:write':
      return t('delegations.scope.collection_write')
    case 'profile:read':
      return t('delegations.scope.profile_read')
    default:
      return scope
  }
}

function networkText(origin: string, t: Translate): string {
  const target = networkTarget(origin)
  switch (target.kind) {
    case 'computer':
      return target.port ? t('plugins.consent.computerPort', { port: target.port }) : t('plugins.consent.computer')
    case 'subdomains':
      return t('plugins.consent.subdomains', { host: target.host })
    default:
      return t('plugins.consent.siteHost', { host: target.host })
  }
}

function deviceText(device: string, t: Translate): string {
  switch (device) {
    case 'camera':
      return t('plugins.consent.camera')
    case 'loopback-network':
      return t('plugins.consent.loopbackNetwork')
    default:
      return device
  }
}
