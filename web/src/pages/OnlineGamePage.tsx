import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { JoinOnlineGame } from '../components/online/JoinOnlineGame'
import { OnlineLobby } from '../components/online/OnlineLobby'
import { OnlineTable } from '../components/online/OnlineTable'
import { forgetSeat, hubSocket, hubUrl, storedSeat, storeSeat, type SeatAccess } from '../playtest/online/gameApi'
import { useOnlineGame } from '../playtest/online/useOnlineGame'

const closedReasons = ['ended', 'kicked', 'expired'] as const

/** Reads the invite secret from the fragment of the invite link. */
function inviteSecretOf(hash: string): string | null {
  const value = decodeURIComponent(hash.replace(/^#/, ''))
  return value || null
}

/**
 * The page of an online game: join, lobby and table.
 * - With no seat in this tab, the page shows the join form. It needs the invite secret of the link.
 * - With a seat, the page connects to the hub. Before the start, it shows the lobby. After the start, it shows the table.
 */
export function OnlineGamePage({ gameId }: { gameId: string }) {
  const { t } = useTranslation('playtest')
  const [access, setAccess] = useState<(SeatAccess & { inviteSecret?: string }) | null>(() => storedSeat(gameId))
  const [inviteSecret] = useState(() => inviteSecretOf(window.location.hash))
  const [url] = useState(() => hubUrl())
  // After the start, a player who leaves the table keeps the seat and can come back with the same token.
  const [away, setAway] = useState(false)
  const game = useOnlineGame(away ? null : (access?.gameToken ?? null), url, hubSocket.create)

  // A closed game cannot come back: a reload must not connect to it again.
  useEffect(() => {
    if (game.closedReason) {
      forgetSeat(gameId)
    }
  }, [game.closedReason, gameId])

  function leftLobby() {
    forgetSeat(gameId)
    setAccess(null)
  }

  function changeInvite(secret: string) {
    if (access) {
      const next = { ...access, inviteSecret: secret }
      storeSeat(next)
      setAccess(next)
    }
  }

  let content
  if (!access) {
    content = inviteSecret ? (
      <JoinOnlineGame gameId={gameId} inviteSecret={inviteSecret} onJoined={setAccess} />
    ) : (
      <p className="alert alert-error" role="alert">
        {t('online.join.noInvite')}
      </p>
    )
  } else if (game.closedReason) {
    content = (
      <p className="alert alert-error" role="alert">
        {t(`online.closed.${closedReasons.find((reason) => reason === game.closedReason) ?? 'other'}`)}
      </p>
    )
  } else if (away) {
    content = (
      <div className="panel">
        <p>{t('online.away')}</p>
        <button type="button" className="button-primary" onClick={() => setAway(false)}>
          {t('online.comeBack')}
        </button>
      </div>
    )
  } else if (game.view) {
    content = (
      <OnlineTable
        view={game.view}
        seat={access.seat}
        log={game.log}
        act={game.act}
        isHost={game.lobby?.hostSeat === access.seat}
        onLeave={() => void game.invoke('Leave').finally(() => setAway(true))}
        onEnd={() => void game.invoke('Close').catch(() => undefined)}
      />
    )
  } else {
    content = (
      <OnlineLobby lobby={game.lobby} seat={access.seat} inviteSecret={access.inviteSecret} gameId={gameId} invoke={game.invoke} onInviteChanged={changeInvite} onLeft={leftLobby} />
    )
  }

  return (
    <article className="page page-wide">
      <h1>{t('online.title')}</h1>
      {access && !away && game.status !== 'connected' && !game.closedReason && (
        <p className="alert alert-warning" role="status">
          {t(`online.status.${game.status}`)}
        </p>
      )}
      {game.desyncs > 0 && game.status === 'connected' && <p className="field-hint">{t('online.desyncs', { count: game.desyncs })}</p>}
      {content}
    </article>
  )
}
