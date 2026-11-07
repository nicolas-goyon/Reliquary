import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { locationKinds, type CollectionLocation, type LocationKind } from '../api/collection'
import { useAuth } from '../auth/session'
import { StorageRulesPanel } from '../components/collection/StorageRulesPanel'
import { ErrorAlert, TextField } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

/** The list of the locations of the user: create, change, lock, unlock and delete. */
export function LocationsPage() {
  const { t } = useTranslation('collection')
  const { state, request } = useAuth()
  const [locations, setLocations] = useState<CollectionLocation[] | null>(null)
  const [error, setError] = useState<unknown>(null)

  const load = useCallback(() => {
    request<CollectionLocation[]>('/collection/locations')
      .then(setLocations)
      .catch((caught: unknown) => setError(caught))
  }, [request])

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent('/collection/locations')}`, { replace: true })
      return
    }
    if (state.status === 'authenticated') {
      load()
    }
  }, [state.status, load])

  /** Sends a change of a location, then loads the list again. */
  const change = useCallback(
    async (path: string, method: string, body?: unknown) => {
      setError(null)
      try {
        await request(`/collection/locations${path}`, { method, body })
        load()
      } catch (caught) {
        setError(caught)
      }
    },
    [load, request],
  )

  if (state.status !== 'authenticated') {
    return null
  }

  return (
    <section className="page page-wide">
      <div className="page-header">
        <h1>{t('locations.title')}</h1>
        <div className="form-actions">
          <Link to="/collection">{t('view.title')}</Link>
        </div>
      </div>
      <ErrorAlert error={error} />
      {locations && <CreateLocationForm locations={locations} onCreate={(body) => change('', 'POST', body)} />}
      {locations && locations.length > 0 && <StorageRulesPanel locations={locations} />}
      {locations?.length === 0 && <p>{t('locations.empty')}</p>}
      {locations && locations.length > 0 && (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t('fields.name')}</th>
                <th scope="col">{t('fields.kind')}</th>
                <th scope="col">{t('fields.quantity')}</th>
                <th scope="col">{t('locations.flags')}</th>
                <th scope="col">{t('locations.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {locations.flatMap((location) => [
                <LocationRow key={location.id} location={location} parent={null} change={change} />,
                ...location.sections.map((section) => <LocationRow key={section.id} location={section} parent={location} change={change} />),
              ])}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

interface CreateFormProps {
  locations: CollectionLocation[]
  onCreate(body: { name: string; kind?: LocationKind; parentId?: string }): Promise<void>
}

function CreateLocationForm({ locations, onCreate }: CreateFormProps) {
  const { t } = useTranslation('collection')
  const [name, setName] = useState('')
  const [kind, setKind] = useState<LocationKind>('rack')
  const [parentId, setParentId] = useState('')
  const parents = locations.filter((location) => location.kind !== 'deck')

  async function submit(event: FormEvent) {
    event.preventDefault()
    await onCreate(parentId ? { name: name.trim(), parentId } : { name: name.trim(), kind })
    setName('')
  }

  return (
    <form className="form-inline panel" aria-label={t('locations.create')} onSubmit={(event) => void submit(event)}>
      <TextField label={t('fields.name')} value={name} required maxLength={100} onChange={(event) => setName(event.target.value)} />
      <div className="field">
        <label htmlFor="new-location-parent">{t('fields.parent')}</label>
        <select id="new-location-parent" value={parentId} onChange={(event) => setParentId(event.target.value)} aria-describedby="new-location-hint">
          <option value="">{t('locations.noParent')}</option>
          {parents.map((location) => (
            <option key={location.id} value={location.id}>
              {location.name}
            </option>
          ))}
        </select>
        <p className="field-hint" id="new-location-hint">
          {t('locations.sectionHint')}
        </p>
      </div>
      {!parentId && (
        <div className="field">
          <label htmlFor="new-location-kind">{t('fields.kind')}</label>
          <select id="new-location-kind" value={kind} onChange={(event) => setKind(event.target.value as LocationKind)}>
            {locationKinds.map((value) => (
              <option key={value} value={value}>
                {t(`kinds.${value}`)}
              </option>
            ))}
          </select>
        </div>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={!name.trim()}>
          {t('locations.createButton')}
        </button>
      </div>
    </form>
  )
}

interface RowProps {
  location: CollectionLocation
  parent: CollectionLocation | null
  change(path: string, method: string, body?: unknown): Promise<void>
}

function LocationRow({ location, parent, change }: RowProps) {
  const { t } = useTranslation('collection')
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(location.name)
  const [kind, setKind] = useState<LocationKind>(location.kind)
  const deck = location.kind === 'deck'
  const lockedByParent = parent?.locked === true

  async function save(event: FormEvent) {
    event.preventDefault()
    await change(`/${location.id}`, 'PATCH', parent ? { name: name.trim() } : { name: name.trim(), kind })
    setEditing(false)
  }

  async function remove() {
    if (window.confirm(t('locations.deleteConfirm', { name: location.name }))) {
      await change(`/${location.id}`, 'DELETE')
    }
  }

  return (
    <tr>
      <td>
        {editing ? (
          <form className="form-inline" onSubmit={(event) => void save(event)}>
            <TextField label={t('fields.name')} value={name} required maxLength={100} onChange={(event) => setName(event.target.value)} />
            {!parent && (
              <div className="field">
                <label htmlFor={`kind-${location.id}`}>{t('fields.kind')}</label>
                <select id={`kind-${location.id}`} value={kind} onChange={(event) => setKind(event.target.value as LocationKind)}>
                  {locationKinds.map((value) => (
                    <option key={value} value={value}>
                      {t(`kinds.${value}`)}
                    </option>
                  ))}
                </select>
              </div>
            )}
            <button type="submit" className="button-primary">
              {t('locations.save')}
            </button>
            <button type="button" onClick={() => setEditing(false)}>
              {t('locations.cancel')}
            </button>
          </form>
        ) : (
          <>
            {parent && <span aria-hidden="true">↳ </span>}
            <Link to={`/collection/locations/${location.id}`}>{parent ? `${parent.name} / ${location.name}` : location.name}</Link>
          </>
        )}
      </td>
      <td>{t(`kinds.${location.kind}`)}</td>
      <td>{t('locations.cards', { count: location.totalCardCount })}</td>
      <td>
        {(location.locked || lockedByParent) && (
          <span className="badge">{location.locked ? t('locations.locked') : t('locations.lockedByParent')}</span>
        )}{' '}
        {!deck && (
          <label className="filter-check">
            <input type="checkbox" checked={location.pickable} onChange={() => void change(`/${location.id}`, 'PATCH', { pickable: !location.pickable })} />
            {t('locations.pickable')}
          </label>
        )}
        {deck && <span className="badge">{t('locations.notPickable')}</span>}
      </td>
      <td>
        <div className="form-actions">
          <button type="button" className="button-small" onClick={() => void change(`/${location.id}/${location.locked ? 'unlock' : 'lock'}`, 'POST')}>
            {location.locked ? t('locations.unlock') : t('locations.lock')}
          </button>
          {!deck && !editing && (
            <button type="button" className="button-small" onClick={() => setEditing(true)}>
              {t('locations.edit')}
            </button>
          )}
          {!deck && (
            <button type="button" className="button-small button-danger" onClick={() => void remove()}>
              {t('locations.delete')}
            </button>
          )}
        </div>
      </td>
    </tr>
  )
}
