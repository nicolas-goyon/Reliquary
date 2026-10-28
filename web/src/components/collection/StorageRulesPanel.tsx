import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { flattenLocations, locationLabel, type CollectionLocation, type StorageRule } from '../../api/collection'
import { useAuth } from '../../auth/session'
import { ErrorAlert, TextField } from '../Forms'

/** The storage rules of the user: a card search, a location and a priority. */
export function StorageRulesPanel({ locations }: { locations: CollectionLocation[] }) {
  const { t } = useTranslation('collection')
  const { request } = useAuth()
  const [rules, setRules] = useState<StorageRule[]>([])
  const [query, setQuery] = useState('')
  const [priority, setPriority] = useState('0')
  const [locationId, setLocationId] = useState('')
  const [error, setError] = useState<unknown>(null)
  const targets = flattenLocations(locations).filter(({ location }) => location.kind !== 'deck')

  const load = useCallback(() => {
    request<StorageRule[]>('/collection/storage-rules')
      .then(setRules)
      .catch((caught: unknown) => setError(caught))
  }, [request])

  useEffect(() => {
    load()
  }, [load])

  async function add(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      await request('/collection/storage-rules', { method: 'POST', body: { locationId, query: query.trim(), priority: Number(priority) || 0 } })
      setQuery('')
      load()
    } catch (caught) {
      setError(caught)
    }
  }

  async function remove(rule: StorageRule) {
    setError(null)
    try {
      await request(`/collection/storage-rules/${rule.id}`, { method: 'DELETE' })
      load()
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <details className="panel">
      <summary>{t('rules.title')}</summary>
      <p className="field-hint">{t('rules.description')}</p>
      {rules.length === 0 ? (
        <p>{t('rules.empty')}</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th scope="col">{t('rules.priority')}</th>
              <th scope="col">{t('rules.query')}</th>
              <th scope="col">{t('rules.location')}</th>
              <th scope="col">{t('locations.actions')}</th>
            </tr>
          </thead>
          <tbody>
            {rules.map((rule) => (
              <tr key={rule.id}>
                <td>{rule.priority}</td>
                <td>
                  <code>{rule.query}</code>
                </td>
                <td>{locationLabel(rule.locationName, rule.sectionName)}</td>
                <td>
                  <button type="button" className="button-small button-danger" aria-label={t('rules.deleteLabel', { query: rule.query })} onClick={() => void remove(rule)}>
                    {t('rules.delete')}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <ErrorAlert error={error} />
      <form className="form-inline" aria-label={t('rules.add')} onSubmit={(event) => void add(event)}>
        <TextField label={t('rules.query')} field="query" error={error} value={query} required maxLength={500} placeholder={t('rules.queryPlaceholder')} onChange={(event) => setQuery(event.target.value)} />
        <div className="field">
          <label htmlFor="rule-location">{t('rules.location')}</label>
          <select id="rule-location" value={locationId} required onChange={(event) => setLocationId(event.target.value)}>
            <option value="" />
            {targets.map(({ location, parent }) => (
              <option key={location.id} value={location.id}>
                {parent ? locationLabel(parent.name, location.name) : location.name}
              </option>
            ))}
          </select>
        </div>
        <TextField label={t('rules.priority')} type="number" value={priority} onChange={(event) => setPriority(event.target.value)} />
        <div className="form-actions">
          <button type="submit" className="button-primary" disabled={!query.trim() || !locationId}>
            {t('rules.add')}
          </button>
        </div>
      </form>
    </details>
  )
}
