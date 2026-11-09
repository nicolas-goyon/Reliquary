/**
 * The table engine of the deck testing modes: zones, draw, shuffle, tokens, counters and flip.
 * The engine has no game rules. It is a pure function: the same state and the same action always give the same new state.
 * ADR 0040 gives the reasons.
 */

export const tableZones = ['library', 'hand', 'battlefield', 'graveyard', 'exile', 'command'] as const
export type TableZone = (typeof tableZones)[number]

/** The counters of a player. The players enter them by hand: the engine has no game rules. */
export const playerCounters = ['poison', 'energy', 'experience'] as const
export type PlayerCounter = (typeof playerCounters)[number]

/** The number of cards of an opening hand. */
export const openingHandSize = 7

export interface TableCard {
  /** The ID of this copy on the table. */
  id: string
  oracleId: string
  name: string
  imageUri: string | null
  isLand: boolean
  manaValue: number
  isToken: boolean
  tapped: boolean
  /** Whether the card shows its back. */
  faceDown: boolean
  counters: number
}

export interface TablePlayer {
  id: string
  name: string
  life: number
  /** The commander damage that this player took, by the ID of the other player. */
  commanderDamage: Record<string, number>
  counters: Record<PlayerCounter, number>
  mulligans: number
  /** The number of cards that the player must put at the bottom of the library after a mulligan. */
  cardsToBottom: number
  zones: Record<TableZone, TableCard[]>
}

export interface TableState {
  players: TablePlayer[]
  /** Whether the game uses the commander damage. It is true for a game of the Commander format or for a deck with a commander. */
  commander: boolean
  activePlayerId: string
  turn: number
  /** The state of the random number generator. */
  seed: number
  /** The next number of the IDs of the tokens. */
  nextTokenId: number
}

export interface PlayerSetup {
  id: string
  name: string
  /** The cards of the library, with no order. */
  library: Omit<TableCard, 'tapped' | 'faceDown' | 'counters' | 'isToken'>[]
  /** The cards that start in the command zone. */
  command: Omit<TableCard, 'tapped' | 'faceDown' | 'counters' | 'isToken'>[]
}

export type TableAction =
  | { type: 'draw'; playerId: string; count?: number }
  | { type: 'shuffle'; playerId: string }
  | { type: 'mulligan'; playerId: string }
  | { type: 'bottom'; playerId: string; cardId: string }
  | { type: 'move'; playerId: string; cardId: string; to: TableZone; position?: 'top' | 'bottom' }
  | { type: 'tap'; playerId: string; cardId: string }
  | { type: 'untapAll'; playerId: string }
  | { type: 'flip'; playerId: string; cardId: string }
  | { type: 'counter'; playerId: string; cardId: string; change: number }
  | { type: 'token'; playerId: string; name: string }
  | { type: 'life'; playerId: string; change: number }
  | { type: 'commanderDamage'; playerId: string; fromPlayerId: string; change: number }
  | { type: 'playerCounter'; playerId: string; counter: PlayerCounter; change: number }
  | { type: 'nextTurn' }

/** The mulberry32 generator. A seed gives a repeatable game, so a test can check a shuffle. */
function nextRandom(seed: number): { value: number; seed: number } {
  const next = (seed + 0x6d2b79f5) | 0
  let mixed = Math.imul(next ^ (next >>> 15), 1 | next)
  mixed = (mixed + Math.imul(mixed ^ (mixed >>> 7), 61 | mixed)) ^ mixed
  return { value: ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296, seed: next }
}

/** The Fisher-Yates shuffle. */
function shuffled<T>(items: readonly T[], seed: number): { items: T[]; seed: number } {
  const result = [...items]
  let current = seed
  for (let index = result.length - 1; index > 0; index--) {
    const random = nextRandom(current)
    current = random.seed
    const other = Math.floor(random.value * (index + 1))
    ;[result[index], result[other]] = [result[other], result[index]]
  }
  return { items: result, seed: current }
}

function freshCard(card: PlayerSetup['library'][number]): TableCard {
  return { ...card, isToken: false, tapped: false, faceDown: false, counters: 0 }
}

