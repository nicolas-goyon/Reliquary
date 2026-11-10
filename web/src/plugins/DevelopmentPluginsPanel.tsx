import { useEffect, useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { ExtensionList } from '../api/extensions'
import { useAuth } from '../auth/session'
import { SuccessAlert } from '../components/Forms'
import { DevelopmentManifestError, developmentPluginUrls, isHttpUrl, loadDevelopmentManifest, setDevelopmentPluginUrls, type DevelopmentManifestErrorCode } from './development'
import { NotCheckedBanner } from './NotCheckedBanner'

/**
 * The development plugins of the account page: the user loads a plugin from the URL of its plugin manifest.
 * The panel shows only on an instance that allows development plugins (setting `Plugins:AllowDevelopmentPlugins`).
 */
export function DevelopmentPluginsPanel() {
  const { t } = useTranslation('account')
  const { request } = useAuth()
  const id = useId()
  const [allowed, setAllowed] = useState(false)
  const [urls, setUrls] = useState(developmentPluginUrls)
  const [url, setUrl] = useState('')
  const [error, setError] = useState<DevelopmentManifestErrorCode | 'duplicate' | null>(null)
  const [loaded, setLoaded] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let cancelled = false
    request<ExtensionList>('/extensions')
      .then((list) => {
        if (!cancelled) {
          setAllowed(list?.plugins?.developmentPlugins === true)
        }
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [request])

  if (!allowed) {
    return null
  }

  async function handleAdd(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setLoaded(null)
    const value = url.trim()
    if (!isHttpUrl(value)) {
      setError('invalidUrl')
      return
    }
    if (urls.includes(value)) {
      setError('duplicate')
      return
    }
    setBusy(true)
    try {
      const plugin = await loadDevelopmentManifest(value)
      const next = [...urls, value]
      setDevelopmentPluginUrls(next)
      setUrls(next)
      setUrl('')
      setLoaded(plugin.name)
    } catch (caught) {
      setError(caught instanceof DevelopmentManifestError ? caught.code : 'notAvailable')
    } finally {
      setBusy(false)
    }
  }

  function handleRemove(item: string) {
    const next = urls.filter((value) => value !== item)
    setDevelopmentPluginUrls(next)
    setUrls(next)
    setLoaded(null)
  }

  return (
    <section className="panel" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t('developmentPlugins.title')}</h2>
      <p>{t('developmentPlugins.description')}</p>
      {loaded && <SuccessAlert>{t('developmentPlugins.loaded', { name: loaded })}</SuccessAlert>}
      {urls.length === 0 ? (
        <p>{t('developmentPlugins.none')}</p>
      ) : (
        <ul className="development-plugins">
          {urls.map((item) => (
            <li key={item}>
              <NotCheckedBanner name={item} />
              <code>{item}</code>{' '}
              <button type="button" className="button-danger" onClick={() => handleRemove(item)}>
                {t('developmentPlugins.remove')}{' '}
                <span className="visually-hidden">{item}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      <form className="form" onSubmit={(event) => void handleAdd(event)} noValidate>
        <div className="field">
          <label htmlFor={`${id}-url`}>{t('developmentPlugins.url')}</label>
          <input
            id={`${id}-url`}
            type="url"
            inputMode="url"
            placeholder="http://localhost:5173/manifest.json"
            value={url}
            onChange={(event) => setUrl(event.target.value)}
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? `${id}-error` : undefined}
          />
          {error && (
            <p className="field-error" id={`${id}-error`}>
              {t(`developmentPlugins.errors.${error}`)}
            </p>
          )}
        </div>
        <button type="submit" className="button-primary" disabled={busy}>
          {t('developmentPlugins.add')}
        </button>
      </form>
    </section>
  )
}
