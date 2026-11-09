/**
 * The conformance vectors of the table engine.
 * Each vector is a seeded random game: the setups, the initial state, and the state after each action.
 * The C# port of the engine (Reliquary.Playtest) replays the vectors and must give the same states. ADR 0047 gives the reasons.
 * The vectors are in tests/vectors/table-engine. Run "npm run vectors" to write them again after a change to engine.ts.
 */
import { applyAction, createTable, playerCounters, tableZones, type PlayerSetup, type TableAction, type TableState } from './engine'

type Json = null | boolean | number | string | Json[] | { [key: string]: Json }

/** A change of the state: the path of a value in the JSON of the state, and its new value. */
export type StateChange = [path: (string | number)[], value: Json]

export interface EngineVector {
  name: string
  seed: number
  startingLife: number
  commander: boolean
  setups: PlayerSetup[]
  /** The state that createTable gives. */
  initial: TableState
  /** The steps. Each step has the changes from the state of the step before. The changes give the full state after each action. */
  steps: { action: TableAction; changes: StateChange[] }[]
  /** The state after the last action. */
  final: TableState
}

interface VectorSpec {
  name: string
  players: number
  commander: boolean
  /** The seed of the engine. */
  seed: number
  /** The seed of the random choice of the actions. */
  script: number
  libraryCards: number
  actions: number
  /** The actions of the script. Without them, the actions after the opening actions are random. */
  scripted?: ((state: TableState) => TableAction)[]
}

const hand = (state: TableState, player: number, index = 0) => state.players[player].zones.hand[index]?.id ?? 'none'
const field = (state: TableState, player: number) => state.players[player].zones.battlefield.at(-1)?.id ?? 'none'
const library = (state: TableState, player: number) => state.players[player].zones.library[0]?.id ?? 'none'

/** The actions of the edge cases of the cards: each branch and each limit of engine.ts, in a fixed order. */
const cardEdgeCases: ((state: TableState) => TableAction)[] = [
  // Positions of a move, and the default position of each zone.
  (state) => ({ type: 'move', playerId: 'p0', cardId: hand(state, 0), to: 'library' }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: hand(state, 0), to: 'library', position: 'bottom' }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: library(state, 0), to: 'graveyard' }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: hand(state, 0), to: 'graveyard', position: 'top' }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: hand(state, 0), to: 'command', position: 'top' }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: hand(state, 0), to: 'hand', position: 'top' }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: hand(state, 1), to: 'hand' }),
  // A card on the battlefield keeps its marks when it moves on the battlefield, and loses them in another zone.
  (state) => ({ type: 'move', playerId: 'p0', cardId: hand(state, 0), to: 'battlefield' }),
  (state) => ({ type: 'tap', playerId: 'p0', cardId: field(state, 0) }),
  (state) => ({ type: 'counter', playerId: 'p0', cardId: field(state, 0), change: 2 }),
  (state) => ({ type: 'counter', playerId: 'p0', cardId: field(state, 0), change: -5 }),
  (state) => ({ type: 'counter', playerId: 'p0', cardId: field(state, 0), change: 4 }),
  (state) => ({ type: 'flip', playerId: 'p0', cardId: field(state, 0) }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: field(state, 0), to: 'battlefield', position: 'top' }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: field(state, 0), to: 'library', position: 'bottom' }),
  // A tap, a flip and a counter work in each zone, not only on the battlefield.
  (state) => ({ type: 'tap', playerId: 'p0', cardId: hand(state, 0) }),
  (state) => ({ type: 'flip', playerId: 'p0', cardId: library(state, 0) }),
  (state) => ({ type: 'counter', playerId: 'p0', cardId: hand(state, 1), change: 1 }),
  // An action on a card of the other player does not change the state.
  (state) => ({ type: 'tap', playerId: 'p0', cardId: hand(state, 1) }),
  // Tokens: the IDs go up, and a token that leaves the battlefield stops to exist.
  () => ({ type: 'token', playerId: 'p1', name: 'Goblin' }),
  () => ({ type: 'token', playerId: 'nobody', name: 'Ghost' }),
  () => ({ type: 'token', playerId: 'p0', name: '' }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: field(state, 0), to: 'hand' }),
  (state) => ({ type: 'tap', playerId: 'p1', cardId: field(state, 1) }),
  (state) => ({ type: 'move', playerId: 'p1', cardId: field(state, 1), to: 'library', position: 'top' }),
  // Draws: a count of 0, a negative count (slice from the end) and more cards than the library.
  () => ({ type: 'draw', playerId: 'p0', count: 0 }),
  () => ({ type: 'draw', playerId: 'p0', count: -2 }),
  () => ({ type: 'draw', playerId: 'p0', count: 50 }),
  () => ({ type: 'draw', playerId: 'p0' }),
  () => ({ type: 'shuffle', playerId: 'p0' }),
  // Mulligans: the number of cards to put at the bottom stops at 7, and a bottom needs a card of the hand.
  () => ({ type: 'mulligan', playerId: 'p1' }),
  (state) => ({ type: 'bottom', playerId: 'p1', cardId: library(state, 1) }),
  (state) => ({ type: 'bottom', playerId: 'p1', cardId: hand(state, 1) }),
  (state) => ({ type: 'bottom', playerId: 'p1', cardId: hand(state, 1) }),
  (state) => ({ type: 'bottom', playerId: 'p0', cardId: hand(state, 0) }),
  ...Array.from({ length: 7 }, () => () => ({ type: 'mulligan', playerId: 'p1' }) as TableAction),
  (state) => ({ type: 'bottom', playerId: 'p1', cardId: hand(state, 1, 3) }),
]

