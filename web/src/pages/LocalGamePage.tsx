import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Page } from '../api/cards'
import type { Deck, DeckSummary } from '../api/decks'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { LocalGame } from '../components/table/LocalGame'
import { LocalGameSetup, type LocalGameChoice, type LocalMode } from '../components/table/LocalGameSetup'
import { Link } from '../router/Link'

/**
 * The local multiplayer mode: one person tests a deck against 2 or 3 other decks on one computer.
 * The other decks come from the deck list of the user. A user who is not signed in can play copies of the same deck.
 */
export function LocalGamePage({ id }: { id: string }) {
  const { t } = useTranslation('playtest')
  const { state, request } = useAuth()
  const [deck, setDeck] = useState<Deck | null>(null)
  const [ownDecks, setOwnDecks] = useState<DeckSummary[] | null>(null)
  const [game, setGame] = useState<{ decks: Deck[]; mode: LocalMode } | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    request<Deck>(`/decks/${id}`)
      .then(setDeck)
      .catch((caught: unknown) => setError(caught))
  }, [id, request])

  useEffect(() => {
    if (state.status === 'authenticated') {
      request<Page<DeckSummary>>('/decks?limit=100&sort=name')
        .then((page) => setOwnDecks(page.items))
        .catch((caught: unknown) => setError(caught))
    }
  }, [state.status, request])
  // A user who is not signed in has no deck list.
  const decks = state.status === 'anonymous' ? [] : ownDecks

  async function start(choice: LocalGameChoice) {
    if (!deck) {
      return
    }
    setError(null)
    try {
      // The same deck can be in the game more than one time. The page reads each deck one time.
      const ids = [...new Set(choice.opponentIds.filter((opponentId) => opponentId !== deck.id))]
      const loaded = new Map<string, Deck>([[deck.id, deck], ...(await Promise.all(ids.map(async (deckId) => [deckId, await request<Deck>(`/decks/${deckId}`)] as const)))])
      setGame({ decks: [deck, ...choice.opponentIds.map((opponentId) => loaded.get(opponentId) as Deck)], mode: choice.mode })
    } catch (caught: unknown) {
      setError(caught)
    }
  }

  return (
    <article className="page page-wide">
      {deck ? (
        <>
          <p>
            <Link to={`/decks/${id}/test`}>{t('title', { name: deck.name })}</Link>
          </p>
          <h1>{t('local.title', { name: deck.name })}</h1>
          <ErrorAlert error={error} />
          {game ? (
            <LocalGame decks={game.decks} mode={game.mode} />
          ) : decks ? (
            <LocalGameSetup deckId={deck.id} deckName={deck.name} decks={decks} onStart={(choice) => void start(choice)} />
          ) : (
            !error && <p role="status">{t('loading')}</p>
          )}
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
