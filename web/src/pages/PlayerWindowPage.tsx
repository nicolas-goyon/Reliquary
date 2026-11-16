import { useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { PlayerCounters } from '../components/table/PlayerCounters'
import { PlayerSummary } from '../components/table/PlayerSummary'
import { PlayerTable } from '../components/table/PlayerTable'
import type { TableAction } from '../playtest/engine'
import { useGameViewer } from '../playtest/sync'

/**
 * The player window of one player of a game on one computer. The game window keeps the game.
 * This window shows the board and the hand of its player, and it sends the actions of the player to the game window.
 */
export function PlayerWindowPage({ gameId, playerId }: { gameId: string; playerId: string }) {
  const { t } = useTranslation('playtest')
  const viewer = useGameViewer(gameId, playerId)
  const { send } = viewer
  const dispatch = useCallback((action: TableAction) => send(action), [send])
  const table = viewer.state
  const player = table?.players.find((item) => item.id === playerId)

  if (!table || !player) {
    return (
      <article className="page page-wide">
        <h1>{t('playerWindow.titleEmpty')}</h1>
        <p className={viewer.status === 'waiting' ? undefined : 'alert alert-error'} role="status">
          {t(`playerWindow.status.${viewer.status === 'connected' ? 'unknownPlayer' : viewer.status}`)}
        </p>
      </article>
    )
  }

  return (
    <article className="page page-wide">
      <h1>{t('playerWindow.title', { name: player.name })}</h1>
      {viewer.status === 'closed' && (
        <p className="alert alert-error" role="alert">
          {t('playerWindow.status.closed')}
        </p>
      )}
      <div className="form-actions">
        <strong>{t('turn', { turn: table.turn })}</strong>
        <button type="button" onClick={() => dispatch({ type: 'nextTurn' })} disabled={viewer.status !== 'connected'}>
          {t('local.nextPlayer')}
        </button>
      </div>
      <PlayerSummary table={table} />
      <PlayerTable
        player={player}
        dispatch={dispatch}
        side={<PlayerCounters player={player} others={table.players.filter((other) => other.id !== player.id)} commander={table.commander} dispatch={dispatch} />}
      />
    </article>
  )
}
