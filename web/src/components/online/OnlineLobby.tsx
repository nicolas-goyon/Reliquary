import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Lobby, NewInvite } from '../../playtest/online/gameApi'
import { ErrorAlert } from '../Forms'
import { ConfirmButton } from './ConfirmButton'
import { InviteLink } from './InviteLink'

interface OnlineLobbyProps {
  lobby: Lobby | null
  seat: string
  inviteSecret?: string
  gameId: string
  invoke<T>(method: string, ...args: unknown[]): Promise<T>
  onInviteChanged(inviteSecret: string): void
  /** The player left the lobby: the seat is free and the token stops. */
  onLeft(): void
}

/** The lobby of an online game: the seats, the ready players, and the controls of the host. */
export function OnlineLobby({ lobby, seat, inviteSecret, gameId, invoke, onInviteChanged, onLeft }: OnlineLobbyProps) {
  const { t } = useTranslation('playtest')
  const [error, setError] = useState<unknown>(null)
  const isHost = lobby?.hostSeat === seat

  async function call(method: string, ...args: unknown[]) {
    setError(null)
    try {
      return await invoke(method, ...args)
    } catch (caught) {
      setError(caught)
      return undefined
    }
  }

  async function leave() {
    setError(null)
    try {
      await invoke('Leave')
      onLeft()
    } catch (caught) {
      setError(caught)
    }
  }

  async function newInvite() {
    const result = (await call('NewInvite')) as NewInvite | undefined
    if (result?.inviteSecret) {
      onInviteChanged(result.inviteSecret)
    }
  }

  if (!lobby) {
    return <p role="status">{t('online.lobby.loading')}</p>
  }
  // A seat exists only with a deck, so the host can start when 2 players or more are in the lobby.
  const canStart = isHost && lobby.seats.length >= 2

  return (
    <section className="panel online-lobby" aria-labelledby={`${gameId}-lobby`}>
      <h2 id={`${gameId}-lobby`}>{t('online.lobby.title')}</h2>
      {isHost && inviteSecret && <InviteLink gameId={gameId} inviteSecret={inviteSecret} onNewInvite={() => void newInvite()} />}
      <p>{t('online.lobby.count', { count: lobby.seats.length, size: lobby.size })}</p>
      <ul className="table-players" aria-label={t('local.players')}>
        {lobby.seats.map((item) => (
          <li key={item.seat} className="table-player">
            <span className="table-player-name">
              {item.nickname}
              {item.seat === lobby.hostSeat && ' '}
              {item.seat === lobby.hostSeat && <span className="badge badge-category">{t('online.lobby.host')}</span>}
              {item.seat === seat && ' '}
              {item.seat === seat && <span className="badge">{t('online.lobby.you')}</span>}
            </span>{' '}
            <span className="table-player-facts">
              {item.guest ? t('online.lobby.guest') : t('online.lobby.account')}
              {item.cardCount !== undefined && `, ${t('online.lobby.cards', { count: item.cardCount })}`}
            </span>
            {isHost && item.seat !== seat && (
              <button type="button" className="button-small" onClick={() => void call('Kick', item.seat)}>
                {t('online.lobby.kick', { name: item.nickname })}
              </button>
            )}
          </li>
        ))}
      </ul>
      <ErrorAlert error={error} />
      <div className="form-actions">
        {isHost ? (
          <button type="button" className="button-primary" disabled={!canStart} onClick={() => void call('Start')}>
            {t('online.lobby.start')}
          </button>
        ) : (
          <p className="field-hint">{t('online.lobby.waitHost')}</p>
        )}
        {isHost ? (
          // When the host leaves the lobby, the server ends the game for all the players.
          <ConfirmButton label={t('online.end')} warning={t('online.endWarning')} confirmLabel={t('online.endConfirm')} onConfirm={() => void call('Close')} />
        ) : (
          <button type="button" onClick={() => void leave()}>
            {t('online.lobby.leave')}
          </button>
        )}
      </div>
    </section>
  )
}
