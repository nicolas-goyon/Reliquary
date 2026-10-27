import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { fileFormats, type ExportedFile, type FileFormat } from '../api/exchange'
import { useAuth } from '../auth/session'
import { ErrorAlert } from './Forms'

/** The dialog of the export of a deck: the file format, then a download of the file. */
export function DeckExportDialog({ deckId, onClose }: { deckId: string; onClose(): void }) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const id = useId()
  const [format, setFormat] = useState<FileFormat>('moxfield')
  const [error, setError] = useState<unknown>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      const file = await request<ExportedFile>(`/decks/${deckId}/export?format=${format}`)
      const url = URL.createObjectURL(new Blob([file.content], { type: file.contentType }))
      const link = document.createElement('a')
      link.href = url
      link.download = file.fileName
      link.click()
      URL.revokeObjectURL(url)
      onClose()
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <div className="dialog" role="dialog" aria-labelledby={`${id}-title`}>
      <form className="form panel" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <h2 id={`${id}-title`}>{t('export.title')}</h2>
        <div className="field">
          <label htmlFor={`${id}-format`}>{t('import.fileFormat')}</label>
          <select id={`${id}-format`} value={format} onChange={(event) => setFormat(event.target.value as FileFormat)}>
            {fileFormats.map((known) => (
              <option key={known} value={known}>
                {t(`fileFormats.${known}`)}
              </option>
            ))}
          </select>
          <p className="field-hint">{t(`export.hint.${format}`)}</p>
        </div>
        <ErrorAlert error={error} />
        <div className="form-actions">
          <button type="submit" className="button-primary">
            {t('export.submit')}
          </button>
          <button type="button" onClick={onClose}>
            {t('actions.cancel')}
          </button>
        </div>
      </form>
    </div>
  )
}
