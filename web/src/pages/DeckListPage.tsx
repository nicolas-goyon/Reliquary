import { useEffect, useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Page } from '../api/cards'
import {
  deckFormats,
  deckListQuery,
  deckSorts,
  formatKey,
  identityColors,
  type Deck,
  type DeckFolder,
  type DeckListFilters,
  type DeckSort,
  type DeckSummary,
  type DeckTagSummary,
  type Visibility,
} from '../api/decks'
import { useAuth } from '../auth/session'
import { FormatSelect, VisibilitySelect } from '../components/DeckFields'
import { DeckFolderTree } from '../components/DeckFolders'
import { DeckGroupActions } from '../components/DeckGroupActions'
import { ErrorAlert, TextField } from '../components/Forms'
import { ManaCost } from '../components/ManaCost'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

/** The list of the decks of the user, with the folders, the filters, the group actions and the dialog of a new deck. */
export function DeckListPage() {
  const { t, i18n } = useTranslation('decks')
  const { state, request } = useAuth()
  const [decks, setDecks] = useState<DeckSummary[] | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [folders, setFolders] = useState<DeckFolder[]>([])
  const [deckTags, setDeckTags] = useState<DeckTagSummary[]>([])
  const [filters, setFilters] = useState<DeckListFilters>({ folderId: '', format: '', colors: '', tag: '', archived: 'exclude', sort: 'updated' })
  const [selected, setSelected] = useState<string[]>([])
  const [lastClicked, setLastClicked] = useState<number | null>(null)
  const [reload, setReload] = useState(0)
  const [error, setError] = useState<unknown>(null)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent('/decks')}`, { replace: true })
      return
    }
    if (state.status !== 'authenticated') {
      return
    }
    Promise.all([request<Page<DeckSummary>>(`/decks?${deckListQuery(filters)}`), request<DeckFolder[]>('/deck-folders'), request<DeckTagSummary[]>('/decks/tags')])
      .then(([page, folderList, tagList]) => {
        setDecks(page.items)
        setNextCursor(page.nextCursor)
        setFolders(folderList)
        setDeckTags(tagList)
        setSelected((current) => current.filter((id) => page.items.some((deck) => deck.id === id)))
      })
      .catch((caught: unknown) => setError(caught))
  }, [state.status, request, filters, reload])

  async function loadMore() {
    if (!nextCursor || !decks) {
      return
    }
    const page = await request<Page<DeckSummary>>(`/decks?${deckListQuery(filters)}&cursor=${encodeURIComponent(nextCursor)}`)
    setDecks([...decks, ...page.items])
    setNextCursor(page.nextCursor)
  }

  function changeFilter(change: Partial<DeckListFilters>) {
    setFilters({ ...filters, ...change })
    setSelected([])
    setLastClicked(null)
  }

  /** Selects a deck. Shift and click selects the range from the last deck. */
  function toggle(index: number, shiftKey: boolean) {
    if (!decks) {
      return
    }
    const id = decks[index].id
    const select = !selected.includes(id)
    if (shiftKey && lastClicked !== null) {
      const [from, to] = [Math.min(lastClicked, index), Math.max(lastClicked, index)]
      const range = decks.slice(from, to + 1).map((deck) => deck.id)
      setSelected(select ? [...new Set([...selected, ...range])] : selected.filter((current) => !range.includes(current)))
    } else {
      setSelected(select ? [...selected, id] : selected.filter((current) => current !== id))
    }
    setLastClicked(index)
  }

  if (state.status !== 'authenticated') {
    return null
  }

  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  const allSelected = decks !== null && decks.length > 0 && selected.length === decks.length
  return (
    <section className="page page-wide">
      <div className="page-header">
        <h1>{t('list.title')}</h1>
        <div className="form-actions">
          <Link to="/decks/import">{t('import.title')}</Link>
          <button type="button" className="button-primary" data-shortcut="new-deck" onClick={() => setCreating(true)}>
            {t('list.create')}
          </button>
        </div>
      </div>
      <ErrorAlert error={error} />
      {creating && <CreateDeckDialog folderId={filters.folderId && filters.folderId !== 'none' ? filters.folderId : null} onClose={() => setCreating(false)} />}
      <div className="deck-list-layout">
        <DeckFolderTree folders={folders} selected={filters.folderId} onSelect={(folderId) => changeFilter({ folderId })} onChanged={() => setReload(reload + 1)} />
        <div className="deck-list-main">
          <DeckListFilterBar filters={filters} deckTags={deckTags} onChange={changeFilter} />
          {selected.length > 0 && (
            <DeckGroupActions
              deckIds={selected}
              folders={folders}
              onDone={() => {
                setSelected([])
                setReload(reload + 1)
              }}
            />
          )}
          {decks && decks.length === 0 && <p>{t('list.empty')}</p>}
          {decks && decks.length > 0 && (
            <div className="table-scroll">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">
                      <input type="checkbox" aria-label={t('group.selectAll')} checked={allSelected} onChange={() => setSelected(allSelected ? [] : decks.map((deck) => deck.id))} />
                    </th>
                    <th scope="col">{t('fields.name')}</th>
                    <th scope="col">{t('fields.format')}</th>
                    <th scope="col">{t('list.colors')}</th>
                    <th scope="col">{t('list.cards')}</th>
                    <th scope="col" className="column-optional">
                      {t('list.tags')}
                    </th>
                    <th scope="col" className="column-optional">
                      {t('list.updated')}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {decks.map((deck, index) => (
                    <tr key={deck.id} aria-selected={selected.includes(deck.id)}>
                      <td>
                        <input
                          type="checkbox"
                          aria-label={t('group.select', { name: deck.name })}
                          checked={selected.includes(deck.id)}
                          onChange={() => undefined}
                          onClick={(event) => toggle(index, event.shiftKey)}
                        />
                      </td>
                      <td>
                        {deck.pinned && <span className="deck-badge">{t('list.pinned')}</span>}
                        {deck.archived && <span className="deck-badge">{t('list.archived')}</span>}
                        <Link to={`/decks/${deck.id}`}>{deck.name}</Link>
                      </td>
                      <td>{t(`formats.${formatKey(deck.format)}`)}</td>
                      <td>{deck.colorIdentity ? <ManaCost cost={deck.colorIdentity.split('').map((color) => `{${color}}`).join('')} /> : t('list.colorless')}</td>
                      <td>{deck.cardCount}</td>
                      <td className="column-optional">{deck.tags.join(', ')}</td>
                      <td className="column-optional">{dates.format(new Date(deck.updatedAt))}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {nextCursor && (
            <button type="button" onClick={() => void loadMore()}>
              {t('list.more')}
            </button>
          )}
        </div>
      </div>
    </section>
  )
}

/** The filters and the sort of the deck list. */
function DeckListFilterBar({ filters, deckTags, onChange }: { filters: DeckListFilters; deckTags: DeckTagSummary[]; onChange(change: Partial<DeckListFilters>): void }) {
  const { t } = useTranslation('decks')
  const id = useId()
  const colors = filters.colors === 'C' ? [] : filters.colors.split('').filter(Boolean)

  function toggleColor(color: string) {
    const next = colors.includes(color) ? colors.filter((current) => current !== color) : [...colors, color]
    onChange({ colors: identityColors.filter((known) => next.includes(known)).join('') })
  }

  return (
    <div className="deck-filters">
      <div className="field">
        <label htmlFor={`${id}-format`}>{t('fields.format')}</label>
        <select id={`${id}-format`} value={filters.format} onChange={(event) => onChange({ format: event.target.value })}>
          <option value="">{t('filters.allFormats')}</option>
          {deckFormats.map((format) => (
            <option key={format} value={format}>
              {t(`formats.${format}`)}
            </option>
          ))}
        </select>
      </div>
      <fieldset className="field color-filter">
        <legend>{t('filters.colors')}</legend>
        {identityColors.map((color) => (
          <label key={color}>
            <input type="checkbox" checked={colors.includes(color)} onChange={() => toggleColor(color)} />
            {t(`filters.color.${color}`)}
          </label>
        ))}
        <label>
          <input type="checkbox" checked={filters.colors === 'C'} onChange={() => onChange({ colors: filters.colors === 'C' ? '' : 'C' })} />
          {t('list.colorless')}
        </label>
      </fieldset>
      <div className="field">
        <label htmlFor={`${id}-tag`}>{t('filters.tag')}</label>
        <select id={`${id}-tag`} value={filters.tag} onChange={(event) => onChange({ tag: event.target.value })}>
          <option value="">{t('filters.allTags')}</option>
          {deckTags.map((tag) => (
            <option key={tag.name} value={tag.name}>
              {`${tag.name} (${tag.deckCount})`}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor={`${id}-archived`}>{t('filters.archived')}</label>
        <select id={`${id}-archived`} value={filters.archived} onChange={(event) => onChange({ archived: event.target.value as DeckListFilters['archived'] })}>
          <option value="exclude">{t('filters.archivedExclude')}</option>
          <option value="include">{t('filters.archivedInclude')}</option>
          <option value="only">{t('filters.archivedOnly')}</option>
        </select>
      </div>
      <div className="field">
        <label htmlFor={`${id}-sort`}>{t('filters.sort')}</label>
        <select id={`${id}-sort`} value={filters.sort} onChange={(event) => onChange({ sort: event.target.value as DeckSort })}>
          {deckSorts.map((sort) => (
            <option key={sort} value={sort}>
              {t(`filters.sorts.${sort}`)}
            </option>
          ))}
        </select>
      </div>
    </div>
  )
}

/** The dialog of a new deck: name, format and visibility. */
function CreateDeckDialog({ folderId, onClose }: { folderId: string | null; onClose(): void }) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const [name, setName] = useState('')
  const [format, setFormat] = useState<string>('commander')
  const [visibility, setVisibility] = useState<Visibility>('private')
  const [error, setError] = useState<unknown>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      const deck = await request<Deck>('/decks', { method: 'POST', body: { name, format, visibility, ...(folderId ? { folderId } : {}) } })
      navigate(`/decks/${deck.id}`)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <div className="dialog" role="dialog" aria-modal="false" aria-labelledby="create-deck-title">
      <form className="form panel" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <h2 id="create-deck-title">{t('list.create')}</h2>
        <TextField label={t('fields.name')} field="name" error={error} required maxLength={100} autoFocus value={name} onChange={(event) => setName(event.target.value)} />
        <FormatSelect value={format} onChange={setFormat} />
        <VisibilitySelect value={visibility} onChange={setVisibility} />
        <ErrorAlert error={error} />
        <div className="form-actions">
          <button type="submit" className="button-primary">
            {t('list.createSubmit')}
          </button>
          <button type="button" onClick={onClose}>
            {t('actions.cancel')}
          </button>
        </div>
      </form>
    </div>
  )
}
