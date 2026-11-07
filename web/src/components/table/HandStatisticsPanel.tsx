import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { handStatistics, type TablePlayer } from '../../playtest/engine'

const samples = 2000

/** The statistics of the deck of a player, and the land count of the current hand. */
export function HandStatisticsPanel({ player, seed }: { player: TablePlayer; seed: number }) {
  const { t, i18n } = useTranslation('playtest')
  // The panel reads the deck when a new game starts: each new game has a new seed.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const allCards = useMemo(() => [...player.zones.library, ...player.zones.hand], [seed])
  const statistics = useMemo(() => handStatistics(allCards, samples, seed), [allCards, seed])
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 1 })
  const landsInHand = player.zones.hand.filter((card) => card.isLand).length

  return (
    <section className="panel">
      <h2>{t('statistics.title')}</h2>
      <p>{t('statistics.landsInHand', { count: landsInHand })}</p>
      <dl className="facts">
        <dt>{t('statistics.averageLands')}</dt>
        <dd>{number.format(statistics.averageLands)}</dd>
        <dt>{t('statistics.keepable')}</dt>
        <dd>{number.format(statistics.keepablePercent)} %</dd>
      </dl>
      <p className="field-hint">{t('statistics.samples', { count: samples })}</p>
    </section>
  )
}