/** Starts a table: each library has a random order, and each player has an opening hand. */
export function createTable(setups: readonly PlayerSetup[], seed: number, startingLife: number, options: { commander?: boolean } = {}): TableState {
  let current = seed
  const players = setups.map((setup): TablePlayer => {
    const library = shuffled(setup.library.map(freshCard), current)
    current = library.seed
    return {
      id: setup.id,
      name: setup.name,
      life: startingLife,
      commanderDamage: {},
      counters: { poison: 0, energy: 0, experience: 0 },
      mulligans: 0,
      cardsToBottom: 0,
      zones: {
        library: library.items.slice(openingHandSize),
        hand: library.items.slice(0, openingHandSize),
        battlefield: [],
        graveyard: [],
        exile: [],
        command: setup.command.map(freshCard),
      },
    }
  })
  return { players, commander: options.commander ?? false, activePlayerId: players[0]?.id ?? '', turn: 1, seed: current, nextTokenId: 1 }
}

function updatePlayer(state: TableState, playerId: string, update: (player: TablePlayer) => TablePlayer): TableState {
  return { ...state, players: state.players.map((player) => (player.id === playerId ? update(player) : player)) }
}

function updateCard(player: TablePlayer, cardId: string, update: (card: TableCard) => TableCard): TablePlayer {
  const zones = { ...player.zones }
  for (const zone of tableZones) {
    if (zones[zone].some((card) => card.id === cardId)) {
      zones[zone] = zones[zone].map((card) => (card.id === cardId ? update(card) : card))
    }
  }
  return { ...player, zones }
}

/** Gets the zone of a card, or undefined when the player does not have the card. */
export function zoneOf(player: TablePlayer, cardId: string): TableZone | undefined {
  return tableZones.find((zone) => player.zones[zone].some((card) => card.id === cardId))
}

function moveCard(player: TablePlayer, cardId: string, to: TableZone, position: 'top' | 'bottom'): TablePlayer {
  const from = zoneOf(player, cardId)
  const card = from && player.zones[from].find((item) => item.id === cardId)
  if (!from || !card) {
    return player
  }
  const without = { ...player.zones, [from]: player.zones[from].filter((item) => item.id !== cardId) }
  // A token stops to exist when it leaves the battlefield.
  if (card.isToken && to !== 'battlefield') {
    return { ...player, zones: without }
  }
  const moved = to === 'battlefield' ? card : { ...card, tapped: false, faceDown: false, counters: 0 }
  const target = position === 'top' ? [moved, ...without[to]] : [...without[to], moved]
  return { ...player, zones: { ...without, [to]: target } }
}

function drawCards(player: TablePlayer, count: number): TablePlayer {
  const drawn = player.zones.library.slice(0, count)
  return {
    ...player,
    zones: { ...player.zones, library: player.zones.library.slice(drawn.length), hand: [...player.zones.hand, ...drawn] },
  }
}

/** The London mulligan: the hand goes back, the library gets a shuffle, and the player draws 7 cards. The player then puts one card at the bottom for each mulligan. */
function mulligan(state: TableState, playerId: string): TableState {
  const player = state.players.find((item) => item.id === playerId)
  if (!player) {
    return state
  }
  const library = shuffled([...player.zones.library, ...player.zones.hand], state.seed)
  const mulligans = player.mulligans + 1
  const returned = { ...player, zones: { ...player.zones, library: library.items, hand: [] } }
  const next = { ...drawCards(returned, openingHandSize), mulligans, cardsToBottom: Math.min(mulligans, openingHandSize) }
  return { ...updatePlayer(state, playerId, () => next), seed: library.seed }
}

