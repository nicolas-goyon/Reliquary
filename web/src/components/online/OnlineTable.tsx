import { useCallback, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TableAction } from '../../playtest/engine'
import { newGameId } from '../../playtest/gameId'
import { projectPublicState } from '../../playtest/projection'
import type { ActResult } from '../../playtest/online/hubClient'
import { useGameHost } from '../../playtest/sync'
import type { PlayerView } from '../../playtest/views'
import { PlayerCounters } from '../table/PlayerCounters'
import { PlayerTable } from '../table/PlayerTable'
import { PublicBoard } from '../table/PublicBoard'
import { SharedScreenPanel } from '../table/SharedScreenPanel'
import { ConfirmButton } from './ConfirmButton'

interface OnlineTableProps {
  view: PlayerView
  seat: string
  log: string[]
  act(action: TableAction): Promise<ActResult>
  isHost: boolean
  /** The player leaves the table. The seat stays, so the player can come back. */
  onLeave(): void
  /** The host ends the game for all the players. */
  onEnd(): void
}

/** The codes of the refused actions that the interface translates. */
const rejectedCodes = ['invalid_action', 'not_your_object', 'not_your_turn', 'unknown_card', 'not_started', 'game_closed', 'rate_limited'] as const

/**
 * The online table of one player: the board and the hand of the player, and the boards of the other players with their hidden zones as counts.
 * The server applies the actions. This page sends each action to the hub and shows the view that the hub sends.
 * The page is also the game window of a table window on the same computer: the table window gets the public projection of the view.
 */
export function OnlineTable({ view, seat, log, act, isHost, onLeave, onEnd }: OnlineTableProps) {
  const { t } = useTranslation('playtest')
  const [rejected, setRejected] = useState<string | null>(null)
  const [windowId] = useState(newGameId)
  const player = view.players.find((item) => item.id === seat)
  const others = { ...view, players: view.players.filter((item) => item.id !== seat) }

  const send = useCallback(
    (action: TableAction) => {
      setRejected(null)
      act(action)
        .then((result) => {
          if ('rejected' in result) {
            setRejected(result.rejected)
          }
        })
        .catch(() => setRejected('other'))
    },
    [act],
  )
  // The table window does no actions, so the dispatch of the host is not used.
  useGameHost(windowId, view, send, projectPublicState)

  return (
    <>
      <div className="form-actions">
        <strong>{t('turn', { turn: view.turn })}</strong>
        <span>{t('online.table.active', { name: view.players.find((item) => item.id === view.activePlayerId)?.name ?? '' })}</span>
        {view.activePlayerId === seat && (
          <button type="button" className="button-primary" onClick={() => send({ type: 'nextTurn' })}>
            {t('local.nextPlayer')}
          </button>
        )}
      </div>
      {rejected && (
        <p className="alert alert-error" role="alert">
          {t(`online.rejected.${rejectedCodes.find((code) => code === rejected) ?? 'other'}`)}
        </p>
      )}
      {player ? (
        <PlayerTable player={player} dispatch={send} side={<PlayerCounters player={player} others={others.players} commander={view.commander} dispatch={send} />} />
      ) : (
        <p className="alert alert-warning">{t('online.table.noSeat')}</p>
      )}
      <section aria-labelledby={`${windowId}-others`}>
        <h2 id={`${windowId}-others`}>{t('online.table.others')}</h2>
        <PublicBoard table={others} />
      </section>
      <section className="panel online-log" aria-labelledby={`${windowId}-log`}>
        <h2 id={`${windowId}-log`}>{t('online.table.log')}</h2>
        {log.length === 0 ? (
          <p className="field-hint">{t('online.table.logEmpty')}</p>
        ) : (
          <ol className="online-log-list" aria-live="polite">
            {log.map((line, index) => (
              <li key={`${log.length - index}`}>{line}</li>
            ))}
          </ol>
        )}
      </section>
      <SharedScreenPanel gameId={windowId} players={[]} />
      <div className="form-actions online-exit">
        <button type="button" onClick={onLeave}>
          {t('online.table.leave')}
        </button>
        {isHost && <ConfirmButton label={t('online.end')} warning={t('online.endWarning')} confirmLabel={t('online.endConfirm')} onConfirm={onEnd} />}
      </div>
    </>
  )
}
