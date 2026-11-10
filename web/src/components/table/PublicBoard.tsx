import { useTranslation } from 'react-i18next'
import { playerCounters, type TableCard, type TablePlayer, type TableState, type TableZone } from '../../playtest/engine'
import { CardImage } from '../CardImage'

/** One card of the table window. A card with no identity shows its back. */
function PublicCard({ card }: { card: TableCard }) {
  const { t } = useTranslation('playtest')
  return (
    <li className={card.tapped ? 'table-card table-card-small table-card-tapped' : 'table-card table-card-small'}>
      <span className="table-card-face">
        {card.faceDown ? (
          <span className="table-card-back" role="img" aria-label={card.tapped ? t('card.backTapped') : t('card.back')}>
            {t('card.back')}
          </span>
        ) : (
          <CardImage src={card.imageUri ?? undefined} alt={card.tapped ? t('card.tapped', { name: card.name }) : card.name} />
        )}
      </span>
      {card.counters > 0 && <span className="badge">{t('card.counters', { count: card.counters })}</span>}
    </li>
  )
}

function PublicZone({ player, zone }: { player: TablePlayer; zone: TableZone }) {
  const { t } = useTranslation('playtest')
  const cards = player.zones[zone]
  return (
    <section className="table-zone" aria-label={t('shared.zoneOf', { zone: t(`zones.${zone}`), name: player.name })}>
      <h3>
        {t(`zones.${zone}`)} ({cards.length})
      </h3>
      {cards.length > 0 && (
        <ul className="table-cards">
          {cards.map((card) => (
            <PublicCard key={card.id} card={card} />
          ))}
        </ul>
      )}
    </section>
  )
}

/** The hidden zones: the table window shows only the number of cards. */
function HiddenCounts({ player }: { player: TablePlayer }) {
  const { t } = useTranslation('playtest')
  return (
    <dl className="facts table-hidden-counts">
      <dt>{t('zones.hand')}</dt>
      <dd>{player.zones.hand.length}</dd>
      <dt>{t('zones.library')}</dt>
      <dd>{player.zones.library.length}</dd>
    </dl>
  )
}

/**
 * The board of each player as the table window shows it. The state must come from projectPublicState:
 * this component does not hide cards itself, it shows only what the projection gives.
 * The commander damage on a board comes from the players of `sources`. The default is the players of the table.
 * The online table shows only the other players, but the commander damage can also come from the own player.
 */
export function PublicBoard({ table, sources = table.players }: { table: TableState; sources?: TablePlayer[] }) {
  const { t } = useTranslation('playtest')
  return (
    <div className="table-public">
      {table.players.map((player) => {
        const active = player.id === table.activePlayerId
        const counters = playerCounters.filter((counter) => player.counters[counter] > 0)
        const damage = sources.filter((other) => (player.commanderDamage[other.id] ?? 0) > 0)
        return (
          <section key={player.id} className={active ? 'table-public-player table-player-active' : 'table-public-player'} aria-label={player.name}>
            <header className="table-public-header">
              <h2>
                {player.name}
                {active && ' '}
                {active && <span className="badge badge-category">{t('local.active')}</span>}
              </h2>
              <span className="table-life">{t('shared.life', { count: player.life })}</span>
            </header>
            <HiddenCounts player={player} />
            {(counters.length > 0 || damage.length > 0) && (
              <ul className="table-public-counters">
                {counters.map((counter) => (
                  <li key={counter}>
                    {t('shared.counterValue', { label: t(`counters.${counter}`), value: player.counters[counter] })}
                  </li>
                ))}
                {damage.map((other) => (
                  <li key={other.id}>
                    {t('shared.counterValue', { label: t('counters.commanderDamage', { name: other.name }), value: player.commanderDamage[other.id] })}
                  </li>
                ))}
              </ul>
            )}
            <PublicZone player={player} zone="battlefield" />
            <div className="table-public-side">
              <PublicZone player={player} zone="command" />
              <PublicZone player={player} zone="graveyard" />
              <PublicZone player={player} zone="exile" />
            </div>
          </section>
        )
      })}
    </div>
  )
}
