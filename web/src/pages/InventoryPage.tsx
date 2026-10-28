import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { AutocompleteItem, Page } from '../api/cards'
import { apiRequest } from '../api/client'
import {
  addOrigins,
  countedCard,
  endpointValue,
  flattenLocations,
  parseEndpoint,
  removeDestinations,
  type CollectionEntry,
  type CollectionLocation,
  type Inventory,
  type InventorySummary,
  type NewEntry,
} from '../api/collection'
import { useAuth } from '../auth/session'
import { Combobox, type Suggestion } from '../components/Combobox'
import { ErrorAlert, SuccessAlert } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

/** A line of the count: an entry of the location, or a card that the user added. */
interface CountLine {
  key: string
  name: string
  detail: string
  card: NewEntry
  count: string
}

type Target = { location: CollectionLocation; parent: CollectionLocation | null }

function lineOf(entry: CollectionEntry): CountLine {
  const detail = [entry.setCode?.toUpperCase(), entry.language, entry.finish, entry.condition].filter(Boolean).join(' · ')
  return { key: entry.id, name: entry.name, detail, card: countedCard(entry, entry.quantity), count: String(entry.quantity) }
}

/** The inventory of a location: the count, the differences with their answers, and the history of the inventories. */
export function InventoryPage({ id }: { id: string }) {
  const { t, i18n } = useTranslation('collection')
  const { state, request } = useAuth()
  const [target, setTarget] = useState<Target | null>(null)
  const [others, setOthers] = useState<Target[]>([])
  const [lines, setLines] = useState<CountLine[] | null>(null)
  const [inventory, setInventory] = useState<Inventory | null>(null)
  const [history, setHistory] = useState<InventorySummary[]>([])
  const [applied, setApplied] = useState(false)
  const [error, setError] = useState<unknown>(null)

  const loadHistory = useCallback(() => {
    request<InventorySummary[]>(`/collection/locations/${id}/inventories`)
      .then(setHistory)
      .catch((caught: unknown) => setError(caught))
  }, [id, request])

  /** Gets all the entries of the location, page by page. */
  const fetchEntries = useCallback(async () => {
    const entries: CollectionEntry[] = []
    let cursor: string | null = null
    do {
      const page: Page<CollectionEntry> = await request<Page<CollectionEntry>>(`/collection/locations/${id}/entries?limit=100${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`)
      entries.push(...page.items)
      cursor = page.nextCursor
    } while (cursor)
    return entries
  }, [id, request])

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent(`/collection/locations/${id}/inventory`)}`, { replace: true })
      return
    }
    if (state.status !== 'authenticated') {
      return
    }
    request<CollectionLocation[]>('/collection/locations')
      .then((locations) => {
        const all = flattenLocations(locations)
        setTarget(all.find((item) => item.location.id === id) ?? null)
        setOthers(all.filter((item) => item.location.id !== id && item.location.kind !== 'deck'))
      })
      .catch((caught: unknown) => setError(caught))
    fetchEntries()
      .then((entries) => setLines(entries.map(lineOf)))
      .catch((caught: unknown) => setError(caught))
    loadHistory()
  }, [id, state.status, request, fetchEntries, loadHistory])

  async function compare(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      const entries = (lines ?? [])
        .map((line) => ({ ...line.card, quantity: Math.trunc(Number(line.count)) || 0 }))
        .filter((card) => card.quantity > 0)
      setInventory(await request<Inventory>(`/collection/locations/${id}/inventories`, { method: 'POST', body: { entries } }))
      loadHistory()
    } catch (caught) {
      setError(caught)
    }
  }

  async function apply(body: unknown) {
    if (!inventory) {
      return
    }
    setError(null)
    try {
      await request<Inventory>(`/collection/inventories/${inventory.id}/apply`, { method: 'POST', body })
      setInventory(null)
      setApplied(true)
      loadHistory()
      setLines((await fetchEntries()).map(lineOf))
    } catch (caught) {
      setError(caught)
    }
  }

  async function cancel() {
    if (!inventory) {
      return
    }
    try {
      await request(`/collection/inventories/${inventory.id}`, { method: 'DELETE' })
      setInventory(null)
      loadHistory()
    } catch (caught) {
      setError(caught)
    }
  }

  if (state.status !== 'authenticated') {
    return null
  }
  if (!target || !lines) {
    return (
      <>
        <ErrorAlert error={error} />
        <p role="status">{t('inventory.loading')}</p>
      </>
    )
  }

  const name = target.parent ? `${target.parent.name} / ${target.location.name}` : target.location.name
  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium', timeStyle: 'short' })
  return (
    <article className="page page-wide">
      <p>
        <Link to={`/collection/locations/${id}`}>{name}</Link>
      </p>
      <h1>{t('inventory.title', { name })}</h1>
      <ErrorAlert error={error} />
      {applied && <SuccessAlert>{t('inventory.applied')}</SuccessAlert>}
      {inventory ? (
        <DifferencesForm inventory={inventory} others={others} onApply={apply} onCancel={() => void cancel()} />
      ) : (
        <CountForm
          lines={lines}
          onChange={(next) => {
            setLines(next)
            setApplied(false)
          }}
          onSubmit={compare}
        />
      )}
      <InventoryHistory history={history} format={(value) => dates.format(new Date(value))} />
    </article>
  )
}

interface CountFormProps {
  lines: CountLine[]
  onChange(lines: CountLine[]): void
  onSubmit(event: FormEvent): Promise<void>
}

function CountForm({ lines, onChange, onSubmit }: CountFormProps) {
  const { t, i18n } = useTranslation('collection')
  const [text, setText] = useState('')

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

  return (
    <form className="panel form" aria-label={t('inventory.countTitle')} onSubmit={(event) => void onSubmit(event)}>
      <h2>{t('inventory.countTitle')}</h2>
      <p className="field-hint">{t('inventory.countHint')}</p>
      <table className="table">
        <thead>
          <tr>
            <th scope="col">{t('fields.card')}</th>
            <th scope="col">{t('inventory.counted')}</th>
          </tr>
        </thead>
        <tbody>
          {lines.map((line) => (
            <tr key={line.key}>
              <td>
                {line.name} {line.detail && <span className="field-hint">{line.detail}</span>}
              </td>
              <td>
                <input
                  type="number"
                  min={0}
                  max={100000}
                  aria-label={t('inventory.countLabel', { name: line.name })}
                  value={line.count}
                  onChange={(event) => onChange(lines.map((candidate) => (candidate.key === line.key ? { ...candidate, count: event.target.value } : candidate)))}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <Combobox
        label={t('inventory.addCard')}
        value={text}
        onChange={setText}
        suggest={suggest}
        onSelect={(selected) => {
          onChange([...lines, { key: `new-${lines.length}-${selected.value}`, name: selected.label, detail: '', card: { oracleId: selected.value, quantity: 1 }, count: '1' }])
          setText('')
        }}
        placeholder={t('location.addCardPlaceholder')}
      />
      <div className="form-actions">
        <button type="submit" className="button-primary">
          {t('inventory.compare')}
        </button>
      </div>
    </form>
  )
}

interface DifferencesProps {
  inventory: Inventory
  others: Target[]
  onApply(body: unknown): Promise<void>
  onCancel(): void
}

function DifferencesForm({ inventory, others, onApply, onCancel }: DifferencesProps) {
  const { t } = useTranslation('collection')
  const [answers, setAnswers] = useState<Record<string, string>>({})
  const [origin, setOrigin] = useState('')
  const [destination, setDestination] = useState('')

  function options(added: boolean) {
    return (
      <>
        {others.map(({ location, parent }) => (
          <option key={location.id} value={endpointValue('location', location.id)}>
            {parent ? `${parent.name} / ${location.name}` : location.name}
          </option>
        ))}
        {(added ? addOrigins : removeDestinations).map((kind) => (
          <option key={kind} value={kind}>
            {t(`endpoints.${kind}`)}
          </option>
        ))}
      </>
    )
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    await onApply({
      answers: Object.entries(answers)
        .filter(([, value]) => value)
        .map(([key, value]) => ({ key, ...parseEndpoint(value) })),
      ...(origin ? { origin: parseEndpoint(origin) } : {}),
      ...(destination ? { destination: parseEndpoint(destination) } : {}),
    })
  }

  const differences = inventory.differences
  return (
    <form className="panel form" aria-label={t('inventory.differencesTitle')} onSubmit={(event) => void submit(event)}>
      <h2>{t('inventory.differencesTitle')}</h2>
      {differences.length === 0 ? (
        <p>{t('inventory.noDifference')}</p>
      ) : (
        <>
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t('fields.card')}</th>
                <th scope="col">{t('inventory.expected')}</th>
                <th scope="col">{t('inventory.counted')}</th>
                <th scope="col">{t('inventory.change')}</th>
                <th scope="col">{t('inventory.answer')}</th>
              </tr>
            </thead>
            <tbody>
              {differences.map((difference) => (
                <tr key={difference.key}>
                  <td>
                    {difference.card.name}{' '}
                    <span className="field-hint">{[difference.card.setCode?.toUpperCase(), difference.card.language, difference.card.finish].filter(Boolean).join(' · ')}</span>
                  </td>
                  <td>{difference.expected}</td>
                  <td>{difference.counted}</td>
                  <td>{difference.card.quantity > 0 ? `+${difference.card.quantity}` : difference.card.quantity}</td>
                  <td>
                    <select
                      aria-label={t('inventory.answerLabel', { name: difference.card.name })}
                      value={answers[difference.key] ?? ''}
                      onChange={(event) => setAnswers({ ...answers, [difference.key]: event.target.value })}
                    >
                      <option value="">{t('inventory.useGroup')}</option>
                      {options(difference.card.quantity > 0)}
                    </select>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="form-inline">
            {differences.some((difference) => difference.card.quantity > 0) && (
              <div className="field">
                <label htmlFor="group-origin">{t('inventory.groupOrigin')}</label>
                <select id="group-origin" value={origin} onChange={(event) => setOrigin(event.target.value)}>
                  <option value="" />
                  {options(true)}
                </select>
              </div>
            )}
            {differences.some((difference) => difference.card.quantity < 0) && (
              <div className="field">
                <label htmlFor="group-destination">{t('inventory.groupDestination')}</label>
                <select id="group-destination" value={destination} onChange={(event) => setDestination(event.target.value)}>
                  <option value="" />
                  {options(false)}
                </select>
              </div>
            )}
          </div>
        </>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary">
          {t('inventory.apply')}
        </button>
        <button type="button" onClick={onCancel}>
          {t('inventory.cancel')}
        </button>
      </div>
    </form>
  )
}

function InventoryHistory({ history, format }: { history: InventorySummary[]; format(value: string): string }) {
  const { t } = useTranslation('collection')
  const { request } = useAuth()
  const [shown, setShown] = useState<Inventory | null>(null)

  async function show(inventoryId: string) {
    setShown(shown?.id === inventoryId ? null : await request<Inventory>(`/collection/inventories/${inventoryId}`))
  }

  return (
    <section className="panel" aria-labelledby="inventory-history">
      <h2 id="inventory-history">{t('inventory.historyTitle')}</h2>
      {history.length === 0 ? (
        <p>{t('inventory.historyEmpty')}</p>
      ) : (
        <ul>
          {history.map((item) => (
            <li key={item.id}>
              <button type="button" className="button-link" aria-expanded={shown?.id === item.id} onClick={() => void show(item.id)}>
                {t('inventory.content', { date: format(item.appliedAt ?? item.createdAt) })}
              </button>{' '}
              <span className="field-hint">
                {t('inventory.cards', { count: item.cardCount })}
                {item.status === 'open' && ` · ${t('inventory.open')}`}
              </span>
              {shown?.id === item.id && (
                <ul>
                  {shown.entries.map((entry, index) => (
                    <li key={`${entry.oracleId}-${index}`}>
                      {entry.quantity} {entry.name} <span className="field-hint">{[entry.setCode?.toUpperCase(), entry.language, entry.finish].filter(Boolean).join(' · ')}</span>
                    </li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
