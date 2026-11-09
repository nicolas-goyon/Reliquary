import { useTranslation } from 'react-i18next'
import type { TablePlayer } from '../../playtest/engine'
import { playerWindowPath, tableWindowPath } from '../../playtest/gameId'

/**
 * The links to the other windows of a game: the table window, and the player window of each player in the list.
 * Each link opens a new window. This window keeps the game, so it must stay open.
 */
export function SharedScreenPanel({ gameId, players }: { gameId: string; players: TablePlayer[] }) {
  const { t } = useTranslation('playtest')
  return (
    <section className="panel table-shared">
      <h2>{t('shared.title')}</h2>
      <p className="field-hint">{t('shared.hint')}</p>
      <ul className="table-window-links">
        <li>
          <a href={tableWindowPath(gameId)} target="_blank" rel="noopener">
            {t('shared.openTable')}
          </a>
        </li>
        {players.map((player) => (
          <li key={player.id}>
            <a href={playerWindowPath(gameId, player.id)} target="_blank" rel="noopener">
              {t('shared.openPlayer', { name: player.name })}
            </a>
          </li>
        ))}
      </ul>
    </section>
  )
}
