import { useTranslation } from 'react-i18next'
import { comboUrl, commanderSpellbookUrl, type DeckAnalysis, type DeckCombo } from '../../api/analysis'
import type { Deck } from '../../api/decks'

interface PanelProps {
  deck: Deck
  analysis: DeckAnalysis
}

/** The combos of Commander Spellbook in the deck, and the combos that need one more card. */
export function CombosPanel({ analysis }: PanelProps) {
  const { t } = useTranslation('decks')
  const { combos } = analysis
  return (
    <div className="analysis-panel">
      {!combos.available ? (
        <p className="alert alert-warning">{t('analysis.combos.unavailable')}</p>
      ) : (
        <>
          <section aria-labelledby="analysis-combos-complete-title">
            <h3 id="analysis-combos-complete-title">{t('analysis.combos.complete', { count: combos.complete.length })}</h3>
            {combos.complete.length === 0 ? (
              <p>{t('analysis.combos.noComplete')}</p>
            ) : (
              <ComboList combos={combos.complete} />
            )}
          </section>
          <section aria-labelledby="analysis-combos-near-title">
            <h3 id="analysis-combos-near-title">{t('analysis.combos.near', { count: combos.nearCount })}</h3>
            {combos.nearCount === 0 ? (
              <p>{t('analysis.combos.noNear')}</p>
            ) : (
              <>
                {combos.near.length < combos.nearCount && (
                  <p className="field-hint">{t('analysis.combos.nearLimit', { shown: combos.near.length, total: combos.nearCount })}</p>
                )}
                <ComboList combos={combos.near} />
              </>
            )}
          </section>
        </>
      )}
      <p className="field-hint combo-credit">
        {t('analysis.combos.credit')}{' '}
        <a href={commanderSpellbookUrl} target="_blank" rel="noreferrer">
          Commander Spellbook
        </a>
      </p>
    </div>
  )
}

function ComboList({ combos }: { combos: DeckCombo[] }) {
  const { t } = useTranslation('decks')
  return (
    <ul className="combo-list">
      {combos.map((combo) => {
        const cards = combo.cards.map((card) => card.name).join(' + ')
        const missing = combo.cards.find((card) => card.oracleId === combo.missingOracleId)
        return (
          <li key={combo.id} className="combo">
            <p className="combo-cards">
              {combo.cards.map((card, index) => (
                <span key={card.oracleId}>
                  {index > 0 && ' + '}
                  <span className={card.inDeck ? undefined : 'combo-missing'}>
                    {card.quantity > 1 ? t('analysis.combos.copies', { card: card.name, quantity: card.quantity }) : card.name}
                  </span>
                </span>
              ))}
            </p>
            {missing && <p className="combo-missing-card">{t('analysis.combos.missing', { card: missing.name })}</p>}
            <p className="combo-results">
              {t('analysis.combos.results')} <span lang="en">{combo.results.join(' · ')}</span>
            </p>
            <a href={comboUrl(combo)} target="_blank" rel="noreferrer" aria-label={t('analysis.combos.openLabel', { cards })}>
              {t('analysis.combos.open')}
            </a>
          </li>
        )
      })}
    </ul>
  )
}
