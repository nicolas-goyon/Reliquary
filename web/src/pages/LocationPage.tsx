import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import type { AutocompleteItem, Page } from '../api/cards'
import { ApiError, apiRequest } from '../api/client'
import {
  addOrigins,
  conditions,
  finishes,
  flattenLocations,
  languages,
  removeDestinations,
  type AdvancedField,
  type CollectionEntry,
  type CollectionLocation,
  type CollectionSettings,
  type Condition,
  type EntriesChange,
  type Finish,
  type NewEntry,
  type StorageRule,
} from '../api/collection'
import { useAuth } from '../auth/session'
import { DeckTokensPanel } from '../components/collection/DeckTokensPanel'
import { Combobox, type Suggestion } from '../components/Combobox'
import { ErrorAlert } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

type Loaded = { location: CollectionLocation; parent: CollectionLocation | null; settings: CollectionSettings; locations: CollectionLocation[] }

/** The page of a location: its cards and the cards of its sections, with the forms to add and change cards. */
export function LocationPage({ id }: { id: string }) {
  const { t } = useTranslation('collection')
  const { state, request } = useAuth()
  const [loaded, setLoaded] = useState<Loaded | null>(null)
  const [error, setError] = useState<unknown>(null)

  const load = useCallback(() => {
    Promise.all([request<CollectionLocation[]>('/collection/locations'), request<CollectionSettings>('/collection/settings')])
      .then(([locations, settings]) => {
        const found = flattenLocations(locations).find((item) => item.location.id === id)
        if (!found) {
          setError(new ApiError(404, 'not_found', 'The location does not exist.'))
          return
        }
        setLoaded({ ...found, settings, locations })
      })
      .catch((caught: unknown) => setError(caught))
  }, [id, request])

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent(`/collection/locations/${id}`)}`, { replace: true })
      return
    }
    if (state.status === 'authenticated') {
      load()
    }
  }, [id, state.status, load])

  if (error) {
    return <ErrorAlert error={error} />
  }
  if (!loaded) {
    return <p role="status">{t('location.loading')}</p>
  }
  return <LocationContent key={loaded.location.updatedAt} {...loaded} onChanged={load} />
}

interface ContentProps extends Loaded {
  onChanged(): void
}

function LocationContent({ location, parent, settings, locations, onChanged }: ContentProps) {
  const { t } = useTranslation('collection')
  const { request } = useAuth()
  const [entries, setEntries] = useState<CollectionEntry[] | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [withSections, setWithSections] = useState(location.sections.length > 0)
  const [search, setSearch] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [selected, setSelected] = useState<string[]>([])
  const locked = location.locked || parent?.locked === true
  const readOnly = locked || location.kind === 'deck'
  const enabled = settings.advancedFields
  const sectionNames = new Map(location.sections.map((section) => [section.id, section.name]))

  const query = useCallback(
    (cursor?: string | null) => {
      const parameters = new URLSearchParams({ limit: '100' })
      if (withSections) {
        parameters.set('sections', 'true')
      }
      if (search.trim()) {
        parameters.set('q', search.trim())
      }
      if (cursor) {
        parameters.set('cursor', cursor)
      }
      return `/collection/locations/${location.id}/entries?${parameters.toString()}`
    },
    [location.id, search, withSections],
  )

  const reload = useCallback(() => {
    request<Page<CollectionEntry>>(query())
      .then((page) => {
        setEntries(page.items)
        setNextCursor(page.nextCursor)
      })
      .catch((caught: unknown) => setError(caught))
  }, [query, request])

  useEffect(() => {
    reload()
  }, [reload])

  async function loadMore() {
    if (!nextCursor || !entries) {
      return
    }
    try {
      const page = await request<Page<CollectionEntry>>(query(nextCursor))
      setEntries([...entries, ...page.items])
      setNextCursor(page.nextCursor)
    } catch (caught) {
      setError(caught)
    }
  }

  async function setQuantity(entry: CollectionEntry, quantity: number) {
    setError(null)
    try {
      const change = await request<EntriesChange>(`/collection/entries/${entry.id}`, { method: 'PATCH', body: { quantity } })
      const updated = change.entries.find((candidate) => candidate.id === entry.id)
      setEntries((current) => (current ?? []).flatMap((candidate) => (candidate.id !== entry.id ? [candidate] : updated ? [updated] : [])))
    } catch (caught) {
      setError(caught)
    }
  }

  async function remove(entry: CollectionEntry) {
    setError(null)
    try {
      await request(`/collection/entries/${entry.id}`, { method: 'DELETE' })
      setEntries((current) => (current ?? []).filter((candidate) => candidate.id !== entry.id))
    } catch (caught) {
      setError(caught)
    }
  }

  async function move(destination: { kind: string; locationId?: string }) {
    setError(null)
    try {
      await request('/collection/movements', {
        method: 'POST',
        body: { origin: { kind: 'location', locationId: location.id }, destination, cards: selected.map((entryId) => ({ entryId })) },
      })
      setSelected([])
      reload()
      onChanged()
    } catch (caught) {
      setError(caught)
    }
  }

  async function add(cards: NewEntry[], origin: string) {
    setError(null)
    try {
      await request<EntriesChange>(`/collection/locations/${location.id}/entries`, { method: 'POST', body: { entries: cards, origin } })
      reload()
      onChanged()
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <article className="page page-wide">
      <p>
        <Link to="/collection/locations">{t('location.back')}</Link> · <Link to={`/collection/movements?locationId=${location.id}`}>{t('movements.locationLink')}</Link>
        {location.kind !== 'deck' && (
          <>
            {' · '}
            <Link to={`/collection/locations/${location.id}/inventory`}>{t('inventory.link')}</Link>
          </>
        )}
      </p>
      <div className="page-header">
        <h1>{parent ? `${parent.name} / ${location.name}` : location.name}</h1>
        <span className="badge">{t(`kinds.${location.kind}`)}</span>
      </div>
      {location.kind === 'deck' && <p className="alert alert-warning">{t('location.deckNotice')}</p>}
      {locked && <p className="alert alert-warning">{t('location.lockedNotice')}</p>}
      {location.kind !== 'deck' && !(location.pickable && parent?.pickable !== false) && <p className="field-hint">{t('location.notPickableNotice')}</p>}
      <ErrorAlert error={error} />
      {location.kind === 'deck' && <DeckTokensPanel locationId={location.id} />}
      {!readOnly && <AddEntriesForm locationId={location.id} enabled={enabled} onAdd={add} />}
      <div className="deck-filters">
        <div className="field">
          <label htmlFor="location-search">{t('location.search')}</label>
          <input id="location-search" type="search" value={search} onChange={(event) => setSearch(event.target.value)} />
        </div>
        {location.sections.length > 0 && (
          <label className="filter-check">
            <input type="checkbox" checked={withSections} onChange={() => setWithSections(!withSections)} />
            {t('location.showSections')}
          </label>
        )}
      </div>
      {!readOnly && selected.length > 0 && (
        <MoveBar count={selected.length} targets={flattenLocations(locations).filter((item) => item.location.id !== location.id && item.location.kind !== 'deck')} onMove={move} />
      )}
      {entries?.length === 0 && <p>{t('location.empty')}</p>}
      {entries && entries.length > 0 && (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                {!readOnly && (
                  <th scope="col">
                    <input
                      type="checkbox"
                      aria-label={t('location.selectAll')}
                      checked={selected.length > 0 && selected.length === entries.length}
                      onChange={() => setSelected(selected.length === entries.length ? [] : entries.map((entry) => entry.id))}
                    />
                  </th>
                )}
                <th scope="col">{t('fields.quantity')}</th>
                <th scope="col">{t('fields.card')}</th>
                {withSections && <th scope="col">{t('fields.section')}</th>}
                <th scope="col">{t('fields.set')}</th>
                <th scope="col">{t('fields.language')}</th>
                {enabled.map((field) => (
                  <th key={field} scope="col">
                    {t(`fields.${field}`)}
                  </th>
                ))}
                {!readOnly && <th scope="col">{t('locations.actions')}</th>}
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={entry.id}>
                  {!readOnly && (
                    <td>
                      <input
                        type="checkbox"
                        aria-label={t('location.select', { name: entry.name })}
                        checked={selected.includes(entry.id)}
                        onChange={() => setSelected(selected.includes(entry.id) ? selected.filter((id) => id !== entry.id) : [...selected, entry.id])}
                      />
                    </td>
                  )}
                  <td>
                    {readOnly ? (
                      entry.quantity
                    ) : (
                      <span className="deck-line-quantity">
                        <button type="button" className="button-small" aria-label={t('location.decrease', { name: entry.name })} onClick={() => void setQuantity(entry, entry.quantity - 1)}>
                          −
                        </button>
                        <span>{entry.quantity}</span>
                        <button type="button" className="button-small" aria-label={t('location.increase', { name: entry.name })} onClick={() => void setQuantity(entry, entry.quantity + 1)}>
                          +
                        </button>
                      </span>
                    )}
                  </td>
                  <td>
                    <Link to={`/cards/${entry.oracleId}`}>{entry.name}</Link>
                  </td>
                  {withSections && <td>{sectionNames.get(entry.locationId) ?? ''}</td>}
                  <td>{entry.setCode ? `${entry.setCode.toUpperCase()} ${entry.collectorNumber ?? ''}`.trim() : ''}</td>
                  <td>{t(`languages.${entry.language as (typeof languages)[number]}`, { defaultValue: entry.language })}</td>
                  {enabled.map((field) => (
                    <td key={field}>{fieldText(entry, field, t)}</td>
                  ))}
                  {!readOnly && (
                    <td>
                      <button type="button" className="button-small button-danger" aria-label={t('location.removeLabel', { name: entry.name })} onClick={() => void remove(entry)}>
                        {t('location.remove')}
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {nextCursor && (
        <button type="button" onClick={() => void loadMore()}>
          {t('location.loadMore')}
        </button>
      )}
    </article>
  )
}

type Translate = TFunction<'collection'>

function fieldText(entry: CollectionEntry, field: AdvancedField, t: Translate): string {
  switch (field) {
    case 'finish':
      return entry.finish ? t(`finishes.${entry.finish}`) : ''
    case 'condition':
      return entry.condition ? t(`conditions.${entry.condition}`) : ''
    case 'purchasePrice':
      return entry.purchasePrice !== undefined ? `${entry.purchasePrice} ${entry.purchaseCurrency ?? ''}`.trim() : ''
    case 'acquiredOn':
      return entry.acquiredOn ?? ''
    case 'note':
      return entry.note ?? ''
  }
}

interface AddFormProps {
  locationId: string
  enabled: AdvancedField[]
  onAdd(cards: NewEntry[], origin: string): Promise<void>
}

/** The form that adds copies of a card to the location. It shows only the enabled advanced fields. */
function AddEntriesForm({ locationId, enabled, onAdd }: AddFormProps) {
  const { t, i18n } = useTranslation('collection')
  const { request } = useAuth()
  const [suggestion, setSuggestion] = useState<StorageRule | null>(null)
  const [text, setText] = useState('')
  const [card, setCard] = useState<{ oracleId: string; name: string } | null>(null)
  const [quantity, setQuantity] = useState('1')
  const [language, setLanguage] = useState('')
  const [origin, setOrigin] = useState<string>('nowhere')
  const [finish, setFinish] = useState<Finish | ''>('')
  const [condition, setCondition] = useState<Condition | ''>('')
  const [price, setPrice] = useState('')
  const [currency, setCurrency] = useState('EUR')
  const [acquiredOn, setAcquiredOn] = useState('')
  const [note, setNote] = useState('')

  const suggest = useCallback(
    async (value: string, signal: AbortSignal): Promise<Suggestion<string>[]> => {
      if (value.trim().length < 2) {
        return []
      }
      const items = await apiRequest<AutocompleteItem[]>(`/cards/autocomplete?q=${encodeURIComponent(value.trim())}&lang=${i18n.resolvedLanguage ?? 'en'}&limit=8`, { signal })
      return items.map((item) => ({ key: item.oracleId, label: item.matchedName, detail: item.matchedName === item.name ? undefined : item.name, value: item.oracleId }))
    },
    [i18n.resolvedLanguage],
  )

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!card) {
      return
    }
    const entry: NewEntry = { oracleId: card.oracleId, quantity: Math.max(1, Math.trunc(Number(quantity)) || 1) }
    if (language) {
      entry.language = language
    }
    if (enabled.includes('finish') && finish) {
      entry.finish = finish
    }
    if (enabled.includes('condition') && condition) {
      entry.condition = condition
    }
    if (enabled.includes('purchasePrice') && price) {
      entry.purchasePrice = Number(price)
      entry.purchaseCurrency = currency
    }
    if (enabled.includes('acquiredOn') && acquiredOn) {
      entry.acquiredOn = acquiredOn
    }
    if (enabled.includes('note') && note.trim()) {
      entry.note = note.trim()
    }
    await onAdd([entry], origin)
    setCard(null)
    setText('')
    setQuantity('1')
  }

  return (
    <form className="panel form" aria-label={t('location.addTitle')} onSubmit={(event) => void submit(event)}>
      <h2>{t('location.addTitle')}</h2>
      <div className="form-inline">
        <Combobox
          label={t('location.addCard')}
          value={text}
          onChange={(value) => {
            setText(value)
            setCard(null)
            setSuggestion(null)
          }}
          suggest={suggest}
          onSelect={(selected) => {
            setCard({ oracleId: selected.value, name: selected.label })
            setText(selected.label)
            setSuggestion(null)
            request<StorageRule | undefined>(`/collection/storage-rules/suggestion?oracleId=${selected.value}`)
              .then((rule) => setSuggestion(rule ?? null))
              .catch(() => setSuggestion(null))
          }}
          placeholder={t('location.addCardPlaceholder')}
        />
        <div className="field">
          <label htmlFor="add-quantity">{t('fields.quantity')}</label>
          <input id="add-quantity" type="number" min={1} max={100000} value={quantity} onChange={(event) => setQuantity(event.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="add-language">{t('fields.language')}</label>
          <select id="add-language" value={language} onChange={(event) => setLanguage(event.target.value)}>
            <option value="">{t('location.defaultLanguage')}</option>
            {languages.map((value) => (
              <option key={value} value={value}>
                {t(`languages.${value}`)}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="add-origin">{t('fields.origin')}</label>
          <select id="add-origin" value={origin} onChange={(event) => setOrigin(event.target.value)}>
            {addOrigins.map((value) => (
              <option key={value} value={value}>
                {t(`endpoints.${value}`)}
              </option>
            ))}
          </select>
        </div>
        {enabled.includes('finish') && (
          <div className="field">
            <label htmlFor="add-finish">{t('fields.finish')}</label>
            <select id="add-finish" value={finish} onChange={(event) => setFinish(event.target.value as Finish | '')}>
              <option value="" />
              {finishes.map((value) => (
                <option key={value} value={value}>
                  {t(`finishes.${value}`)}
                </option>
              ))}
            </select>
          </div>
        )}
        {enabled.includes('condition') && (
          <div className="field">
            <label htmlFor="add-condition">{t('fields.condition')}</label>
            <select id="add-condition" value={condition} onChange={(event) => setCondition(event.target.value as Condition | '')}>
              <option value="" />
              {conditions.map((value) => (
                <option key={value} value={value}>
                  {t(`conditions.${value}`)}
                </option>
              ))}
            </select>
          </div>
        )}
        {enabled.includes('purchasePrice') && (
          <>
            <div className="field">
              <label htmlFor="add-price">{t('fields.purchasePrice')}</label>
              <input id="add-price" type="number" min={0} step="0.01" value={price} onChange={(event) => setPrice(event.target.value)} />
            </div>
            <div className="field">
              <label htmlFor="add-currency">{t('fields.purchaseCurrency')}</label>
              <input id="add-currency" value={currency} maxLength={3} size={4} onChange={(event) => setCurrency(event.target.value.toUpperCase())} />
            </div>
          </>
        )}
        {enabled.includes('acquiredOn') && (
          <div className="field">
            <label htmlFor="add-date">{t('fields.acquiredOn')}</label>
            <input id="add-date" type="date" value={acquiredOn} onChange={(event) => setAcquiredOn(event.target.value)} />
          </div>
        )}
        {enabled.includes('note') && (
          <div className="field">
            <label htmlFor="add-note">{t('fields.note')}</label>
            <input id="add-note" value={note} maxLength={500} onChange={(event) => setNote(event.target.value)} />
          </div>
        )}
      </div>
      {card && suggestion && (
        <p className="collection-suggestion" role="status">
          {suggestion.locationId === locationId ? (
            t('location.suggestionHere')
          ) : (
            <Link to={`/collection/locations/${suggestion.locationId}`}>
              {t('location.suggestion', { location: suggestion.sectionName ? `${suggestion.locationName} / ${suggestion.sectionName}` : suggestion.locationName })}
            </Link>
          )}
        </p>
      )}
      <p className="field-hint">{t('location.addHint')}</p>
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={!card}>
          {t('location.add')}
        </button>
      </div>
    </form>
  )
}

interface MoveBarProps {
  count: number
  targets: { location: CollectionLocation; parent: CollectionLocation | null }[]
  onMove(destination: { kind: string; locationId?: string }): Promise<void>
}

/** Moves all the copies of the selected entries to another location, or removes them from the collection. */
function MoveBar({ count, targets, onMove }: MoveBarProps) {
  const { t } = useTranslation('collection')
  const [destination, setDestination] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    const [kind, locationId] = destination.split(':')
    await onMove(kind === 'location' ? { kind, locationId } : { kind })
    setDestination('')
  }

  return (
    <form className="group-actions" aria-label={t('location.moveTitle')} onSubmit={(event) => void submit(event)}>
      <span className="group-actions-count">{t('location.selected', { count })}</span>
      <div className="field">
        <label htmlFor="move-destination">{t('fields.destination')}</label>
        <select id="move-destination" value={destination} required onChange={(event) => setDestination(event.target.value)}>
          <option value="" />
          {targets.map(({ location, parent }) => (
            <option key={location.id} value={`location:${location.id}`}>
              {parent ? `${parent.name} / ${location.name}` : location.name}
            </option>
          ))}
          {removeDestinations.map((kind) => (
            <option key={kind} value={kind}>
              {t(`endpoints.${kind}`)}
            </option>
          ))}
        </select>
      </div>
      <button type="submit" className="button-primary" disabled={!destination}>
        {t('location.move')}
      </button>
    </form>
  )
}
