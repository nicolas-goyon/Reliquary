import { useEffect, useMemo, useReducer, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Deck } from '../api/decks'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { HandStatisticsPanel } from '../components/table/HandStatisticsPanel'
import { PlayerTable } from '../components/table/PlayerTable'
import { playerSetup, startingLife } from '../playtest/deckSetup'
import { applyAction, createTable, type TableAction, type TableState } from '../playtest/engine'
import { Link } from '../router/Link'

function newSeed(): number {
  return crypto.getRandomValues(new Uint32Array(1))[0]
}

type GameAction = TableAction | { type: 'restart'; table: TableState }

function reducer(state: TableState, action: GameAction): TableState {
  return action.type === 'restart' ? action.table : applyAction(state, action)
}

function Game({ deck }: { deck: Deck }) {
  const { t } = useTranslation('playtest')
  const setup = useMemo(() => playerSetup(deck, 'player-1', deck.name), [deck])
  const [seed, setSeed] = useState(newSeed)
  const [table, dispatch] = useReducer(reducer, undefined, () => createTable([setup], seed, startingLife(deck.format)))
  const player = table.players[0]

  function restart() {
    const next = newSeed()
    setSeed(next)
    dispatch({ type: 'restart', table: createTable([setup], next, startingLife(deck.format)) })
  }

  return (
    <>
      <div className="form-actions">
        <strong>{t('turn', { turn: table.turn })}</strong>
        <button type="button" onClick={() => dispatch({ type: 'nextTurn' })}>
          {t('controls.nextTurn')}
        </button>
        <button type="button" onClick={restart}>
          {t('controls.restart')}
        </button>
      </div>
      <PlayerTable player={player} dispatch={dispatch} />
      <HandStatisticsPanel player={player} seed={seed} />
    </>
  )
}

/** The goldfish mode: one player tests the opening hands and the first turns of a deck. */
export function TablePage({ id }: { id: string }) {
  const { t } = useTranslation('playtest')
  const { request } = useAuth()
  const [deck, setDeck] = useState<Deck | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    request<Deck>(`/decks/${id}`)
      .then(setDeck)
      .catch((caught: unknown) => setError(caught))
  }, [id, request])

  return (
    <article className="page page-wide">
      {deck ? (
        <>
          <p>
            <Link to={`/decks/${id}`}>{deck.name}</Link>
          </p>
          <h1>{t('title', { name: deck.name })}</h1>
          <Game deck={deck} />
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