/** The actions of the edge cases of the players and the turns. */
const playerEdgeCases: ((state: TableState) => TableAction)[] = [
  // Life points with no limit, commander damage with a limit of 0, the order of the keys, and damage from the same player.
  () => ({ type: 'life', playerId: 'p0', change: -45 }),
  () => ({ type: 'life', playerId: 'p0', change: 2 }),
  () => ({ type: 'commanderDamage', playerId: 'p0', fromPlayerId: 'p1', change: 5 }),
  () => ({ type: 'commanderDamage', playerId: 'p0', fromPlayerId: 'p0', change: 3 }),
  () => ({ type: 'commanderDamage', playerId: 'p0', fromPlayerId: 'p1', change: 2 }),
  () => ({ type: 'commanderDamage', playerId: 'p0', fromPlayerId: 'p1', change: -10 }),
  () => ({ type: 'commanderDamage', playerId: 'p0', fromPlayerId: 'unknown', change: -1 }),
  () => ({ type: 'commanderDamage', playerId: 'nobody', fromPlayerId: 'p1', change: 1 }),
  // Player counters with a limit of 0.
  () => ({ type: 'playerCounter', playerId: 'p1', counter: 'poison', change: 4 }),
  () => ({ type: 'playerCounter', playerId: 'p1', counter: 'energy', change: 3 }),
  () => ({ type: 'playerCounter', playerId: 'p1', counter: 'experience', change: 2 }),
  () => ({ type: 'playerCounter', playerId: 'p1', counter: 'energy', change: -5 }),
  () => ({ type: 'playerCounter', playerId: 'p1', counter: 'poison', change: -6 }),
  () => ({ type: 'playerCounter', playerId: 'p1', counter: 'experience', change: -3 }),
  // The next turn untaps only the battlefield of the next player, and the turn goes back to the first player.
  (state) => ({ type: 'move', playerId: 'p1', cardId: hand(state, 1), to: 'battlefield' }),
  (state) => ({ type: 'tap', playerId: 'p1', cardId: field(state, 1) }),
  (state) => ({ type: 'move', playerId: 'p0', cardId: hand(state, 0), to: 'battlefield' }),
  (state) => ({ type: 'tap', playerId: 'p0', cardId: field(state, 0) }),
  () => ({ type: 'nextTurn' }),
  () => ({ type: 'untapAll', playerId: 'nobody' }),
  () => ({ type: 'nextTurn' }),
  (state) => ({ type: 'tap', playerId: 'p0', cardId: field(state, 0) }),
  () => ({ type: 'untapAll', playerId: 'p0' }),
]

/** The specifications of the vectors. Keep the total size of the files below 400 KB. */
export const vectorSpecs: readonly VectorSpec[] = [
  { name: '2-players', players: 2, commander: false, seed: 1, script: 101, libraryCards: 11, actions: 34 },
  { name: '2-players-commander', players: 2, commander: true, seed: 4294967295, script: 102, libraryCards: 11, actions: 34 },
  { name: '3-players', players: 3, commander: false, seed: 2147483648, script: 103, libraryCards: 9, actions: 32 },
  { name: '3-players-commander', players: 3, commander: true, seed: 0, script: 104, libraryCards: 9, actions: 32 },
  { name: '4-players', players: 4, commander: false, seed: 3735928559, script: 105, libraryCards: 8, actions: 30 },
  { name: '4-players-commander', players: 4, commander: true, seed: 123456789, script: 106, libraryCards: 9, actions: 32 },
  // A large library and many shuffles check the random number generator.
  { name: '2-players-shuffles', players: 2, commander: false, seed: 2654435769, script: 107, libraryCards: 40, actions: 6 },
  // The actions of a script: each branch and each limit of engine.ts.
  { name: '2-players-edge-cases', players: 2, commander: true, seed: 2023, script: 109, libraryCards: 10, actions: cardEdgeCases.length + playerEdgeCases.length, scripted: [...cardEdgeCases, ...playerEdgeCases] },
  { name: '2-players-edge-cases-no-commander', players: 2, commander: false, seed: 77, script: 110, libraryCards: 8, actions: playerEdgeCases.length, scripted: playerEdgeCases },
  // A library of 1 card gives no step of the shuffle, so the seed of the state stays unsigned.
  { name: '2-players-unsigned-seed', players: 2, commander: true, seed: 4294967295, script: 108, libraryCards: 1, actions: 12 },
]

