import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError, apiRequest } from '../api/client'
import { imageOf, type AutocompleteItem, type CardSearchResponse } from '../api/cards'
import { keywordsFor, wordAt } from '../api/searchKeywords'
import { CardImage } from '../components/CardImage'
import { Combobox, type Suggestion } from '../components/Combobox'
import { ErrorAlert } from '../components/Forms'
import { SearchFilterPanel } from '../components/SearchFilterPanel'
import { Link } from '../router/Link'
import { navigate, useLocation } from '../router/navigation'

const pageSize = 60
const syntaxGuideUrl = 'https://scryfall.com/docs/syntax'

type Results = { state: 'idle' } | { state: 'loading' } | { state: 'error'; error: unknown } | ({ state: 'done' } & CardSearchResponse)

/** The card search page. The address keeps the query, so a user can share a search. */
export function CardSearchPage() {
  const query = useLocation().searchParams.get('q') ?? ''
  return <CardSearch key={query} query={query} />
}

function CardSearch({ query }: { query: string }) {
  const { t, i18n } = useTranslation('cards')
  const [text, setText] = useState(query)
  const [results, setResults] = useState<Results>(query ? { state: 'loading' } : { state: 'idle' })
  const [loadingMore, setLoadingMore] = useState(false)

  useEffect(() => {
    if (!query) {
      return
    }
    const controller = new AbortController()
    apiRequest<CardSearchResponse>(`/cards/search?q=${encodeURIComponent(query)}&limit=${pageSize}`, { signal: controller.signal })
      .then((response) => setResults({ state: 'done', ...response }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setResults({ state: 'error', error })
        }
      })
    return () => controller.abort()
  }, [query])

  async function loadMore() {
    if (results.state !== 'done' || !results.nextCursor) {
      return
    }
    setLoadingMore(true)
    try {
      const next = await apiRequest<CardSearchResponse>(
        `/cards/search?q=${encodeURIComponent(query)}&limit=${pageSize}&cursor=${encodeURIComponent(results.nextCursor)}`,
      )
      setResults({ ...results, items: [...results.items, ...next.items], nextCursor: next.nextCursor })
    } catch (error) {
      setResults({ state: 'error', error })
    } finally {
      setLoadingMore(false)
    }
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const trimmed = text.trim()
    navigate(trimmed ? `/cards?q=${encodeURIComponent(trimmed)}` : '/cards')
  }

  const suggest = useCallback(
    async (value: string, signal: AbortSignal): Promise<Suggestion<string>[]> => {
      const { start, word } = wordAt(value, value.length)
      const keywords = keywordsFor(word)
      if (keywords.length > 0) {
        return keywords.slice(0, 8).map((keyword) => ({ key: `k:${keyword}`, label: keyword, detail: t('suggestions.keyword'), value: value.slice(0, start) + keyword }))
      }
      if (value.trim().length < 2 || /[:<>=!"]/.test(value)) {
        return []
      }
      const lang = i18n.resolvedLanguage ?? 'en'
      const items = await apiRequest<AutocompleteItem[]>(`/cards/autocomplete?q=${encodeURIComponent(value.trim())}&lang=${lang}&limit=8`, { signal })
      return items.map((item) => ({
        key: item.oracleId,
        label: item.matchedName,
        detail: item.matchedName === item.name ? undefined : item.name,
        value: `/cards/${item.oracleId}`,
      }))
    },
    [i18n.resolvedLanguage, t],
  )

  function handleSelect(suggestion: Suggestion<string>) {
    if (suggestion.value.startsWith('/cards/')) {
      navigate(suggestion.value)
    } else {
      setText(suggestion.value)
    }
  }

  return (
    <section className="page page-wide">
      <h1>{t('search.title')}</h1>
      <form className="search-form" role="search" onSubmit={handleSubmit}>
        <Combobox
          label={t('search.label')}
          value={text}
          onChange={setText}
          suggest={suggest}
          onSelect={handleSelect}
          placeholder={t('search.placeholder')}
          spellCheck={false}
          autoCapitalize="off"
        />
        <button type="submit" className="button-primary">
          {t('search.submit')}
        </button>
        <SearchFilterPanel query={text} onChange={setText} />
      </form>
      <p className="field-hint">
        {t('search.help')}{' '}
        <a href={syntaxGuideUrl} target="_blank" rel="noreferrer">
          {t('search.syntaxGuide')}
        </a>
      </p>

      {results.state === 'loading' && <p role="status">{t('search.loading')}</p>}
      {results.state === 'error' && <SearchError error={results.error} query={query} />}
      {results.state === 'done' && (
        <>
          <p role="status">{t('search.total', { count: results.total })}</p>
          {results.warnings.length > 0 && (
            <ul className="alert search-warnings">
              {results.warnings.map((warning) => (
                <li key={`${warning.position}-${warning.term}`}>
                  {t(`warnings.${warningKey(warning.code)}`, { term: warning.term })}
                </li>
              ))}
            </ul>
          )}
          <ul className="card-grid">
            {results.items.map(({ card, printing }) => {
              const name = printing.printedName ?? card.name
              return (
                <li key={card.id}>
                  <Link to={`/cards/${card.id}`} className="card-tile">
                    <CardImage src={imageOf(printing)} alt={name} />
                  </Link>
                </li>
              )
            })}
          </ul>
          {results.nextCursor && (
            <button type="button" onClick={() => void loadMore()} disabled={loadingMore}>
              {t('search.more')}
            </button>
          )}
        </>
      )}
    </section>
  )
}

const knownWarnings = ['unknown_keyword', 'unsupported_keyword', 'invalid_value', 'invalid_operator'] as const

function warningKey(code: string): (typeof knownWarnings)[number] | 'other' {
  return knownWarnings.find((known) => known === code) ?? 'other'
}

/** Shows the error of a search. A syntax error shows the position in the query. */
function SearchError({ error, query }: { error: unknown; query: string }) {
  const { t } = useTranslation('cards')
  if (error instanceof ApiError && error.code === 'search_syntax_error' && typeof error.details.position === 'number') {
    const position = Math.min(error.details.position, query.length)
    return (
      <div className="alert alert-error" role="alert">
        <p>{t('search.syntaxError', { position: position + 1 })}</p>
        <pre className="query-error">
          <code>
            {query.slice(0, position)}
            <mark>{query.slice(position, position + 1) || ' '}</mark>
            {query.slice(position + 1)}
          </code>
        </pre>
      </div>
    )
  }
  return <ErrorAlert error={error} />
}
