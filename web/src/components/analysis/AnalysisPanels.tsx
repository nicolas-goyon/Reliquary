import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import {
  categoryKey, checkpointCode, colorKey, formatDecimal, formatPercent, manaStatus, needStatus, reasonCode, statusTone, typeKey, uncheckedCode,
  type DeckAnalysis,
} from '../../api/analysis'
import type { Deck } from '../../api/decks'
import { BarChart } from './BarChart'

interface PanelProps {
  deck: Deck
  analysis: DeckAnalysis
}

const lastCurvePoint = 7

/** The summary of the deck and its mana curve. */
export function CurvePanel({ analysis }: PanelProps) {
  const { t, i18n } = useTranslation('decks')
  const language = i18n.resolvedLanguage ?? 'en'
  return (
    <div className="analysis-panel">
      <dl className="analysis-summary">
        <div>
          <dt>{t('analysis.summary.cards')}</dt>
          <dd>{analysis.cardCount}</dd>
        </div>
        <div>
          <dt>{t('analysis.summary.lands')}</dt>
          <dd>{analysis.landCount}</dd>
        </div>
        <div>
          <dt>{t('analysis.summary.modalLands')}</dt>
          <dd>{analysis.modalLandCount}</dd>
        </div>
        <div>
          <dt>{t('analysis.summary.spells')}</dt>
          <dd>{analysis.spellCount}</dd>
        </div>
        <div>
          <dt>{t('analysis.summary.averageManaValue')}</dt>
          <dd>{formatDecimal(analysis.averageManaValue, language)}</dd>
        </div>
      </dl>
      <BarChart
        title={t('analysis.curve.title')}
        labelHeader={t('analysis.curve.manaValue')}
        valueHeader={t('analysis.cards')}
        orientation="columns"
        bars={analysis.manaCurve.map((point) => ({
          key: String(point.manaValue),
          label: point.manaValue >= lastCurvePoint ? t('analysis.curve.orMore', { manaValue: point.manaValue }) : String(point.manaValue),
          value: point.count,
        }))}
      />
      <p className="field-hint">{t('analysis.curve.hint')}</p>
    </div>
  )
}

/** The distributions of the card types, the colors and the categories. */
export function DistributionPanel({ analysis }: PanelProps) {
  const { t } = useTranslation('decks')
  return (
    <div className="analysis-panel analysis-grid">
      <BarChart
        title={t('analysis.types.title')}
        labelHeader={t('analysis.types.type')}
        valueHeader={t('analysis.cards')}
        bars={analysis.types.map((entry) => ({ key: entry.key, label: t(`analysis.types.keys.${typeKey(entry.key)}`), value: entry.count }))}
      />
      <BarChart
        title={t('analysis.colors.title')}
        labelHeader={t('analysis.colors.color')}
        valueHeader={t('analysis.cards')}
        bars={analysis.colors.map((entry) => ({
          key: entry.key,
          label: t(`analysis.colors.keys.${colorKey(entry.key)}`),
          value: entry.count,
          className: `bar-mana-${colorKey(entry.key).toLowerCase()}`,
        }))}
      />
      <BarChart
        title={t('analysis.categories.title')}
        labelHeader={t('analysis.categories.category')}
        valueHeader={t('analysis.cards')}
        bars={analysis.categories.map((entry) => ({ key: entry.key, label: categoryLabel(t, entry.key), value: entry.count }))}
      />
    </div>
  )
}

type DecksT = ReturnType<typeof useTranslation<'decks'>>['t']

function categoryLabel(t: DecksT, key: string): string {
  const known = categoryKey(key)
  return known === 'uncategorized' ? t('analysis.categories.uncategorized') : t(`categories.${known}`)
}

