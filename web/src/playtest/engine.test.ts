import { describe, expect, it } from 'vitest'
import { applyAction, createTable, handStatistics, openingHandSize, type PlayerSetup, type TableState } from './engine'

function cards(prefix: string, count: number, isLand = false): PlayerSetup['library'] {
  return Array.from({ length: count }, (_, index) => ({ id: `${prefix}-${index}`, oracleId: prefix, name: prefix, imageUri: null, isLand, manaValue: 1 }))
}

function table(players = 1, seed = 42): TableState {
  const setups = Array.from({ length: players }, (_, index) => ({ id: `p${index}`, name: `P${index}`, library: cards(`c${index}`, 60), command: cards(`cmd${index}`, 1) }))
  return createTable(setups, seed, 20)
}

function cardCount(state: TableState, playerId = 'p0'): number {
  const player = state.players.find((item) => item.id === playerId)!
  return Object.values(player.zones).reduce((sum, zone) => sum + zone.length, 0)
}

describe('table engine', () => {
  it('starts with an opening hand and a command zone', () => {
    const state = table()
    expect(state.players[0].zones.hand).toHaveLength(openingHandSize)
    expect(state.players[0].zones.library).toHaveLength(53)
    expect(state.players[0].zones.command).toHaveLength(1)
  })

  it('gives the same table for the same seed, and a different table for a different seed', () => {
    expect(table(1, 7)).toEqual(table(1, 7))
    expect(table(1, 7).players[0].zones.hand).not.toEqual(table(1, 8).players[0].zones.hand)
  })

  it('draws the top card, and stops when the library is empty', () => {
    const state = table()
    const top = state.players[0].zones.library[0]
    const next = applyAction(state, { type: 'draw', playerId: 'p0' })
    expect(next.players[0].zones.hand.at(-1)).toEqual(top)
    const empty = applyAction(state, { type: 'draw', playerId: 'p0', count: 100 })
    expect(empty.players[0].zones.library).toHaveLength(0)
    expect(cardCount(empty)).toBe(61)
  })

  it('does the London mulligan: 7 new cards, then one card to the bottom for each mulligan', () => {
    let state = applyAction(table(), { type: 'mulligan', playerId: 'p0' })
    state = applyAction(state, { type: 'mulligan', playerId: 'p0' })
    expect(state.players[0].zones.hand).toHaveLength(7)
    expect(state.players[0].cardsToBottom).toBe(2)

    const first = state.players[0].zones.hand[0].id
    state = applyAction(state, { type: 'bottom', playerId: 'p0', cardId: first })
    state = applyAction(state, { type: 'bottom', playerId: 'p0', cardId: state.players[0].zones.hand[0].id })
    const unchanged = applyAction(state, { type: 'bottom', playerId: 'p0', cardId: state.players[0].zones.hand[0].id })

    expect(state.players[0].zones.hand).toHaveLength(5)
    expect(state.players[0].zones.library.at(-2)?.id).toBe(first)
    expect(unchanged.players[0].zones.hand).toHaveLength(5)
    expect(cardCount(state)).toBe(61)
  })

  it('moves a card and clears its marks when it leaves the battlefield', () => {
    let state = table()
    const id = state.players[0].zones.hand[0].id
    state = applyAction(state, { type: 'move', playerId: 'p0', cardId: id, to: 'battlefield' })
    state = applyAction(state, { type: 'tap', playerId: 'p0', cardId: id })
    state = applyAction(state, { type: 'counter', playerId: 'p0', cardId: id, change: 2 })
    expect(state.players[0].zones.battlefield[0]).toMatchObject({ tapped: true, counters: 2 })

    state = applyAction(state, { type: 'move', playerId: 'p0', cardId: id, to: 'graveyard' })
    expect(state.players[0].zones.graveyard[0]).toMatchObject({ id, tapped: false, counters: 0 })
  })

  it('removes a token that leaves the battlefield', () => {
    let state = applyAction(table(), { type: 'token', playerId: 'p0', name: 'Soldier' })
    const id = state.players[0].zones.battlefield[0].id
    state = applyAction(state, { type: 'flip', playerId: 'p0', cardId: id })
    expect(state.players[0].zones.battlefield[0].faceDown).toBe(true)
    state = applyAction(state, { type: 'move', playerId: 'p0', cardId: id, to: 'graveyard' })
    expect(state.players[0].zones.graveyard).toHaveLength(0)
    expect(state.players[0].zones.battlefield).toHaveLength(0)
  })

  it('does not apply game rules to the life points and the commander damage', () => {
    let state = applyAction(table(2), { type: 'life', playerId: 'p0', change: -25 })
    state = applyAction(state, { type: 'commanderDamage', playerId: 'p0', fromPlayerId: 'p1', change: 21 })
    expect(state.players[0].life).toBe(-5)
    expect(state.players[0].commanderDamage.p1).toBe(21)
  })

  it('gives the turn to the next player, who untaps and draws', () => {
    let state = table(2)
    const id = state.players[1].zones.hand[0].id
    state = applyAction(state, { type: 'move', playerId: 'p1', cardId: id, to: 'battlefield' })
    state = applyAction(state, { type: 'tap', playerId: 'p1', cardId: id })
    state = applyAction(state, { type: 'nextTurn' })
    expect(state.activePlayerId).toBe('p1')
    expect(state.turn).toBe(2)
    expect(state.players[1].zones.battlefield[0].tapped).toBe(false)
    expect(state.players[1].zones.hand).toHaveLength(7)
    expect(applyAction(state, { type: 'nextTurn' }).activePlayerId).toBe('p0')
  })

  it('does not change the state for an unknown card', () => {
    const state = table()
    expect(applyAction(state, { type: 'move', playerId: 'p0', cardId: 'none', to: 'exile' }).players[0]).toEqual(state.players[0])
  })

  it('gives hand statistics that agree with the probabilities', () => {
    const deck = [...cards('land', 40, true), ...cards('spell', 60)]
    const statistics = handStatistics(deck, 5000, 1)
    expect(statistics.averageLands).toBeGreaterThan(2.7)
    expect(statistics.averageLands).toBeLessThan(2.9)
    expect(statistics.landsPercent.reduce((sum, value) => sum + value, 0)).toBeCloseTo(100)
    expect(statistics.keepablePercent).toBeGreaterThan(70)
  })
})
