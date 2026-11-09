import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Deck } from '../api/decks'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { CreateOnlineGame } from '../components/online/CreateOnlineGame'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

/** The page that creates an online game with a deck. Only an account can create a game. */
export function OnlineCreatePage({ id }: { id: string }) {
  const { t } = useTranslation('playtest')
  const { state, request } = useAuth()
  const [deck, setDeck] = useState<Deck | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent(`/decks/${id}/test/online`)}`, { replace: true })
      return
    }
    if (state.status === 'authenticated') {
      request<Deck>(`/decks/${id}`)
        .then(setDeck)
        .catch((caught: unknown) => setError(caught))
    }
  }, [id, request, state.status])

  return (
    <article className="page">
      {deck ? (
        <>
          <p>
            <Link to={`/decks/${id}/test`}>{t('title', { name: deck.name })}</Link>
          </p>
          <h1>{t('online.create.pageTitle', { name: deck.name })}</h1>
          <CreateOnlineGame deckId={deck.id} />
        </>
      ) : (
        <>
          <ErrorAlert error={error} />
          {!error && <p role="status">{t('loading')}</p>}
        </>
      )}
    </article>
  )
}
