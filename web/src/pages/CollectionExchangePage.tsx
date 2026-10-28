import { useEffect, useId, useState, type ChangeEvent, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import {
  addOrigins,
  detectCollectionFormat,
  exchangeFormats,
  exportPath,
  flattenLocations,
  importLineKey,
  locationLabel,
  type CollectionLocation,
  type ExchangeFormat,
  type ImportLine,
  type ImportResult,
} from '../api/collection'
import type { ExportedFile } from '../api/exchange'
import { useAuth } from '../auth/session'
import { ErrorAlert, SuccessAlert } from '../components/Forms'
import { downloadFile } from '../proxies/pdf'
import { Link } from '../router/Link'
import { navigate, useLocation } from '../router/navigation'

/** The import of a file into a location, and the export of a location or of all the collection. */
export function CollectionExchangePage() {
  const { t } = useTranslation('collection')
  const { state, request } = useAuth()
  const url = useLocation()
  const initialLocation = url.searchParams.get('locationId') ?? ''
  const [locations, setLocations] = useState<CollectionLocation[] | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent('/collection/exchange')}`, { replace: true })
      return
    }
    if (state.status !== 'authenticated') {
      return
    }
    request<CollectionLocation[]>('/collection/locations')
      .then(setLocations)
      .catch((caught: unknown) => setError(caught))
  }, [state.status, request])

  if (state.status !== 'authenticated') {
    return null
  }

  return (
    <section className="page">
      <p>
        <Link to="/collection">{t('view.title')}</Link> · <Link to="/collection/locations">{t('view.locationsLink')}</Link>
      </p>
      <h1>{t('exchange.title')}</h1>
      <ErrorAlert error={error} />
      {locations === null ? (
        !error && <p role="status">{t('exchange.loading')}</p>
      ) : (
        <>
          <ImportForm locations={locations} initialLocation={initialLocation} />
          <ExportForm locations={locations} initialLocation={initialLocation} />
        </>
      )}
    </section>
  )
}

/** The form of the import: the file, its format, the location and the origin of the cards. */
function ImportForm({ locations, initialLocation }: { locations: CollectionLocation[]; initialLocation: string }) {
  const { t } = useTranslation('collection')
  const { request } = useAuth()
  const id = useId()
  const targets = flattenLocations(locations).filter(({ location }) => location.kind !== 'deck')
  const [content, setContent] = useState('')
  const [format, setFormat] = useState<ExchangeFormat>('text')
  const [locationId, setLocationId] = useState(() => targets.find(({ location }) => location.id === initialLocation)?.location.id ?? targets[0]?.location.id ?? '')
  const [origin, setOrigin] = useState<(typeof addOrigins)[number]>('nowhere')
  const [currency, setCurrency] = useState('USD')
  const [result, setResult] = useState<ImportResult | null>(null)
  const [error, setError] = useState<unknown>(null)

  async function handleFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) {
      return
    }
    const text = await file.text()
    setContent(text)
    setFormat(detectCollectionFormat(file.name, text))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setResult(null)
    try {
      setResult(
        await request<ImportResult>(`/collection/locations/${locationId}/import`, {
          method: 'POST',
          body: { format, content, origin, currency: currency.trim().toUpperCase() },
        }),
      )
    } catch (caught) {
      setError(caught)
    }
  }

  const target = targets.find(({ location }) => location.id === locationId)
  return (
    <form className="form panel" onSubmit={(event) => void handleSubmit(event)} noValidate aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t('exchange.importTitle')}</h2>
      {targets.length === 0 ? (
        <p>{t('exchange.noLocation')}</p>
      ) : (
        <>
          <div className="field">
            <label htmlFor={`${id}-file`}>{t('exchange.file')}</label>
            <input id={`${id}-file`} type="file" accept=".txt,.dek,.csv" onChange={(event) => void handleFile(event)} />
            <p className="field-hint">{t('exchange.fileHint')}</p>
          </div>
          <div className="field">
            <label htmlFor={`${id}-content`}>{t('exchange.content')}</label>
            <textarea id={`${id}-content`} className="import-text" rows={10} spellCheck={false} value={content} onChange={(event) => setContent(event.target.value)} />
          </div>
          <div className="field">
            <label htmlFor={`${id}-format`}>{t('exchange.format')}</label>
            <select id={`${id}-format`} value={format} onChange={(event) => setFormat(event.target.value as ExchangeFormat)}>
              {exchangeFormats.map((known) => (
                <option key={known} value={known}>
                  {t(`exchange.formats.${known}`)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor={`${id}-location`}>{t('exchange.importLocation')}</label>
            <select id={`${id}-location`} value={locationId} onChange={(event) => setLocationId(event.target.value)}>
              {targets.map(({ location, parent }) => (
                <option key={location.id} value={location.id}>
                  {parent ? locationLabel(parent.name, location.name) : location.name}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor={`${id}-origin`}>{t('fields.origin')}</label>
            <select id={`${id}-origin`} value={origin} onChange={(event) => setOrigin(event.target.value as (typeof addOrigins)[number])}>
              {addOrigins.map((known) => (
                <option key={known} value={known}>
                  {t(`endpoints.${known}`)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor={`${id}-currency`}>{t('fields.purchaseCurrency')}</label>
            <input id={`${id}-currency`} maxLength={3} value={currency} onChange={(event) => setCurrency(event.target.value)} aria-describedby={`${id}-currency-hint`} />
            <p className="field-hint" id={`${id}-currency-hint`}>
              {t('exchange.currencyHint')}
            </p>
          </div>
          <ErrorAlert error={error} />
          {result && (
            <>
              <SuccessAlert>{t('exchange.imported', { count: result.cardCount, location: target?.parent ? locationLabel(target.parent.name, target.location.name) : (target?.location.name ?? '') })}</SuccessAlert>
              <LineList kind="error" title={t('exchange.errors')} lines={result.errors} />
              <LineList kind="warning" title={t('exchange.warnings')} lines={result.warnings} />
            </>
          )}
          <div className="form-actions">
            <button type="submit" className="button-primary" disabled={content.trim().length === 0 || !locationId}>
              {t('exchange.importSubmit')}
            </button>
          </div>
        </>
      )}
    </form>
  )
}

/** The form of the export: the location or all the collection, the sections and the format. */
function ExportForm({ locations, initialLocation }: { locations: CollectionLocation[]; initialLocation: string }) {
  const { t } = useTranslation('collection')
  const { request } = useAuth()
  const id = useId()
  const all = flattenLocations(locations)
  const [locationId, setLocationId] = useState(() => all.find(({ location }) => location.id === initialLocation)?.location.id ?? '')
  const [sections, setSections] = useState(true)
  const [format, setFormat] = useState<ExchangeFormat>('csv')
  const [error, setError] = useState<unknown>(null)
  const selected = all.find(({ location }) => location.id === locationId)
  const hasSections = selected !== undefined && selected.location.sections.length > 0

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      const file = await request<ExportedFile>(exportPath(format, locationId, hasSections && sections))
      downloadFile(new Blob([file.content], { type: file.contentType }), file.fileName)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <form className="form panel" onSubmit={(event) => void handleSubmit(event)} noValidate aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t('exchange.exportTitle')}</h2>
      <div className="field">
        <label htmlFor={`${id}-location`}>{t('exchange.exportLocation')}</label>
        <select id={`${id}-location`} value={locationId} onChange={(event) => setLocationId(event.target.value)}>
          <option value="">{t('exchange.allCollection')}</option>
          {all.map(({ location, parent }) => (
            <option key={location.id} value={location.id}>
              {parent ? locationLabel(parent.name, location.name) : location.name}
            </option>
          ))}
        </select>
      </div>
      {hasSections && (
        <label className="filter-check">
          <input type="checkbox" checked={sections} onChange={(event) => setSections(event.target.checked)} />
          {t('exchange.includeSections')}
        </label>
      )}
      <div className="field">
        <label htmlFor={`${id}-format`}>{t('exchange.format')}</label>
        <select id={`${id}-format`} value={format} onChange={(event) => setFormat(event.target.value as ExchangeFormat)}>
          {exchangeFormats.map((known) => (
            <option key={known} value={known}>
              {t(`exchange.formats.${known}`)}
            </option>
          ))}
        </select>
        <p className="field-hint">{t(`exchange.exportHints.${format}`)}</p>
      </div>
      <ErrorAlert error={error} />
      <div className="form-actions">
        <button type="submit" className="button-primary">
          {t('exchange.exportSubmit')}
        </button>
      </div>
    </form>
  )
}

/** The lines of an import that the API ignored or changed, with their numbers. */
function LineList({ title, lines, kind }: { title: string; lines: ImportLine[]; kind: 'error' | 'warning' }) {
  const { t } = useTranslation('collection')
  if (lines.length === 0) {
    return null
  }
  return (
    <div className={kind === 'error' ? 'alert alert-error' : 'alert alert-warning'} role={kind === 'error' ? 'alert' : 'status'}>
      <p>{title}</p>
      <ul>
        {lines.map((line) => (
          <li key={`${line.line}-${line.code}`}>{t(`exchange.lineCodes.${importLineKey(line.code)}`, { line: line.line, text: line.text.trim() })}</li>
        ))}
      </ul>
    </div>
  )
}
