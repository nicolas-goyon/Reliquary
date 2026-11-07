/** The status of the recommendations of a deck. See GET /decks/{id}/recommendations. */
export type RecommendationStatus = 'ok' | 'no_commander' | 'unavailable'

/** The error code when the administrator did not enable the recommendations. */
export const recommendationsDisabledCode = 'recommendations_disabled'

export interface RecommendedCard {
  oracleId: string
  name: string
  score: number
  manaCost: string | null
  typeLine: string | null
  imageUri: string | null
}

export interface DeckRecommendations {
  status: RecommendationStatus
  /** The source of the recommendations. The interface must show its credit with the list. */
  source: { name: string; url: string }
  commanders: string[]
  cards: RecommendedCard[]
}
