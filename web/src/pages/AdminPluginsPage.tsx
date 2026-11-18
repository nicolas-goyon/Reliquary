import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Extension } from '../api/extensions'
import { useAuth } from '../auth/session'
import { ErrorAlert, SuccessAlert } from '../components/Forms'
import { navigate } from '../router/navigation'

/** A plugin of the catalog whose files the instance has. */
interface CatalogPlugin {
  id: string
  name: string
  description: string | null
  /** The last version that the catalog did not revoke, or null. */
  latestVersion: string | null
  versions: { version: string; revocationReason: string | null }[]
}

/** The response of `GET /admin/plugin-catalog`. */
interface PluginCatalog {
  configured: boolean
  serial: number | null
  expiresAt: string | null
  checkedAt: string | null
  plugins: CatalogPlugin[]
}

/** An installed extension, from `GET /admin/extensions`. */
interface InstalledExtension {
  extension: Extension
  revocationReason: string | null
}

/** One row of the page: a plugin of the catalog, an installed plugin, or the two. */
interface Row {
  id: string
  name: string
  description: string | null
  latestVersion: string | null
  installed: InstalledExtension | null
}

function rows(catalog: PluginCatalog, installed: InstalledExtension[]): Row[] {
  const plugins = installed.filter((item) => item.extension.kind === 'plugin')
  const byId = new Map(plugins.map((item) => [item.extension.id, item]))
  const fromCatalog = catalog.plugins.map((plugin) => ({ ...plugin, installed: byId.get(plugin.id) ?? null }))
  // A plugin that an administrator installed from a manifest is not in the catalog.
  const others = plugins
    .filter((item) => !catalog.plugins.some((plugin) => plugin.id === item.extension.id))
    .map((item) => ({ id: item.extension.id, name: item.extension.name, description: null, latestVersion: null, installed: item }))
  return [...fromCatalog, ...others].sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id))
}

/**
 * The plugin page of the administrators: the plugins of the catalog and the installed plugins.
 * The administrator syncs the catalog, installs a plugin version, and enables or disables a plugin for the instance. See issue #300.
 */
