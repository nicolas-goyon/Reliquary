import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Page } from '../api/cards'
import { cardViewQuery, flattenLocations, languages, locationLabel, type CollectionCard, type CollectionLocation, type CollectionSettings } from '../api/collection'
import { useAuth } from '../auth/session'
import { AdvancedFieldsPanel } from '../components/collection/AdvancedFieldsPanel'
import { ErrorAlert } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

type Filters = { q: string; locationId: string; language: string; set: string }

const noFilters: Filters = { q: '', locationId: '', language: '', set: '' }

/** The global view of the collection: all the cards grouped by Oracle card, with the locations of the copies. */
export function CollectionPage() {
  const { t } = useTranslation('collection')
  const { state, request } = useAuth()
  const [draft, setDraft] = useState<Filters>(noFilters)
  const [filters, setFilters] = useState<Filters>(noFilters)
  const [cards, setCards] = useState<CollectionCard[] | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [locations, setLocations] = useState<CollectionLocation[]>([])
  const [settings, setSettings] = useState<CollectionSettings | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent('/collection')}`, { replace: true })
      return
    }
    if (state.status !== 'authenticated') {
      return
    }
    Promise.all([request<CollectionLocation[]>('/collection/locations'), request<CollectionSettings>('/collection/settings')])
      .then(([locationList, current]) => {
        setLocations(locationList)
        setSettings(current)
      })
      .catch((caught: unknown) => setError(caught))
  }, [state.status, request])

  useEffect(() => {
    if (state.status !== 'authenticated') {
      return
    }
    request<Page<CollectionCard>>(`/collection/cards?${cardViewQuery(filters)}`)
      .then((page) => {
        setCards(page.items)
        setNextCursor(page.nextCursor)
      })
      .catch((caught: unknown) => setError(caught))
  }, [state.status, request, filters])

  async function loadMore() {
    if (!nextCursor || !cards) {
      return
    }
    try {
      const page = await request<Page<CollectionCard>>(`/collection/cards?${cardViewQuery(filters, nextCursor)}`)
      setCards([...cards, ...page.items])
      setNextCursor(page.nextCursor)
    } catch (caught) {
      setError(caught)
    }
  }

  function apply(event: FormEvent) {
    event.preventDefault()
    setFilters(draft)
  }

  if (state.status !== 'authenticated') {
    return null
  }

  const filtered = Object.values(filters).some((value) => value.trim() !== '')
  return (
    <section className="page page-wide">
      <div className="page-header">
        <h1>{t('view.title')}</h1>
        <div className="form-actions">
          <Link to="/collection/locations">{t('view.locationsLink')}</Link>
          <Link to="/collection/movements">{t('movements.link')}</Link>
        </div>
      </div>
      <ErrorAlert error={error} />
      {settings && <AdvancedFieldsPanel settings={settings} onSaved={setSettings} />}
      <form className="deck-filters" aria-label={t('view.filters')} onSubmit={apply}>
        <div className="field">
          <label htmlFor="collection-search">{t('view.search')}</label>
          <input id="collection-search" type="search" value={draft.q} placeholder={t('view.searchPlaceholder')} onChange={(event) => setDraft({ ...draft, q: event.target.value })} />
        </div>
        <div className="field">
          <label htmlFor="collection-location">{t('view.location')}</label>
          <select id="collection-location" value={draft.locationId} onChange={(event) => setDraft({ ...draft, locationId: event.target.value })}>
            <option value="">{t('view.allLocations')}</option>
            {flattenLocations(locations).map(({ location, parent }) => (
              <option key={location.id} value={location.id}>
                {parent ? locationLabel(parent.name, location.name) : location.name}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="collection-language">{t('view.language')}</label>
          <select id="collection-language" value={draft.language} onChange={(event) => setDraft({ ...draft, language: event.target.value })}>
            <option value="">{t('view.allLanguages')}</option>
            {languages.map((language) => (
              <option key={language} value={language}>
                {t(`languages.${language}`)}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="collection-set">{t('view.set')}</label>
          <input id="collection-set" value={draft.set} placeholder={t('view.setPlaceholder')} onChange={(event) => setDraft({ ...draft, set: event.target.value })} />
        </div>
        <div className="form-actions">
          <button type="submit">{t('view.apply')}</button>
        </div>
      </form>
      {cards === null && <p role="status">{t('view.loading')}</p>}
      {cards?.length === 0 && <p>{filtered ? t('view.empty') : t('view.emptyCollection')}</p>}
      {cards && cards.length > 0 && (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t('fields.quantity')}</th>
                <th scope="col">{t('fields.card')}</th>
                <th scope="col">{t('fields.locations')}</th>
              </tr>
            </thead>
            <tbody>
              {cards.map((card) => (
                <tr key={card.oracleId}>
                  <td>{card.quantity}</td>
                  <td>
                    <Link to={`/cards/${card.oracleId}`}>{card.name}</Link>
                  </td>
                  <td>
                    <ul className="inline-list">
                      {card.locations.map((location) => (
                        <li key={location.locationId}>
                          <Link to={`/collection/locations/${location.locationId}`}>{locationLabel(location.locationName, location.sectionName)}</Link>{' '}
                          <span className="field-hint">({t('view.copies', { count: location.quantity })})</span>
                        </li>
                      ))}
                    </ul>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {nextCursor && (
        <button type="button" onClick={() => void loadMore()}>
          {t('view.loadMore')}
        </button>
      )}
    </section>
  )
}