/** The mana sources of each color, compared with the requirement of the most demanding card. */
export function ManaPanel({ deck, analysis }: PanelProps) {
  const { t, i18n } = useTranslation('decks')
  const language = i18n.resolvedLanguage ?? 'en'
  return (
    <div className="analysis-panel">
      <p className="field-hint">{t('analysis.mana.hint', { size: analysis.mana.tableDeckSize })}</p>
      {analysis.mana.colors.length === 0 ? (
        <p>{t('analysis.mana.empty')}</p>
      ) : (
        <div className="table-scroll">
          <table className="table analysis-table">
            <caption className="visually-hidden">{t('analysis.tabs.mana')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('analysis.colors.color')}</th>
                <th scope="col">{t('analysis.mana.symbols')}</th>
                <th scope="col">{t('analysis.mana.landSources')}</th>
                <th scope="col">{t('analysis.mana.otherSources')}</th>
                <th scope="col">{t('analysis.mana.sources')}</th>
                <th scope="col">{t('analysis.mana.required')}</th>
                <th scope="col">{t('analysis.mana.card')}</th>
                <th scope="col">{t('analysis.status')}</th>
              </tr>
            </thead>
            <tbody>
              {analysis.mana.colors.map((color) => {
                const status = manaStatus(color.status)
                const card = color.oracleId ? (deck.cards[color.oracleId]?.name ?? '') : ''
                return (
                  <tr key={color.color}>
                    <th scope="row">{t(`analysis.colors.keys.${colorKey(color.color)}`)}</th>
                    <td>{color.symbols}</td>
                    <td>{color.landSources}</td>
                    <td>{color.otherSources}</td>
                    <td>{formatDecimal(color.sources, language)}</td>
                    <td>{color.required ?? '—'}</td>
                    <td>{card ? t('analysis.mana.cardPips', { card, count: color.pips ?? 0 }) : '—'}</td>
                    <td>
                      <span className={`status status-${statusTone(status)}`}>{t(`analysis.mana.status.${status}`)}</span>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

const landColumns = [1, 2, 3, 4, 5, 6, 7] as const

/** The probability to have at least N lands at each checkpoint, as a table with a tint for each cell. */
export function DrawsPanel({ analysis }: PanelProps) {
  const { t, i18n } = useTranslation('decks')
  const language = i18n.resolvedLanguage ?? 'en'
  return (
    <div className="analysis-panel">
      <p className="field-hint">{t('analysis.draws.hint', { librarySize: analysis.draws.librarySize, lands: analysis.draws.lands })}</p>
      <div className="table-scroll">
        <table className="table analysis-table draws-table">
          <caption className="visually-hidden">{t('analysis.tabs.draws')}</caption>
          <thead>
            <tr>
              <th scope="col">{t('analysis.draws.checkpoint')}</th>
              {landColumns.map((lands) => (
                <th key={lands} scope="col">
                  {t('analysis.draws.atLeast', { count: lands })}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {analysis.draws.checkpoints.map((checkpoint) => (
              <tr key={checkpoint.code}>
                <th scope="row">{t(`analysis.draws.checkpoints.${checkpointCode(checkpoint.code)}`)}</th>
                {landColumns.map((lands) => {
                  const probability = checkpoint.atLeast.find((entry) => entry.lands === lands)?.probability ?? 0
                  return (
                    <td key={lands} className="draws-cell" style={{ '--probability': `${Math.round(probability * 100)}%` } as CSSProperties}>
                      {formatPercent(probability, language)}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

/** The category needs and the indicative bracket of a Commander deck. */
export function CommanderPanel({ deck, analysis }: PanelProps) {
  const { t } = useTranslation('decks')
  return (
    <div className="analysis-panel">
      {analysis.needs && (
        <section aria-labelledby="analysis-needs-title">
          <h3 id="analysis-needs-title">{t('analysis.needs.title')}</h3>
          <div className="table-scroll">
            <table className="table analysis-table">
              <thead>
                <tr>
                  <th scope="col">{t('analysis.categories.category')}</th>
                  <th scope="col">{t('analysis.cards')}</th>
                  <th scope="col">{t('analysis.needs.target')}</th>
                  <th scope="col">{t('analysis.status')}</th>
                </tr>
              </thead>
              <tbody>
                {analysis.needs.map((need) => {
                  const status = needStatus(need.status)
                  return (
                    <tr key={need.category}>
                      <th scope="row">{categoryLabel(t, need.category)}</th>
                      <td>{need.count}</td>
                      <td>
                        {need.target === null
                          ? '—'
                          : t('analysis.needs.range', { target: need.target, min: need.min ?? need.target, max: need.max ?? need.target })}
                      </td>
                      <td>
                        <span className={`status status-${statusTone(status)}`}>
                          {t(`analysis.needs.status.${status}`, { count: Math.abs(need.difference ?? 0) })}
                        </span>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </section>
      )}
      {analysis.bracket && (
        <section aria-labelledby="analysis-bracket-title">
          <h3 id="analysis-bracket-title">{t('analysis.bracket.title')}</h3>
          <p className="analysis-bracket">{t('analysis.bracket.value', { bracket: analysis.bracket.bracket })}</p>
          <p className="field-hint">{t('analysis.bracket.hint')}</p>
          {analysis.bracket.reasons.length === 0 ? (
            <p>{t('analysis.bracket.noReason')}</p>
          ) : (
            <ul className="analysis-reasons">
              {analysis.bracket.reasons.map((reason) => (
                <li key={reason.code}>
                  <strong>{t(`analysis.bracket.reasons.${reasonCode(reason.code)}`)}</strong>
                  {' — '}
                  {t('analysis.bracket.reasonBracket', { bracket: reason.bracket })}
                  {reason.oracleIds.length > 0 && (
                    <>
                      {' '}
                      ({reason.oracleIds.map((oracleId) => deck.cards[oracleId]?.name ?? oracleId).join(', ')})
                    </>
                  )}
                </li>
              ))}
            </ul>
          )}
          {analysis.bracket.unchecked.map((code) => (
            <p key={code} className="alert alert-warning">
              {t(`analysis.bracket.unchecked.${uncheckedCode(code)}`)}
            </p>
          ))}
        </section>
      )}
    </div>
  )
}
