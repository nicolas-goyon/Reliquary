import type { categories } from './decks'

export type Category = (typeof categories)[number]

/** The sources of a suggestion of auto-tag. */
export const autoTagSources = ['user_rule', 'oracle_tag', 'text_rule', 'none'] as const
export type AutoTagSource = (typeof autoTagSources)[number]

/** The reason for one suggested category. */
export interface AutoTagReason {
  category: string
  source: string
  /** The slug of the Oracle tag, or the code of the text rule. Null for a user rule. */
  detail: string | null
}

/** The suggestion of auto-tag for one deck line. */
export interface AutoTagLine {
  lineId: string
  oracleId: string
  name: string
  zone: string
  categories: string[]
  suggested: string[]
  source: string
  reasons: AutoTagReason[]
  /** Whether the suggested categories differ from the current categories. */
  changes: boolean
}

/** The response of GET /decks/{id}/auto-tag. */
export interface AutoTagResponse {
  deckId: string
  version: number
  lines: AutoTagLine[]
}

/** The body of POST /decks/{id}/auto-tag. */
export interface ApplyAutoTagRequest {
  expectedVersion: number
  lines: { lineId: string; categories: string[] }[]
}

/** Gets the known source of a suggestion. */
export function sourceKey(source: string): AutoTagSource {
  return autoTagSources.find((known) => known === source) ?? 'none'
}
