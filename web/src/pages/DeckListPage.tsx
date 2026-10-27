import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Page } from '../api/cards'
import { formatKey, type Deck, type DeckSummary, type Visibility } from '../api/decks'
import { useAuth } from '../auth/session'
import { FormatSelect, VisibilitySelect } from '../components/DeckFields'
import { ErrorAlert, TextField } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

/** The list of the decks of the user, with the dialog of a new deck. */
export function DeckListPage() {
  const { t, i18n } = useTranslation('decks')
  const { state, request } = useAuth()
  const [decks, setDecks] = useState<DeckSummary[] | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
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
    request<Page<DeckSummary>>('/decks?limit=100')
      .then((page) => {
        setDecks(page.items)
        setNextCursor(page.nextCursor)
      })
      .catch((caught: unknown) => setError(caught))
  }, [state.status, request])

  async function loadMore() {
    if (!nextCursor || !decks) {
      return
    }
    const page = await request<Page<DeckSummary>>(`/decks?limit=100&cursor=${encodeURIComponent(nextCursor)}`)
    setDecks([...decks, ...page.items])
    setNextCursor(page.nextCursor)
  }

  if (state.status !== 'authenticated') {
    return null
  }

  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  return (
    <section className="page page-wide">
      <div className="page-header">
        <h1>{t('list.title')}</h1>
        <div className="form-actions">
          <Link to="/decks/import">{t('import.title')}</Link>
          <button type="button" className="button-primary" onClick={() => setCreating(true)}>
            {t('list.create')}
          </button>
        </div>
      </div>
      <ErrorAlert error={error} />
      {creating && <CreateDeckDialog onClose={() => setCreating(false)} />}
      {decks && decks.length === 0 && <p>{t('list.empty')}</p>}
      {decks && decks.length > 0 && (
        <table className="table">
          <thead>
            <tr>
              <th scope="col">{t('fields.name')}</th>
              <th scope="col">{t('fields.format')}</th>
              <th scope="col">{t('list.cards')}</th>
              <th scope="col">{t('fields.visibility')}</th>
              <th scope="col">{t('list.updated')}</th>
            </tr>
          </thead>
          <tbody>
            {decks.map((deck) => (
              <tr key={deck.id}>
                <td>
                  <Link to={`/decks/${deck.id}`}>{deck.name}</Link>
                </td>
                <td>{t(`formats.${formatKey(deck.format)}`)}</td>
                <td>{deck.cardCount}</td>
                <td>{t(`visibility.${deck.visibility}`)}</td>
                <td>{dates.format(new Date(deck.updatedAt))}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {nextCursor && (
        <button type="button" onClick={() => void loadMore()}>
          {t('list.more')}
        </button>
      )}
    </section>
  )
}

/** The dialog of a new deck: name, format and visibility. */
function CreateDeckDialog({ onClose }: { onClose(): void }) {
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
      const deck = await request<Deck>('/decks', { method: 'POST', body: { name, format, visibility } })
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