/** A mulberry32 generator for the choice of the actions. It is separate from the generator of the engine. */
function scriptRandom(seed: number): () => number {
  let state = seed | 0
  return () => {
    state = (state + 0x6d2b79f5) | 0
    let mixed = Math.imul(state ^ (state >>> 15), 1 | state)
    mixed = (mixed + Math.imul(mixed ^ (mixed >>> 7), 61 | mixed)) ^ mixed
    return ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296
  }
}

const manaValues = [0, 1, 2, 3, 5, 0.5, 2.5]

function setups(spec: VectorSpec, random: () => number): PlayerSetup[] {
  const card = (playerId: string, index: number) => {
    const oracle = Math.floor(random() * 20)
    return {
      id: `${playerId}-${index}`,
      oracleId: `o${oracle}`,
      name: `C${oracle}`,
      imageUri: oracle % 3 === 0 ? null : `https://i.example/${oracle}`,
      isLand: oracle % 4 === 0,
      manaValue: manaValues[oracle % manaValues.length],
    }
  }
  return Array.from({ length: spec.players }, (_, player) => {
    const id = `p${player}`
    const library = Array.from({ length: spec.libraryCards }, (_, index) => card(id, index))
    const command = spec.commander || player === 0 ? [card(id, spec.libraryCards)] : []
    return { id, name: `Player ${player}`, library, command }
  })
}

/** Chooses one action. The weights give each action type, and some actions on unknown players and cards. */
function chooseAction(state: TableState, random: () => number): TableAction {
  const pick = <T>(items: readonly T[]): T => items[Math.floor(random() * items.length)]
  const players = state.players
  const player = random() < 0.05 ? undefined : pick(players)
  const playerId = player?.id ?? 'nobody'
  const anyCard = (): string => {
    const roll = random()
    if (roll < 0.04) {
      return 'none'
    }
    // An ID of a card of a different player: the engine does not change the state.
    const owner = roll < 0.08 ? pick(players) : player
    const cards = owner ? tableZones.flatMap((zone) => owner.zones[zone]) : []
    return cards.length === 0 ? 'none' : pick(cards).id
  }
  const fieldCard = (): string => {
    const cards = player?.zones.battlefield ?? []
    return cards.length === 0 || random() < 0.2 ? anyCard() : pick(cards).id
  }
  const handCard = (): string => {
    const cards = player?.zones.hand ?? []
    return cards.length === 0 || random() < 0.15 ? anyCard() : pick(cards).id
  }
  const marked = player?.zones.battlefield.filter((card) => card.tapped || card.faceDown || card.counters > 0 || card.isToken) ?? []
  const change = () => pick([-3, -2, -1, 1, 2, 4, 7])
  const actions: [number, () => TableAction][] = [
    [8, () => ({ type: 'draw', playerId, ...(random() < 0.5 ? {} : { count: pick([0, 1, 2, 3, 20, -1]) }) })],
    [3, () => ({ type: 'shuffle', playerId })],
    [3, () => ({ type: 'mulligan', playerId })],
    [(player?.cardsToBottom ?? 0) > 0 ? 25 : 2, () => ({ type: 'bottom', playerId, cardId: handCard() })],
    [
      16,
      () => {
        const roll = random()
        const cardId = roll < 0.45 ? handCard() : roll < 0.8 ? fieldCard() : anyCard()
        const to = roll >= 0.45 && roll < 0.8 ? pick(tableZones) : random() < 0.6 ? 'battlefield' : pick(tableZones)
        const position = pick([undefined, 'top', 'bottom'] as const)
        return { type: 'move', playerId, cardId, to, ...(position ? { position } : {}) }
      },
    ],
    // A card or a token of the battlefield leaves it with its marks, so the vectors check that the engine clears the marks and removes the token.
    [
      marked.length > 0 ? 8 : 0,
      () => ({ type: 'move', playerId, cardId: pick(marked).id, to: pick(tableZones.filter((zone) => zone !== 'battlefield')), ...(random() < 0.5 ? { position: 'top' } : {}) }),
    ],
    [8, () => ({ type: 'tap', playerId, cardId: random() < 0.85 ? fieldCard() : anyCard() })],
    [player?.zones.battlefield.some((card) => card.tapped) ? 10 : 1, () => ({ type: 'untapAll', playerId })],
    [5, () => ({ type: 'flip', playerId, cardId: random() < 0.85 ? fieldCard() : anyCard() })],
    [6, () => ({ type: 'counter', playerId, cardId: random() < 0.85 ? fieldCard() : anyCard(), change: change() })],
    [5, () => ({ type: 'token', playerId, name: pick(['Goblin', 'Soldier', 'Treasure', 'Élémentaire 1/1']) })],
    [6, () => ({ type: 'life', playerId, change: change() })],
    [5, () => ({ type: 'commanderDamage', playerId, fromPlayerId: random() < 0.1 ? 'nobody' : pick(players).id, change: change() })],
    [6, () => ({ type: 'playerCounter', playerId, counter: pick(playerCounters), change: change() })],
    [6, () => ({ type: 'nextTurn' })],
  ]
  const total = actions.reduce((sum, [weight]) => sum + weight, 0)
  let roll = random() * total
  for (const [weight, make] of actions) {
    roll -= weight
    if (roll < 0) {
      return make()
    }
  }
  return actions[0][1]()
}

