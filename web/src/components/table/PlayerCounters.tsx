import type { Dispatch } from 'react'
import { useTranslation } from 'react-i18next'
import { playerCounters, type TableAction, type TablePlayer } from '../../playtest/engine'

/** A value with a button to remove one and a button to add one. */
function Stepper({ label, value, onChange }: { label: string; value: number; onChange: (change: number) => void }) {
  const { t } = useTranslation('playtest')
  return (
    <div className="table-stepper" role="group" aria-label={label}>
      <span className="table-stepper-label">{label}</span>
      <button type="button" className="button-small" aria-label={t('stepper.decrease', { label })} onClick={() => onChange(-1)}>
        −
      </button>
      <output className="table-stepper-value">{value}</output>
      <button type="button" className="button-small" aria-label={t('stepper.increase', { label })} onClick={() => onChange(1)}>
        +
      </button>
    </div>
  )
}

/**
 * The counters of a player. In a game that uses the commander damage, also the commander damage that the player took from each other player.
 * The players enter the values by hand: the table engine has no game rules.
 */
export function PlayerCounters({ player, others, commander, dispatch }: { player: TablePlayer; others: TablePlayer[]; commander: boolean; dispatch: Dispatch<TableAction> }) {
  const { t } = useTranslation('playtest')
  const playerId = player.id
  return (
    <section className="table-zone table-counters" aria-label={t('counters.title')}>
      <h2>{t('counters.title')}</h2>
      {playerCounters.map((counter) => (
        <Stepper key={counter} label={t(`counters.${counter}`)} value={player.counters[counter]} onChange={(change) => dispatch({ type: 'playerCounter', playerId, counter, change })} />
      ))}
      {commander &&
        others.map((other) => (
          <Stepper
            key={other.id}
            label={t('counters.commanderDamage', { name: other.name })}
            value={player.commanderDamage[other.id] ?? 0}
            onChange={(change) => dispatch({ type: 'commanderDamage', playerId, fromPlayerId: other.id, change })}
          />
        ))}
    </section>
  )
}
