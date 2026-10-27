import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../api/client'
import type { Deck, Zone } from '../../api/decks'
import { recommendationsDisabledCode, type DeckRecommendations, type RecommendedCard } from '../../api/recommendations'
import { useAuth } from '../../auth/session'
import { Link } from '../../router/Link'
import { CardImage } from '../CardImage'
import { ManaCost } from '../ManaCost'
import './recommendations.css'

/** The zones where the panel adds a recommended card. */
export type RecommendationZone = Extract<Zone, 'main' | 'considering'>

interface RecommendationsPanelProps {
  deck: Deck
  /** Adds one copy of a card to a zone of the deck. */
  onAdd(oracleId: string, zone: RecommendationZone): Promise<void>
}

type Loaded = { state: 'loading' } | { state: 'hidden' } | { state: 'failed' } | { state: 'done'; recommendations: DeckRecommendations }

/** The number of cards that the panel asks for. */
const limit = 30

/**
 * The card recommendations for the commanders of the deck, with the credit of the source.
 * The panel is not visible when the administrator did not enable the recommendations.
 */
export function RecommendationsPanel({ deck, onAdd }: RecommendationsPanelProps) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const [loaded, setLoaded] = useState<Loaded>({ state: 'loading' })
  const [adding, setAdding] = useState<string | null>(null)

  // The panel asks again only when the commanders change. A new card in the deck does not change the recommendations.
  const commanderKey = deck.lines
    .filter((line) => line.zone === 'commander')
    .map((line) => line.oracleId)
    .sort()
    .join(',')

  useEffect(() => {
    let current = true
    request<DeckRecommendations>(`/decks/${deck.id}/recommendations?limit=${limit}`)
      .then((recommendations) => current && setLoaded({ state: 'done', recommendations }))
      .catch((error: unknown) => {
        if (!current) {
          return
        }
        const hidden = error instanceof ApiError && (error.code === recommendationsDisabledCode || error.status === 404)
        setLoaded({ state: hidden ? 'hidden' : 'failed' })
      })
    return () => {
      current = false
    }
  }, [deck.id, commanderKey, request])

  // The panel hides the cards that the deck contains, also the cards that the user added after the request.
  const inDeck = useMemo(() => new Set(deck.lines.map((line) => line.oracleId)), [deck.lines])

  if (loaded.state === 'hidden') {
    return null
  }

  async function add(card: RecommendedCard, zone: RecommendationZone) {
    setAdding(card.oracleId)
    try {
      await onAdd(card.oracleId, zone)
    } finally {
      setAdding(null)
    }
  }

  const cards = loaded.state === 'done' ? loaded.recommendations.cards.filter((card) => !inDeck.has(card.oracleId)) : []
  return (
    <section className="panel recommendations" aria-labelledby="recommendations-title" aria-busy={loaded.state === 'loading'}>
      <h2 id="recommendations-title">{t('recommendations.title')}</h2>
      {loaded.state === 'loading' && <p role="status">{t('recommendations.loading')}</p>}
      {loaded.state === 'failed' && <p className="field-hint">{t('recommendations.unavailable')}</p>}
      {loaded.state === 'done' && (
        <>
          {loaded.recommendations.status === 'no_commander' && <p className="field-hint">{t('recommendations.noCommander')}</p>}
          {loaded.recommendations.status === 'unavailable' && <p className="field-hint">{t('recommendations.unavailable')}</p>}
          {loaded.recommendations.status === 'ok' && cards.length === 0 && <p className="field-hint">{t('recommendations.empty')}</p>}
          {cards.length > 0 && (
            <ul className="recommendation-list">
              {cards.map((card) => (
                <li key={card.oracleId} className="recommendation">
                  <span className="recommendation-name">
                    <Link to={`/cards/${card.oracleId}`}>{card.name}</Link>
                    <span className="recommendation-preview" aria-hidden="true">
                      <CardImage src={card.imageUri ?? undefined} alt="" />
                    </span>
                    {card.typeLine && <span className="recommendation-type">{card.typeLine}</span>}
                  </span>
                  <ManaCost cost={card.manaCost} />
                  <span className="recommendation-actions">
                    <button
                      type="button"
                      className="button-small"
                      disabled={adding !== null}
                      aria-label={t('recommendations.addToMainLabel', { name: card.name })}
                      onClick={() => void add(card, 'main')}
                    >
                      {t('recommendations.addButton')}
                    </button>
                    <button
                      type="button"
                      className="button-small"
                      disabled={adding !== null}
                      aria-label={t('recommendations.addToConsideringLabel', { name: card.name })}
                      onClick={() => void add(card, 'considering')}
                    >
                      {t('recommendations.considerButton')}
                    </button>
                  </span>
                </li>
              ))}
            </ul>
          )}
          <p className="recommendation-credit">
            {t('recommendations.credit')}{' '}
            <a href={loaded.recommendations.source.url} target="_blank" rel="noopener noreferrer">
              {loaded.recommendations.source.name}
            </a>
          </p>
        </>
      )}
    </section>
  )
}