function isObject(value: Json): value is { [key: string]: Json } {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * Gives the changes from one JSON value to another.
 * The function goes into the objects and into the arrays of the same length. It replaces the other arrays and values.
 * The table engine does not remove a property, so a change only sets a value.
 */
export function stateChanges(before: Json, after: Json, path: (string | number)[] = []): StateChange[] {
  if (isObject(before) && isObject(after)) {
    return Object.keys(after).flatMap((key) => (key in before ? stateChanges(before[key], after[key], [...path, key]) : [[[...path, key], after[key]] as StateChange]))
  }
  if (Array.isArray(before) && Array.isArray(after) && before.length === after.length) {
    return after.flatMap((item, index) => stateChanges(before[index], item, [...path, index]))
  }
  return JSON.stringify(before) === JSON.stringify(after) ? [] : [[path, after]]
}

const asJson = (state: TableState) => JSON.parse(JSON.stringify(state)) as Json

/** Plays the game of one specification. */
export function generateVector(spec: VectorSpec): EngineVector {
  const random = scriptRandom(spec.script)
  const playerSetups = setups(spec, random)
  const startingLife = spec.commander ? 40 : 20
  const initial = createTable(playerSetups, spec.seed, startingLife, { commander: spec.commander })
  const steps: EngineVector['steps'] = []
  let state = initial
  // Each game starts with mulligans, as a real game.
  // Then player 1 puts a card on the battlefield, marks it in each way and moves it out, and does the same with a token.
  // Then the random actions come.
  const last = `p${spec.players - 1}`
  const fieldCard = (current: TableState) => current.players[1].zones.battlefield.at(-1)?.id ?? 'none'
  const opening: ((current: TableState) => TableAction)[] = spec.scripted ?? [
    () => ({ type: 'mulligan', playerId: 'p0' }),
    () => ({ type: 'mulligan', playerId: 'p0' }),
    () => ({ type: 'mulligan', playerId: last }),
    (current) => ({ type: 'move', playerId: 'p1', cardId: current.players[1].zones.hand[0]?.id ?? 'none', to: 'battlefield' }),
    (current) => ({ type: 'tap', playerId: 'p1', cardId: fieldCard(current) }),
    (current) => ({ type: 'flip', playerId: 'p1', cardId: fieldCard(current) }),
    (current) => ({ type: 'counter', playerId: 'p1', cardId: fieldCard(current), change: 3 }),
    (current) => ({ type: 'move', playerId: 'p1', cardId: fieldCard(current), to: 'graveyard' }),
    () => ({ type: 'token', playerId: 'p1', name: 'Saproling' }),
    (current) => ({ type: 'counter', playerId: 'p1', cardId: fieldCard(current), change: 1 }),
    (current) => ({ type: 'move', playerId: 'p1', cardId: fieldCard(current), to: 'exile' }),
  ]
  for (let index = 0; index < spec.actions; index++) {
    const action = opening[index]?.(state) ?? chooseAction(state, random)
    const next = applyAction(state, action)
    steps.push({ action, changes: stateChanges(asJson(state), asJson(next)) })
    state = next
  }
  return { name: spec.name, seed: spec.seed, startingLife, commander: spec.commander, setups: playerSetups, initial, steps, final: state }
}

/** Gives the text of a vector file: one line for each step, so that a change gives a small diff. */
export function vectorText(vector: EngineVector): string {
  const { steps, final, ...head } = vector
  const line = (key: string, value: unknown) => `  ${JSON.stringify(key)}: ${JSON.stringify(value)}`
  const stepLines = steps.map((step) => `    ${JSON.stringify(step)}`)
  return `{\n${Object.entries(head).map(([key, value]) => line(key, value)).join(',\n')},\n  "steps": [\n${stepLines.join(',\n')}\n  ],\n${line('final', final)}\n}\n`
}
