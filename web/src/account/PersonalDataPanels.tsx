import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/session'
import { ErrorAlert, TextField } from '../components/Forms'

interface DataExport {
  id: string
  status: 'pending' | 'ready' | 'failed'
  requestedAt: string
  completedAt: string | null
  expiresAt: string | null
  sizeBytes: number | null
  downloadPath: string | null
}

interface Deletion {
  requestedAt: string | null
  deleteAfter: string | null
}

/** The export of all the data of the account, as a ZIP archive that the worker makes. */
export function DataExportPanel() {
  const { t, i18n } = useTranslation('account')
  const { request } = useAuth()
  const [exports, setExports] = useState<DataExport[]>([])
  const [error, setError] = useState<unknown>(null)

  const load = useCallback(() => request<DataExport[]>('/me/exports').then(setExports), [request])

  useEffect(() => {
    load().catch((caught: unknown) => setError(caught))
  }, [load])

  async function requestExport() {
    setError(null)
    try {
      await request('/me/exports', { method: 'POST' })
      await load()
    } catch (caught) {
      setError(caught)
    }
  }

  async function download(item: DataExport) {
    setError(null)
    try {
      const archive = await request<Blob>(`/me/exports/${item.id}/archive`, { responseType: 'blob' })
      const url = URL.createObjectURL(archive)
      const link = document.createElement('a')
      link.href = url
      link.download = 'reliquary-export.zip'
      link.click()
      URL.revokeObjectURL(url)
    } catch (caught) {
      setError(caught)
    }
  }

  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium', timeStyle: 'short' })
  const pending = exports.some((item) => item.status === 'pending')
  return (
    <section className="panel" aria-labelledby="data-export-title">
      <h2 id="data-export-title">{t('dataExport.title')}</h2>
      <p>{t('dataExport.description')}</p>
      <button type="button" className="button-primary" disabled={pending} onClick={() => void requestExport()}>
        {t('dataExport.request')}
      </button>
      {pending && (
        <p role="status" className="field-hint">
          {t('dataExport.pending')}
        </p>
      )}
      {exports.length > 0 && (
        <ul>
          {exports.map((item) => (
            <li key={item.id}>
              {dates.format(new Date(item.requestedAt))} · {t(`dataExport.status.${item.status}`)}
              {item.status === 'ready' && item.expiresAt && (
                <>
                  {' · '}
                  <button type="button" className="button-link" onClick={() => void download(item)}>
                    {t('dataExport.download')}
                  </button>{' '}
                  {t('dataExport.expires', { date: dates.format(new Date(item.expiresAt)) })}
                </>
              )}
            </li>
          ))}
        </ul>
      )}
      <ErrorAlert error={error} />
    </section>
  )
}

/** The deletion of the account, with a delay of 7 days and a cancellation. */
export function AccountDeletionPanel() {
  const { t, i18n } = useTranslation('account')
  const { request } = useAuth()
  const [deletion, setDeletion] = useState<Deletion | null>(null)
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    request<Deletion>('/me/deletion')
      .then(setDeletion)
      .catch((caught: unknown) => setError(caught))
  }, [request])

  async function handleDelete(event: FormEvent) {
    event.preventDefault()
    if (!window.confirm(t('deletion.confirm'))) {
      return
    }
    setError(null)
    try {
      setDeletion(await request<Deletion>('/me/deletion', { method: 'POST', body: { password } }))
      setPassword('')
    } catch (caught) {
      setError(caught)
    }
  }

  async function cancel() {
    setError(null)
    try {
      await request('/me/deletion', { method: 'DELETE' })
      setDeletion({ requestedAt: null, deleteAfter: null })
    } catch (caught) {
      setError(caught)
    }
  }

  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'long', timeStyle: 'short' })
  return (
    <section className="panel" aria-labelledby="deletion-title">
      <h2 id="deletion-title">{t('deletion.title')}</h2>
      {deletion?.deleteAfter ? (
        <div className="alert alert-error" role="status">
          <p>{t('deletion.scheduled', { date: dates.format(new Date(deletion.deleteAfter)) })}</p>
          <button type="button" className="button-primary" onClick={() => void cancel()}>
            {t('deletion.cancel')}
          </button>
        </div>
      ) : (
        <form className="form" onSubmit={(event) => void handleDelete(event)} noValidate>
          <p>{t('deletion.description')}</p>
          <TextField label={t('deletion.password')} field="password" error={error} type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} />
          <button type="submit" className="button-danger">
            {t('deletion.submit')}
          </button>
        </form>
      )}
      <ErrorAlert error={error} />
    </section>
  )
}
