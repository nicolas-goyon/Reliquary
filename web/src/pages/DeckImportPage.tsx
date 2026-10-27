import { useEffect, useId, useState, type ChangeEvent, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Deck, DeckTextLineError, Visibility } from '../api/decks'
import { automaticHeader, csvFields, csvHeaders, deckNameFromFile, detectFormat, fileFormats, type CsvField, type FileFormat, type ImportWarning } from '../api/exchange'
import { ApiError } from '../api/client'
import { useAuth } from '../auth/session'
import { FormatSelect, VisibilitySelect } from '../components/DeckFields'
import { ErrorAlert, TextField } from '../components/Forms'
import { LineErrorList } from '../components/LineErrors'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

/** The import of a deck from a file or a pasted list of another tool. */
export function DeckImportPage() {
  const { t } = useTranslation('decks')
  const { state, request } = useAuth()
  const fileId = useId()
  const textId = useId()
  const formatId = useId()
  const [content, setContent] = useState('')
  const [fileFormat, setFileFormat] = useState<FileFormat>('moxfield')
  const [name, setName] = useState('')
  const [deckFormat, setDeckFormat] = useState('commander')
  const [visibility, setVisibility] = useState<Visibility>('private')
  const [columns, setColumns] = useState<Partial<Record<CsvField, string>>>({})
  const [lineErrors, setLineErrors] = useState<DeckTextLineError[]>([])
  const [imported, setImported] = useState<{ deck: Deck; warnings: ImportWarning[] } | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent('/decks/import')}`, { replace: true })
    }
  }, [state.status])

  if (state.status !== 'authenticated') {
    return null
  }

  async function handleFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) {
      return
    }
    const text = await file.text()
    setContent(text)
    setFileFormat(detectFormat(file.name, text))
    setColumns({})
    if (name.trim().length === 0) {
      setName(deckNameFromFile(file.name))
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setLineErrors([])
    const columnMap = Object.fromEntries(Object.entries(columns).filter(([, header]) => header))
    try {
      const result = await request<{ deck: Deck; warnings: ImportWarning[] }>('/decks/import', {
        method: 'POST',
        body: {
          format: fileFormat,
          content,
          name,
          deckFormat,
          visibility,
          ...(fileFormat === 'csv' && Object.keys(columnMap).length > 0 ? { columns: columnMap } : {}),
        },
      })
      if (result.warnings.length === 0) {
        navigate(`/decks/${result.deck.id}`)
      } else {
        setImported(result)
      }
    } catch (caught) {
      if (caught instanceof ApiError && caught.code === 'deck_text_invalid' && Array.isArray(caught.details.lineErrors)) {
        setLineErrors(caught.details.lineErrors as DeckTextLineError[])
      } else {
        setError(caught)
      }
    }
  }

  if (imported) {
    return (
      <section className="page">
        <h1>{t('import.title')}</h1>
        <LineErrorList kind="warning" title={t('import.warnings')} lines={imported.warnings} />
        <p>
          <Link to={`/decks/${imported.deck.id}`}>{t('import.open', { name: imported.deck.name })}</Link>
        </p>
      </section>
    )
  }

  const headers = fileFormat === 'csv' ? csvHeaders(content) : []
  return (
    <section className="page">
      <h1>{t('import.title')}</h1>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <div className="field">
          <label htmlFor={fileId}>{t('import.file')}</label>
          <input id={fileId} type="file" accept=".txt,.dek,.csv,.dck" onChange={(event) => void handleFile(event)} />
          <p className="field-hint">{t('import.fileHint')}</p>
        </div>
        <div className="field">
          <label htmlFor={textId}>{t('import.content')}</label>
          <textarea id={textId} className="import-text" rows={12} spellCheck={false} value={content} onChange={(event) => setContent(event.target.value)} />
        </div>
        <div className="field">
          <label htmlFor={formatId}>{t('import.fileFormat')}</label>
          <select id={formatId} value={fileFormat} onChange={(event) => setFileFormat(event.target.value as FileFormat)}>
            {fileFormats.map((format) => (
              <option key={format} value={format}>
                {t(`fileFormats.${format}`)}
              </option>
            ))}
          </select>
        </div>
        {fileFormat === 'csv' && headers.length > 0 && <ColumnMap headers={headers} columns={columns} onChange={setColumns} />}
        <TextField label={t('fields.name')} field="name" error={error} maxLength={100} value={name} onChange={(event) => setName(event.target.value)} />
        <FormatSelect value={deckFormat} onChange={setDeckFormat} />
        <VisibilitySelect value={visibility} onChange={setVisibility} />
        <LineErrorList title={t('import.errors')} lines={lineErrors} />
        <ErrorAlert error={error} />
        <div className="form-actions">
          <button type="submit" className="button-primary" disabled={content.trim().length === 0}>
            {t('import.submit')}
          </button>
          <Link to="/decks">{t('actions.cancel')}</Link>
        </div>
      </form>
    </section>
  )
}

/** The column of each field of a generic CSV file. The default is the column that the API finds from its header name. */
function ColumnMap({ headers, columns, onChange }: { headers: string[]; columns: Partial<Record<CsvField, string>>; onChange(columns: Partial<Record<CsvField, string>>): void }) {
  const { t } = useTranslation('decks')
  const id = useId()
  return (
    <fieldset className="column-map">
      <legend>{t('import.columns')}</legend>
      {csvFields.map((field) => {
        const automatic = automaticHeader(field, headers)
        return (
          <div className="field" key={field}>
            <label htmlFor={`${id}-${field}`}>{t(`csvFields.${field}`)}</label>
            <select id={`${id}-${field}`} value={columns[field] ?? ''} onChange={(event) => onChange({ ...columns, [field]: event.target.value })}>
              <option value="">{automatic ? t('import.automatic', { header: automatic }) : t('import.noColumn')}</option>
              {headers.map((header) => (
                <option key={header} value={header}>
                  {header}
                </option>
              ))}
            </select>
          </div>
        )
      })}
    </fieldset>
  )
}
