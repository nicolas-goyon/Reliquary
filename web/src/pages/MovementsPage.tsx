import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Page } from '../api/cards'
import { flattenLocations, locationLabel, type CollectionLocation, type Movement, type MovementEndpoint } from '../api/collection'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate, useLocation } from '../router/navigation'

/** The history of the movements of the collection, with a filter by location. */
export function MovementsPage() {
  const { t, i18n } = useTranslation('collection')
  const { state, request } = useAuth()
  const url = useLocation()
  const locationId = url.searchParams.get('locationId') ?? ''
  const [movements, setMovements] = useState<Movement[] | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [locations, setLocations] = useState<CollectionLocation[]>([])
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent('/collection/movements')}`, { replace: true })
      return
    }
    if (state.status !== 'authenticated') {
      return
    }
    request<CollectionLocation[]>('/collection/locations')
      .then(setLocations)
      .catch((caught: unknown) => setError(caught))
  }, [state.status, request])

  useEffect(() => {
    if (state.status !== 'authenticated') {
      return
    }
    request<Page<Movement>>(`/collection/movements${locationId ? `?locationId=${locationId}` : ''}`)
      .then((page) => {
        setMovements(page.items)
        setNextCursor(page.nextCursor)
      })
      .catch((caught: unknown) => setError(caught))
  }, [state.status, request, locationId])

  async function loadMore() {
    if (!nextCursor || !movements) {
      return
    }
    try {
      const query = new URLSearchParams({ cursor: nextCursor })
      if (locationId) {
        query.set('locationId', locationId)
      }
      const page = await request<Page<Movement>>(`/collection/movements?${query.toString()}`)
      setMovements([...movements, ...page.items])
      setNextCursor(page.nextCursor)
    } catch (caught) {
      setError(caught)
    }
  }

  function endpoint(value: MovementEndpoint) {
    if (value.kind !== 'location') {
      return t(`endpoints.${value.kind}`)
    }
    return value.locationName ? <Link to={`/collection/locations/${value.locationId}`}>{value.locationName}</Link> : t('movements.deletedLocation')
  }

  if (state.status !== 'authenticated') {
    return null
  }

  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium', timeStyle: 'short' })
  return (
    <section className="page page-wide">
      <div className="page-header">
        <h1>{t('movements.title')}</h1>
        <div className="form-actions">
          <Link to="/collection">{t('view.title')}</Link>
          <Link to="/collection/locations">{t('locations.title')}</Link>
        </div>
      </div>
      <ErrorAlert error={error} />
      <div className="deck-filters">
        <div className="field">
          <label htmlFor="movements-location">{t('view.location')}</label>
          <select
            id="movements-location"
            value={locationId}
            onChange={(event) => navigate(event.target.value ? `/collection/movements?locationId=${event.target.value}` : '/collection/movements', { replace: true })}
          >
            <option value="">{t('view.allLocations')}</option>
            {flattenLocations(locations).map(({ location, parent }) => (
              <option key={location.id} value={location.id}>
                {parent ? locationLabel(parent.name, location.name) : location.name}
              </option>
            ))}
          </select>
        </div>
      </div>
      {movements?.length === 0 && <p>{t('movements.empty')}</p>}
      {movements && movements.length > 0 && (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t('movements.date')}</th>
                <th scope="col">{t('movements.kind')}</th>
                <th scope="col">{t('fields.quantity')}</th>
                <th scope="col">{t('fields.card')}</th>
                <th scope="col">{t('fields.origin')}</th>
                <th scope="col">{t('fields.destination')}</th>
              </tr>
            </thead>
            <tbody>
              {movements.map((movement) => (
                <tr key={movement.id}>
                  <td>{dates.format(new Date(movement.createdAt))}</td>
                  <td>
                    {t(`movements.kinds.${movement.kind}`)}
                    {movement.inventoryId && <span className="badge">{t('movements.inventory')}</span>}
                  </td>
                  <td>{movement.quantity}</td>
                  <td>
                    <Link to={`/cards/${movement.oracleId}`}>{movement.name}</Link>
                  </td>
                  <td>{endpoint(movement.origin)}</td>
                  <td>{endpoint(movement.destination)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {nextCursor && (
        <button type="button" onClick={() => void loadMore()}>
          {t('movements.loadMore')}
        </button>
      )}
    </section>
  )
}
