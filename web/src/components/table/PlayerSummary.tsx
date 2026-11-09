import { useTranslation } from 'react-i18next'
import type { TablePlayer, TableState } from '../../playtest/engine'

/**
 * The public information of each player: life points and the number of cards in the hand and in the library.
 * When onSelect is set, each player is a button that shows the board of the player.
 */
export function PlayerSummary({ table, selectedId, onSelect }: { table: TableState; selectedId?: string; onSelect?: (player: TablePlayer) => void }) {
  const { t } = useTranslation('playtest')
  return (
    <ul className="table-players" aria-label={t('local.players')}>
      {table.players.map((player) => {
        const active = player.id === table.activePlayerId
        const content = (
          <>
            <span className="table-player-name">
              {player.name}
              {active && <span className="badge badge-category">{t('local.active')}</span>}
            </span>{' '}
            <span className="table-player-facts">
              {t('local.facts', { life: player.life, hand: player.zones.hand.length, library: player.zones.library.length })}
            </span>
          </>
        )
        return (
          <li key={player.id} className={active ? 'table-player table-player-active' : 'table-player'} aria-current={active ? 'true' : undefined}>
            {onSelect ? (
              <button type="button" className="table-player-button" aria-pressed={player.id === selectedId} onClick={() => onSelect(player)}>
                {content}
              </button>
            ) : (
              content
            )}
          </li>
        )
      })}
    </ul>
  )
}
