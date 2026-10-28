import { useEffect, useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Page } from '../api/cards'
import { deckFormats } from '../api/decks'
import { emptyPublicDeckFilters, filtersFromSearch, publicDeckQuery, publicDeckSorts, type PublicDeck, type PublicDeckFilters, type PublicDeckSort } from '../api/publicDecks'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { PublicDeckList } from '../components/PublicDeckList'
import { navigate, useLocation } from '../router/navigation'

type View = 'search' | 'favorites' | 'following'

/** The exploration of the public decks: the search, and for a user the favorites and the decks of the followed authors. */
export function ExplorePage() {
  const { t } = useTranslation('decks')
  const { state, request } = useAuth()
  const location = useLocation()
  const id = useId()
  const applied = filtersFromSearch(location.search)
  const [form, setForm] = useState<PublicDeckFilters>(applied)
  const [view, setView] = useState<View>('search')
  const [decks, setDecks] = useState<PublicDeck[] | null>(null)
  const [error, setError] = useState<unknown>(null)
  const query = view === 'search' ? `/public-decks?${publicDeckQuery(applied)}` : `/public-decks/${view}?limit=50`

  useEffect(() => {
    if (state.status === 'loading') {
      return
    }
    request<Page<PublicDeck>>(query)
      .then((page) => {
        setDecks(page.items)
        setError(null)
      })
      .catch((caught: unknown) => {
        setDecks([])
        setError(caught)
      })
  }, [state.status, request, query])

  function submit(event: FormEvent) {
    event.preventDefault()
    setView('search')
    const search = publicDeckQuery(form).replace(/^limit=50&?/, '')
    navigate(search ? `/explore?${search}` : '/explore')
  }

  const field = (key: 'q' | 'commander' | 'card', label: string) => (
    <div className="field">
      <label htmlFor={`${id}-${key}`}>{label}</label>
      <input id={`${id}-${key}`} value={form[key]} onChange={(event) => setForm({ ...form, [key]: event.target.value })} />
    </div>
  )

  return (
    <section className="page page-wide">
      <h1>{t('explore.title')}</h1>
      {state.status === 'authenticated' && (
        <div className="segmented" role="group" aria-label={t('explore.views')}>
          {(['search', 'favorites', 'following'] as const).map((value) => (
            <button key={value} type="button" aria-pressed={view === value} onClick={() => setView(value)}>
              {t(`explore.view.${value}`)}
            </button>
          ))}
        </div>
      )}
      {view === 'search' && (
        <form className="deck-filters" role="search" onSubmit={submit}>
          {field('q', t('explore.name'))}
          {field('commander', t('explore.commander'))}
          {field('card', t('explore.card'))}
          <div className="field">
            <label htmlFor={`${id}-format`}>{t('fields.format')}</label>
            <select id={`${id}-format`} value={form.format} onChange={(event) => setForm({ ...form, format: event.target.value })}>
              <option value="">{t('filters.allFormats')}</option>
              {deckFormats.map((format) => (
                <option key={format} value={format}>
                  {t(`formats.${format}`)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor={`${id}-colors`}>{t('filters.colors')}</label>
            <input id={`${id}-colors`} placeholder="WUBRG" maxLength={5} value={form.colors} onChange={(event) => setForm({ ...form, colors: event.target.value })} />
          </div>
          <div className="field">
            <label htmlFor={`${id}-sort`}>{t('filters.sort')}</label>
            <select id={`${id}-sort`} value={form.sort} onChange={(event) => setForm({ ...form, sort: event.target.value as PublicDeckSort })}>
              {publicDeckSorts.map((sort) => (
                <option key={sort} value={sort}>
                  {t(`explore.sorts.${sort}`)}
                </option>
              ))}
            </select>
          </div>
          <div className="form-actions">
            <button type="submit" className="button-primary">
              {t('explore.search')}
            </button>
            <button
              type="button"
              onClick={() => {
                setForm(emptyPublicDeckFilters)
                navigate('/explore')
              }}
            >
              {t('explore.clear')}
            </button>
          </div>
        </form>
      )}
      <ErrorAlert error={error} />
      {decks === null ? <p role="status">{t('explore.loading')}</p> : <PublicDeckList decks={decks} headingLevel={2} />}
    </section>
  )
}
