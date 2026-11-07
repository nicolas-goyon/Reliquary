/** The types of the public decks. See docs/architecture/decks.md. */

export interface AuthorSummary {
  id: string
  displayName: string
}

export interface PublicDeck {
  id: string
  name: string
  format: string
  colorIdentity: string
  commanders: { oracleId: string; name: string; imageUri: string | null }[]
  author: AuthorSummary
  cardCount: number
  copyCount: number
  favoriteCount: number
  updatedAt: string
}

export interface Author {
  id: string
  displayName: string
  deckCount: number
  followerCount: number
  following: boolean
  /** Whether an administrator hid the profile. Only the author gets a hidden profile. */
  hidden?: boolean
}

export interface PublicDeckSocial {
  public: boolean
  favorite: boolean
  favoriteCount: number
  copyCount: number
  author: AuthorSummary | null
}

export const publicDeckSorts = ['recent', 'popular'] as const
export type PublicDeckSort = (typeof publicDeckSorts)[number]

/** The filters of the search of public decks. */
export interface PublicDeckFilters {
  q: string
  format: string
  commander: string
  card: string
  colors: string
  sort: PublicDeckSort
}

export const emptyPublicDeckFilters: PublicDeckFilters = { q: '', format: '', commander: '', card: '', colors: '', sort: 'recent' }

/** Gets the query of the search for the filters. Empty filters are not in the query. */
export function publicDeckQuery(filters: PublicDeckFilters, author?: string): string {
  const query = new URLSearchParams({ limit: '50' })
  for (const key of ['q', 'format', 'commander', 'card', 'colors'] as const) {
    const value = filters[key].trim()
    if (value) {
      query.set(key, value)
    }
  }
  if (filters.sort !== 'recent') {
    query.set('sort', filters.sort)
  }
  if (author) {
    query.set('author', author)
  }
  return query.toString()
}

/** Reads the filters from the query of the page address. */
export function filtersFromSearch(search: string): PublicDeckFilters {
  const query = new URLSearchParams(search)
  const sort = query.get('sort')
  return {
    q: query.get('q') ?? '',
    format: query.get('format') ?? '',
    commander: query.get('commander') ?? '',
    card: query.get('card') ?? '',
    colors: query.get('colors') ?? '',
    sort: sort === 'popular' ? 'popular' : 'recent',
  }
}
