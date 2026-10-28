import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { advancedFields, type AdvancedField, type CollectionSettings } from '../../api/collection'
import { useAuth } from '../../auth/session'
import { ErrorAlert, SuccessAlert } from '../Forms'

/** The settings of the advanced collection: the optional fields of the entries. */
export function AdvancedFieldsPanel({ settings, onSaved }: { settings: CollectionSettings; onSaved(settings: CollectionSettings): void }) {
  const { t } = useTranslation('collection')
  const { request } = useAuth()
  const [enabled, setEnabled] = useState<AdvancedField[]>(settings.advancedFields)
  const [error, setError] = useState<unknown>(null)
  const [saved, setSaved] = useState(false)

  async function save(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setSaved(false)
    try {
      const result = await request<CollectionSettings>('/collection/settings', { method: 'PUT', body: { advancedFields: enabled } })
      setSaved(true)
      onSaved(result)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <details className="panel">
      <summary>{t('settings.title')}</summary>
      <form className="form" onSubmit={(event) => void save(event)}>
        <p className="field-hint">{t('settings.description')}</p>
        <fieldset className="filter-options">
          <legend className="visually-hidden">{t('settings.title')}</legend>
          {advancedFields.map((field) => (
            <label key={field} className="filter-check">
              <input
                type="checkbox"
                checked={enabled.includes(field)}
                onChange={() => setEnabled(enabled.includes(field) ? enabled.filter((value) => value !== field) : [...enabled, field])}
              />
              {t(`fields.${field}`)}
            </label>
          ))}
        </fieldset>
        <ErrorAlert error={error} />
        {saved && <SuccessAlert>{t('settings.saved')}</SuccessAlert>}
        <div className="form-actions">
          <button type="submit" className="button-primary">
            {t('settings.save')}
          </button>
        </div>
      </form>
    </details>
  )
}
