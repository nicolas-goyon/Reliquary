import { describe, expect, it } from 'vitest'
import { applyAction, createTable, tableZones, type PlayerSetup, type TableAction, type TableCard, type TableState } from './engine'
import { projectPublicState, publicViewerId } from './projection'

/** Each card has a unique name and Oracle ID, so the test can search the projection for them. */
function setup(id: string): PlayerSetup {
  const card = (index: number) => ({ id: `${id}-line${index}-0`, oracleId: `oracle-${id}-${index}`, name: `Card ${id} ${index}`, imageUri: `https://img/${id}/${index}.jpg`, isLand: index % 3 === 0, manaValue: index % 5 })
  return { id, name: `Name ${id}`, library: Array.from({ length: 40 }, (_, index) => card(index)), command: [card(99)] }
}

function table(seed = 3): TableState {
  return createTable(['p1', 'p2', 'p3', 'p4'].map(setup), seed, 40, { commander: true })
}

/** The texts that tell the identity of a card. */
function identity(card: TableCard): string[] {
  return [card.id, card.oracleId, card.name, card.imageUri ?? ''].filter(Boolean)
}

/** Gives each hidden card of the state for the viewer, and checks that no text of its identity is in the projection. */
/** Gives all the strings of a value, also the strings in the keys of objects. */
function strings(value: unknown, found = new Set<string>()): Set<string> {
  if (typeof value === 'string') {
    found.add(value)
  } else if (value && typeof value === 'object') {
    for (const [key, child] of Object.entries(value)) {
      found.add(key)
      strings(child, found)
    }
  }
  return found
}

function leaks(state: TableState, viewerId: string, project = projectPublicState): string[] {
  const visible = strings(project(state, viewerId))
  const hidden = state.players.flatMap((player) =>
    tableZones.flatMap((zone) =>
      player.zones[zone].filter((card) => zone === 'library' || ((zone === 'hand' || card.faceDown) && player.id !== viewerId)),
    ),
  )
  return hidden.flatMap(identity).filter((value) => visible.has(value))
}

/** A random but repeatable list of actions that uses all the zones and face-down cards. */
function randomGame(seed: number, steps: number): TableState[] {
  let state = table(seed)
  let random = seed
  const next = (max: number) => {
    random = (Math.imul(random, 1103515245) + 12345) >>> 0
    return random % max
  }
  const states = [state]
  for (let step = 0; step < steps; step++) {
    const player = state.players[next(state.players.length)]
    const zone = tableZones[next(tableZones.length)]
    const card = player.zones[zone][next(Math.max(1, player.zones[zone].length))]
    const choices: TableAction[] = [
      { type: 'draw', playerId: player.id },
      { type: 'shuffle', playerId: player.id },
      { type: 'nextTurn' },
      { type: 'token', playerId: player.id, name: 'Treasure' },
      ...(card
        ? ([
            { type: 'move', playerId: player.id, cardId: card.id, to: tableZones[next(tableZones.length)] },
            { type: 'move', playerId: player.id, cardId: card.id, to: 'battlefield' },
            { type: 'flip', playerId: player.id, cardId: card.id },
            { type: 'tap', playerId: player.id, cardId: card.id },
          ] satisfies TableAction[])
        : []),
    ]
    state = applyAction(state, choices[next(choices.length)])
    states.push(state)
  }
  return states
}

describe('public projection', () => {
  it('has a leak check that finds the hidden cards in a state with no projection', () => {
    expect(leaks(table(), publicViewerId, (state) => state)).toHaveLength(4 * 40 * 4)
  })

  it('shows the hand to its owner only', () => {
    const state = table()
    const hand = state.players[1].zones.hand
    expect(projectPublicState(state, 'p2').players[1].zones.hand).toEqual(hand)
    for (const viewer of ['p1', publicViewerId]) {
      const projected = projectPublicState(state, viewer).players[1].zones.hand
      expect(projected).toHaveLength(hand.length)
      expect(projected.every((card) => card.faceDown && card.name === '' && card.oracleId === '' && card.imageUri === null)).toBe(true)
      expect(leaks(state, viewer)).toEqual([])
    }
  })

  it('hides the identity and the order of each library, also from its owner', () => {
    const state = table()
    const shuffled = applyAction(state, { type: 'shuffle', playerId: 'p1' })
    expect(shuffled.players[0].zones.library).not.toEqual(state.players[0].zones.library)
    for (const viewer of ['p1', 'p2', publicViewerId]) {
      expect(projectPublicState(shuffled, viewer).players[0].zones.library).toEqual(projectPublicState(state, viewer).players[0].zones.library)
      expect(projectPublicState(state, viewer).players[0].zones.library).toHaveLength(33)
    }
  })

  it('removes the seed, because the seed gives the next shuffles', () => {
    const state = table()
    expect(projectPublicState(state, 'p1').seed).toBe(0)
    expect(projectPublicState({ ...state, seed: 12345 }, 'p1')).toEqual(projectPublicState(state, 'p1'))
  })

  it('shows a face-down card to its owner only, and keeps its public marks', () => {
    let state = table()
    const id = state.players[2].zones.hand[0].id
    state = applyAction(state, { type: 'move', playerId: 'p3', cardId: id, to: 'battlefield' })
    state = applyAction(state, { type: 'flip', playerId: 'p3', cardId: id })
    state = applyAction(state, { type: 'tap', playerId: 'p3', cardId: id })
    state = applyAction(state, { type: 'counter', playerId: 'p3', cardId: id, change: 2 })

    expect(projectPublicState(state, 'p3').players[2].zones.battlefield[0]).toMatchObject({ id, faceDown: true })
    const masked = projectPublicState(state, publicViewerId).players[2].zones.battlefield[0]
    expect(masked).toMatchObject({ faceDown: true, tapped: true, counters: 2, name: '', oracleId: '' })
    expect(masked.id).not.toBe(id)
    expect(leaks(state, publicViewerId)).toEqual([])
    expect(leaks(state, 'p1')).toEqual([])
  })

  it('keeps the public zones, the life points and the counters', () => {
    let state = table()
    const id = state.players[0].zones.hand[0].id
    state = applyAction(state, { type: 'move', playerId: 'p1', cardId: id, to: 'graveyard' })
    state = applyAction(state, { type: 'life', playerId: 'p1', change: -3 })
    state = applyAction(state, { type: 'playerCounter', playerId: 'p1', counter: 'poison', change: 2 })
    state = applyAction(state, { type: 'commanderDamage', playerId: 'p1', fromPlayerId: 'p2', change: 6 })
    const player = projectPublicState(state, publicViewerId).players[0]
    expect(player.zones.graveyard).toEqual(state.players[0].zones.graveyard)
    expect(player.zones.command).toEqual(state.players[0].zones.command)
    expect(player.life).toBe(37)
    expect(player.counters.poison).toBe(2)
    expect(player.commanderDamage.p2).toBe(6)
  })

  it('does not change the state', () => {
    const state = table()
    const copy = structuredClone(state)
    projectPublicState(state, publicViewerId)
    expect(state).toEqual(copy)
  })

  it('gives no hidden card identity to any viewer during 300 random actions', () => {
    for (const seed of [1, 2, 3]) {
      for (const state of randomGame(seed, 300)) {
        for (const viewer of ['p1', 'p2', 'p3', 'p4', publicViewerId]) {
          expect(leaks(state, viewer)).toEqual([])
        }
      }
    }
  })
})
