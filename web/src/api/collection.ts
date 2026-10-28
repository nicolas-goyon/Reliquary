/** The kinds of locations that a user creates. Only an assembled deck has the kind `deck`. */
export const locationKinds = ['rack', 'binder', 'box', 'cube'] as const
export type LocationKind = (typeof locationKinds)[number] | 'deck'

/** The advanced fields of the entries, in the order of the forms. */
export const advancedFields = ['finish', 'condition', 'purchasePrice', 'acquiredOn', 'note'] as const
export type AdvancedField = (typeof advancedFields)[number]

export const finishes = ['nonfoil', 'foil', 'etched'] as const
export type Finish = (typeof finishes)[number]

export const conditions = ['mint', 'near_mint', 'excellent', 'good', 'light_played', 'played', 'poor'] as const
export type Condition = (typeof conditions)[number]

/** The language codes of the printings. */
export const languages = ['en', 'es', 'fr', 'de', 'it', 'pt', 'ja', 'ko', 'ru', 'zhs', 'zht', 'he', 'la', 'grc', 'ar', 'sa', 'ph', 'qya'] as const

/** The places outside the collection: the origin of new cards and the destination of removed cards. */
export const addOrigins = ['purchase', 'gift', 'trade', 'nowhere'] as const
export const removeDestinations = ['sale', 'gift', 'trade', 'nowhere'] as const
export type EndpointKind = 'location' | 'purchase' | 'sale' | 'gift' | 'trade' | 'nowhere'

/** A location of the collection, with its sections. */
export interface CollectionLocation {
  id: string
  parentId: string | null
  name: string
  kind: LocationKind
  locked: boolean
  pickable: boolean
  deckId: string | null
  position: number
  cardCount: number
  totalCardCount: number
  createdAt: string
  updatedAt: string
  sections: CollectionLocation[]
}

/** An entry of a location. A disabled advanced field is not in the response. */
export interface CollectionEntry {
  id: string
  locationId: string
  oracleId: string
  name: string
  printingId: string | null
  setCode: string | null
  collectorNumber: string | null
  imageUri: string | null
  language: string
  quantity: number
  finish?: Finish
  condition?: Condition
  purchasePrice?: number
  purchaseCurrency?: string
  acquiredOn?: string
  note?: string
}

export interface EntriesChange {
  batchId: string
  entries: CollectionEntry[]
}

/** One Oracle card of the global view. */
export interface CollectionCard {
  oracleId: string
  name: string
  quantity: number
  imageUri: string | null
  locations: { locationId: string; locationName: string; sectionName: string | null; kind: LocationKind; quantity: number }[]
}

/** A storage rule: a card search that suggests a location. The lowest priority number applies first. */
export interface StorageRule {
  id: string
  locationId: string
  locationName: string
  sectionName: string | null
  query: string
  priority: number
}

/** A token of an assembled deck. */
export interface DeckToken {
  oracleId: string
  name: string
  owned: boolean
}

/** The origin or the destination of a movement. */
export interface MovementEndpoint {
  kind: EndpointKind
  locationId: string | null
  locationName: string | null
}

/** A movement of the history. */
export interface Movement {
  id: string
  batchId: string
  kind: 'add' | 'remove' | 'move'
  origin: MovementEndpoint
  destination: MovementEndpoint
  oracleId: string
  name: string
  printingId: string | null
  language: string
  quantity: number
  inventoryId: string | null
  createdAt: string
}

/** Copies of one card in an inventory. The quantity of a difference is the change. */
export interface InventoryEntry {
  oracleId: string
  name: string
  printingId: string | null
  setCode: string | null
  language: string
  quantity: number
  finish?: Finish
  condition?: Condition
  purchasePrice?: number
  purchaseCurrency?: string
  acquiredOn?: string
  note?: string
}

export interface InventoryDifference {
  key: string
  card: InventoryEntry
  expected: number
  counted: number
}

export interface Inventory {
  id: string
  locationId: string
  status: 'open' | 'applied'
  createdAt: string
  appliedAt: string | null
  cardCount: number
  entries: InventoryEntry[]
  differences: InventoryDifference[]
}

export interface InventorySummary {
  id: string
  status: 'open' | 'applied'
  createdAt: string
  appliedAt: string | null
  cardCount: number
}

/** Gives the value of an origin or a destination in a select: "location:<id>" or the kind. */
export function endpointValue(kind: EndpointKind, locationId?: string): string {
  return kind === 'location' && locationId ? `location:${locationId}` : kind
}

/** Reads the value of an origin or a destination of a select. */
export function parseEndpoint(value: string): { kind: EndpointKind; locationId?: string } {
  const [kind, locationId] = value.split(':')
  return kind === 'location' ? { kind: 'location', locationId } : { kind: kind as EndpointKind }
}

/** Gives the counted card of an entry, with its fields, for a new inventory. */
export function countedCard(entry: Omit<InventoryEntry, 'name' | 'setCode'>, quantity: number): NewEntry {
  const card: NewEntry = { oracleId: entry.oracleId, language: entry.language, quantity }
  if (entry.printingId) {
    card.printingId = entry.printingId
  }
  for (const field of ['finish', 'condition', 'purchasePrice', 'purchaseCurrency', 'acquiredOn', 'note'] as const) {
    if (entry[field] !== undefined) {
      Object.assign(card, { [field]: entry[field] })
    }
  }
  return card
}

export interface CollectionSettings {
  advancedFields: AdvancedField[]
}

/** A card to add, with the optional advanced fields. */
export interface NewEntry {
  oracleId: string
  printingId?: string
  language?: string
  quantity: number
  finish?: Finish
  condition?: Condition
  purchasePrice?: number
  purchaseCurrency?: string
  acquiredOn?: string
  note?: string
}

/** Gives all the locations and sections in the order of the list, with the parent location of each section. */
export function flattenLocations(locations: CollectionLocation[]): { location: CollectionLocation; parent: CollectionLocation | null }[] {
  return locations.flatMap((location) => [{ location, parent: null }, ...location.sections.map((section) => ({ location: section, parent: location }))])
}

/** Gives the name of a location for a list: "Rack 1 / Box A" for a section. */
export function locationLabel(name: string, sectionName?: string | null): string {
  return sectionName ? `${name} / ${sectionName}` : name
}

/** Makes the query of the global view from its filters. Empty filters are not in the query. */
export function cardViewQuery(filters: { q: string; locationId: string; language: string; set: string }, cursor?: string | null): string {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(filters)) {
    if (value.trim()) {
      query.set(key, value.trim())
    }
  }
  if (cursor) {
    query.set('cursor', cursor)
  }
  return query.toString()
}