export function AdminPluginsPage() {
  const { t, i18n } = useTranslation()
  const { state, request } = useAuth()
  const [catalog, setCatalog] = useState<PluginCatalog | null>(null)
  const [installed, setInstalled] = useState<InstalledExtension[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const isAdministrator = state.status === 'authenticated' && state.account.isAdministrator

  const load = useCallback(
    () =>
      Promise.all([request<PluginCatalog>('/admin/plugin-catalog'), request<InstalledExtension[]>('/admin/extensions')]).then(([nextCatalog, nextInstalled]) => {
        setCatalog(nextCatalog)
        setInstalled(nextInstalled)
      }),
    [request],
  )

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent('/admin/plugins')}`, { replace: true })
    }
  }, [state.status])

  useEffect(() => {
    if (isAdministrator) {
      load().catch((caught: unknown) => setError(caught))
    }
  }, [isAdministrator, load])

  if (state.status !== 'authenticated') {
    return null
  }

  if (!isAdministrator) {
    return (
      <section className="page">
        <h1>{t('adminPlugins.title')}</h1>
        <p className="alert alert-error" role="alert">
          {t('adminPlugins.forbidden')}
        </p>
      </section>
    )
  }

  /** Runs an action of the administrator, shows its message, then reads the lists again. */
  async function act(action: () => Promise<string>) {
    setError(null)
    setMessage(null)
    setBusy(true)
    try {
      const text = await action()
      await load()
      setMessage(text)
    } catch (caught) {
      setError(caught)
      // The lists can be different after an action that failed, for example after the first step of two steps.
      await load().catch(() => undefined)
    } finally {
      setBusy(false)
    }
  }

  const extensionPath = (id: string) => `/admin/extensions/${encodeURIComponent(id)}`
  const installPath = (id: string, version: string) => `/admin/plugin-catalog/${encodeURIComponent(id)}/${encodeURIComponent(version)}/install`

  const sync = () =>
    act(async () => {
      const result = await request<{ serial: number; addedVersions: string[]; revokedVersions: string[] }>('/admin/plugin-catalog/sync', { method: 'POST' })
      return t('adminPlugins.synced', { serial: result.serial, added: result.addedVersions.length, revoked: result.revokedVersions.length })
    })
  const install = (row: Row, version: string, enable: boolean) =>
    act(async () => {
      await request(installPath(row.id, version), { method: 'POST' })
      if (enable) {
        await request(extensionPath(row.id), { method: 'PATCH', body: { enabled: true } })
      }
      return t(enable ? 'adminPlugins.enabled' : 'adminPlugins.updated', { name: row.name, version })
    })
  const setEnabled = (row: Row, enabled: boolean) =>
    act(async () => {
      await request(extensionPath(row.id), { method: 'PATCH', body: { enabled } })
      return t(enabled ? 'adminPlugins.enabled' : 'adminPlugins.disabled', { name: row.name, version: row.installed?.extension.version })
    })
  const uninstall = (row: Row) => {
    if (!window.confirm(t('adminPlugins.uninstallConfirm', { name: row.name }))) {
      return
    }
    void act(async () => {
      await request(extensionPath(row.id), { method: 'DELETE' })
      return t('adminPlugins.uninstalled', { name: row.name })
    })
  }

  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium', timeStyle: 'short' })
  const list = catalog ? rows(catalog, installed) : []

  function stateText(row: Row): string {
    const item = row.installed
    if (!item) {
      return t('adminPlugins.state.notInstalled')
    }
    if (item.revocationReason) {
      return t('adminPlugins.state.revoked', { version: item.extension.version, reason: item.revocationReason })
    }
    if (!item.extension.compatible) {
      return t('adminPlugins.state.notCompatible', { version: item.extension.version })
    }
    return t(item.extension.enabled ? 'adminPlugins.state.enabled' : 'adminPlugins.state.disabled', { version: item.extension.version })
  }

  return (
    <section className="page">
      <h1>{t('adminPlugins.title')}</h1>
      <p>{t('adminPlugins.description')}</p>

      {catalog && (
        <div className="panel">
          <h2>{t('adminPlugins.catalog.title')}</h2>
          {!catalog.configured && <p>{t('adminPlugins.catalog.notConfigured')}</p>}
          {catalog.configured && catalog.serial === null && <p>{t('adminPlugins.catalog.neverSynced')}</p>}
          {catalog.serial !== null && catalog.checkedAt && catalog.expiresAt && (
            <p>
              {t('adminPlugins.catalog.state', {
                serial: catalog.serial,
                checked: dates.format(new Date(catalog.checkedAt)),
                expires: dates.format(new Date(catalog.expiresAt)),
              })}
            </p>
          )}
          {catalog.configured && (
            <button type="button" onClick={() => void sync()} disabled={busy}>
              {t('adminPlugins.catalog.sync')}
            </button>
          )}
        </div>
      )}

      {message && <SuccessAlert>{message}</SuccessAlert>}
      <ErrorAlert error={error} />

      {catalog && list.length === 0 && <p>{t('adminPlugins.empty')}</p>}
      {list.length > 0 && (
        <div className="table-scroll">
          <table className="table">
            <caption className="visually-hidden">{t('adminPlugins.title')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('adminPlugins.columns.plugin')}</th>
                <th scope="col">{t('adminPlugins.columns.state')}</th>
                <th scope="col">{t('adminPlugins.columns.latest')}</th>
                <th scope="col">{t('adminPlugins.columns.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {list.map((row) => {
                const item = row.installed
                const usable = item !== null && !item.revocationReason && item.extension.compatible
                const newer = row.latestVersion !== null && item !== null && item.extension.version !== row.latestVersion ? row.latestVersion : null
                return (
                  <tr key={row.id}>
                    <th scope="row">
                      {row.name} <span className="field-hint">{row.id}</span>
                      {row.description && <p className="field-hint">{row.description}</p>}
                    </th>
                    <td>{stateText(row)}</td>
                    <td>{row.latestVersion ?? t('adminPlugins.noVersion')}</td>
                    <td>
                      <div className="plugin-list-actions">
                        {!item && row.latestVersion && (
                          <button type="button" className="button-primary" disabled={busy} onClick={() => void install(row, row.latestVersion!, true)}>
                            {t('adminPlugins.actions.installAndEnable')} <span className="visually-hidden">{row.name}</span>
                          </button>
                        )}
                        {newer && (
                          <button type="button" disabled={busy} onClick={() => void install(row, newer, false)}>
                            {t('adminPlugins.actions.update', { version: newer })} <span className="visually-hidden">{row.name}</span>
                          </button>
                        )}
                        {usable && !item.extension.enabled && (
                          <button type="button" className="button-primary" disabled={busy} onClick={() => void setEnabled(row, true)}>
                            {t('adminPlugins.actions.enable')} <span className="visually-hidden">{row.name}</span>
                          </button>
                        )}
                        {item?.extension.enabled && (
                          <button type="button" disabled={busy} onClick={() => void setEnabled(row, false)}>
                            {t('adminPlugins.actions.disable')} <span className="visually-hidden">{row.name}</span>
                          </button>
                        )}
                        {item && (
                          <button type="button" disabled={busy} onClick={() => uninstall(row)}>
                            {t('adminPlugins.actions.uninstall')} <span className="visually-hidden">{row.name}</span>
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
