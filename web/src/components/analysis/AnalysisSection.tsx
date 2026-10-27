import { useEffect, useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { DeckAnalysis } from '../../api/analysis'
import type { Deck } from '../../api/decks'
import { useAuth } from '../../auth/session'
import { AnalysisTabs } from './AnalysisTabs'

type Loaded = { state: 'loading' } | { state: 'error' } | { state: 'done'; analysis: DeckAnalysis }

/**
 * The analysis section of the deck page. It gets the analysis again for each new version of the deck.
 * A failed request shows a message, and the rest of the page continues to work.
 */
export function AnalysisSection({ deck }: { deck: Deck }) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const titleId = useId()
  const [loaded, setLoaded] = useState<Loaded>({ state: 'loading' })

  useEffect(() => {
    let current = true
    request<DeckAnalysis>(`/decks/${deck.id}/analysis`)
      .then((analysis) => current && setLoaded({ state: 'done', analysis }))
      .catch(() => current && setLoaded({ state: 'error' }))
    return () => {
      current = false
    }
  }, [deck.id, deck.version, request])

  return (
    <section className="analysis" aria-labelledby={titleId}>
      <h2 id={titleId}>{t('analysis.title')}</h2>
      {loaded.state === 'loading' && <p role="status">{t('analysis.loading')}</p>}
      {loaded.state === 'error' && <p className="field-hint">{t('analysis.failed')}</p>}
      {loaded.state === 'done' && <AnalysisTabs context={{ deckId: deck.id, deck, analysis: loaded.analysis }} labelledBy={titleId} />}
    </section>
  )
}
