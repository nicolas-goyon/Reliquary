import { describe, expect, it } from 'vitest'
import { canViewerAct, isTableAction, parseControlMessage, tableViewerId } from './sync'

describe('window sync', () => {
  it('accepts the table actions with correct fields only', () => {
    expect(isTableAction({ type: 'draw', playerId: 'player-1' })).toBe(true)
    expect(isTableAction({ type: 'draw', playerId: 'player-1', count: 2 })).toBe(true)
    expect(isTableAction({ type: 'move', playerId: 'player-1', cardId: 'c', to: 'exile' })).toBe(true)
    expect(isTableAction({ type: 'playerCounter', playerId: 'player-1', counter: 'poison', change: 1 })).toBe(true)
    expect(isTableAction({ type: 'nextTurn' })).toBe(true)

    expect(isTableAction({ type: 'draw' })).toBe(false)
    expect(isTableAction({ type: 'draw', playerId: 'player-1', count: -1 })).toBe(false)
    expect(isTableAction({ type: 'move', playerId: 'player-1', cardId: 'c', to: 'sideboard' })).toBe(false)
    expect(isTableAction({ type: 'life', playerId: 'player-1', change: 0.5 })).toBe(false)
    expect(isTableAction({ type: 'playerCounter', playerId: 'player-1', counter: 'rad', change: 1 })).toBe(false)
    expect(isTableAction({ type: 'toString' })).toBe(false)
    expect(isTableAction({ type: 'restart' })).toBe(false)
    expect(isTableAction(null)).toBe(false)
  })

  it('lets a player window act only for its own player, and the table window not at all', () => {
    expect(canViewerAct('player-2', { type: 'draw', playerId: 'player-2' })).toBe(true)
    expect(canViewerAct('player-2', { type: 'draw', playerId: 'player-1' })).toBe(false)
    expect(canViewerAct('player-2', { type: 'nextTurn' })).toBe(true)
    expect(canViewerAct(tableViewerId, { type: 'nextTurn' })).toBe(false)
  })

  it('ignores the messages of other games and the messages with a wrong format', () => {
    expect(parseControlMessage({ type: 'hello', gameId: 'a', viewerId: 'player-1' }, 'a')).toEqual({ type: 'hello', gameId: 'a', viewerId: 'player-1' })
    expect(parseControlMessage({ type: 'hello', gameId: 'b', viewerId: 'player-1' }, 'a')).toBeNull()
    expect(parseControlMessage({ type: 'hello', gameId: 'a' }, 'a')).toBeNull()
    expect(parseControlMessage({ type: 'action', gameId: 'a', viewerId: 'player-1', action: { type: 'restart' } }, 'a')).toBeNull()
    expect(parseControlMessage('hello', 'a')).toBeNull()
  })
})
