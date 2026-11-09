import { useTranslation } from 'react-i18next'
import { PublicBoard } from '../components/table/PublicBoard'
import { publicViewerId } from '../playtest/projection'
import { useGameViewer } from '../playtest/sync'

/**
 * The table window of a game on one computer: the boards of all the players, with the hidden information masked.
 * The game window sends only the public projection of the game to this window. This window sends no actions.
 * Use it for a screen share, a second screen or a stream.
 */
export function TableWindowPage({ gameId }: { gameId: string }) {
  const { t } = useTranslation('playtest')
  const viewer = useGameViewer(gameId, publicViewerId)
  const table = viewer.state

  return (
    <article className="page page-wide table-window">
      <h1>{t('shared.tableTitle')}</h1>
      {table ? (
        <>
          <p className="table-window-turn">
            <strong>{t('turn', { turn: table.turn })}</strong>
          </p>
          {viewer.status === 'closed' && (
            <p className="alert alert-error" role="alert">
              {t('playerWindow.status.closed')}
            </p>
          )}
          <PublicBoard table={table} />
        </>
      ) : (
        <p className={viewer.status === 'waiting' ? undefined : 'alert alert-error'} role="status">
          {t(`playerWindow.status.${viewer.status === 'connected' ? 'waiting' : viewer.status}`)}
        </p>
      )}
    </article>
  )
}