/** Applies one action. An action on a card or a player that does not exist does not change the state. */
export function applyAction(state: TableState, action: TableAction): TableState {
  switch (action.type) {
    case 'draw':
      return updatePlayer(state, action.playerId, (player) => drawCards(player, action.count ?? 1))
    case 'shuffle': {
      const player = state.players.find((item) => item.id === action.playerId)
      if (!player) {
        return state
      }
      const library = shuffled(player.zones.library, state.seed)
      return { ...updatePlayer(state, action.playerId, (item) => ({ ...item, zones: { ...item.zones, library: library.items } })), seed: library.seed }
    }
    case 'mulligan':
      return mulligan(state, action.playerId)
    case 'bottom':
      return updatePlayer(state, action.playerId, (player) => {
        if (player.cardsToBottom === 0 || zoneOf(player, action.cardId) !== 'hand') {
          return player
        }
        return { ...moveCard(player, action.cardId, 'library', 'bottom'), cardsToBottom: player.cardsToBottom - 1 }
      })
    case 'move':
      return updatePlayer(state, action.playerId, (player) => moveCard(player, action.cardId, action.to, action.position ?? (action.to === 'library' ? 'top' : 'bottom')))
    case 'tap':
      return updatePlayer(state, action.playerId, (player) => updateCard(player, action.cardId, (card) => ({ ...card, tapped: !card.tapped })))
    case 'untapAll':
      return updatePlayer(state, action.playerId, (player) => ({
        ...player,
        zones: { ...player.zones, battlefield: player.zones.battlefield.map((card) => ({ ...card, tapped: false })) },
      }))
    case 'flip':
      return updatePlayer(state, action.playerId, (player) => updateCard(player, action.cardId, (card) => ({ ...card, faceDown: !card.faceDown })))
    case 'counter':
      return updatePlayer(state, action.playerId, (player) => updateCard(player, action.cardId, (card) => ({ ...card, counters: Math.max(0, card.counters + action.change) })))
    case 'token': {
      const token: TableCard = {
        id: `token-${state.nextTokenId}`,
        oracleId: '',
        name: action.name,
        imageUri: null,
        isLand: false,
        manaValue: 0,
        isToken: true,
        tapped: false,
        faceDown: false,
        counters: 0,
      }
      const next = updatePlayer(state, action.playerId, (player) => ({ ...player, zones: { ...player.zones, battlefield: [...player.zones.battlefield, token] } }))
      return { ...next, nextTokenId: state.nextTokenId + 1 }
    }
    case 'life':
      return updatePlayer(state, action.playerId, (player) => ({ ...player, life: player.life + action.change }))
    case 'commanderDamage':
      if (!state.commander) {
        return state
      }
      return updatePlayer(state, action.playerId, (player) => ({
        ...player,
        commanderDamage: { ...player.commanderDamage, [action.fromPlayerId]: Math.max(0, (player.commanderDamage[action.fromPlayerId] ?? 0) + action.change) },
      }))
    case 'playerCounter':
      return updatePlayer(state, action.playerId, (player) => ({
        ...player,
        counters: { ...player.counters, [action.counter]: Math.max(0, player.counters[action.counter] + action.change) },
      }))
    case 'nextTurn': {
      const index = state.players.findIndex((player) => player.id === state.activePlayerId)
      const next = state.players[(index + 1) % state.players.length]
      if (!next) {
        return state
      }
      const untapped = applyAction(state, { type: 'untapAll', playerId: next.id })
      const turn = state.turn + 1
      return applyAction({ ...untapped, activePlayerId: next.id, turn }, { type: 'draw', playerId: next.id })
    }
  }
}

export interface HandStatistics {
  samples: number
  averageLands: number
  /** The percentage of hands with 2 to 4 lands. */
  keepablePercent: number
  /** The percentage of hands for each number of lands, from 0 to 7. */
  landsPercent: number[]
}

/** Gives the statistics of many random opening hands. */
export function handStatistics(cards: readonly Pick<TableCard, 'isLand'>[], samples: number, seed: number): HandStatistics {
  const counts = Array.from({ length: openingHandSize + 1 }, () => 0)
  let current = seed
  for (let sample = 0; sample < samples; sample++) {
    const deck = shuffled(cards, current)
    current = deck.seed
    counts[deck.items.slice(0, openingHandSize).filter((card) => card.isLand).length]++
  }
  const percent = (value: number) => (samples === 0 ? 0 : (value * 100) / samples)
  return {
    samples,
    averageLands: samples === 0 ? 0 : counts.reduce((sum, count, lands) => sum + count * lands, 0) / samples,
    keepablePercent: percent(counts[2] + counts[3] + counts[4]),
    landsPercent: counts.map(percent),
  }
}
