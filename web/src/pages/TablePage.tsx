import { useCallback, useEffect, useMemo, useReducer, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Deck } from '../api/decks'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { HandStatisticsPanel } from '../components/table/HandStatisticsPanel'
import { PlayerTable } from '../components/table/PlayerTable'
import { SharedScreenPanel } from '../components/table/SharedScreenPanel'
import { playerSetup, startingLife, usesCommanderDamage } from '../playtest/deckSetup'
import { createTable, type TableAction } from '../playtest/engine'
import { gameReducer } from '../playtest/game'
import { newGameId, newSeed } from '../playtest/gameId'
import { projectPublicState } from '../playtest/projection'
import { useGameHost } from '../playtest/sync'
import { Link } from '../router/Link'

function Game({ deck }: { deck: Deck }) {
  const { t } = useTranslation('playtest')
  const setup = useMemo(() => playerSetup(deck, 'player-1', deck.name), [deck])
  const [seed, setSeed] = useState(newSeed)
  const [table, dispatch] = useReducer(gameReducer, undefined, () => createTable([setup], seed, startingLife(deck.format), { commander: usesCommanderDamage([deck]) }))
  const player = table.players[0]
  const [gameId] = useState(newGameId)
  const tableDispatch = useCallback((action: TableAction) => dispatch(action), [])
  // This window is the game window of the shared screen. The table window gets the public projection only.
  useGameHost(gameId, table, tableDispatch, projectPublicState)

  function restart() {
    const next = newSeed()
    setSeed(next)
    dispatch({ type: 'restart', table: createTable([setup], next, startingLife(deck.format), { commander: usesCommanderDamage([deck]) }) })
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
        <Link to={`/decks/${deck.id}/test/local`} className="button">
          {t('local.action')}
        </Link>
        <Link to={`/decks/${deck.id}/test/online`} className="button">
          {t('online.action')}
        </Link>
      </div>
      <PlayerTable player={player} dispatch={dispatch} />
      <HandStatisticsPanel player={player} seed={seed} />
      <SharedScreenPanel gameId={gameId} players={[]} />
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
