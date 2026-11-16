import { describe, expect, it } from 'vitest'
import { applyAction, createTable, type PlayerSetup } from '../engine'
import { diffViews, playerView, viewHash } from '../views'
import type { Patch } from './hubClient'
import { applyPatch, type OnlineGameState } from './useOnlineGame'

function setup(id: string): PlayerSetup {
  return { id, name: id, library: Array.from({ length: 10 }, (_, index) => ({ id: `${id}-${index}`, oracleId: `o${index}`, name: `Card ${index}`, imageUri: null, isLand: false, manaValue: 1 })), command: [] }
}

const table = createTable([setup('s1'), setup('s2')], 1, 20)
const state: OnlineGameState = { status: 'connected', lobby: null, view: playerView(table, 's1'), log: [], closedReason: null, desyncs: 0 }

function patchFor(seq: number, hash?: string): Patch {
  const after = playerView(applyAction(table, { type: 'draw', playerId: 's1' }), 's1')
  return { seq, actorSeat: 's1', log: 's1 drew 1 card', changes: diffViews(state.view!, after), hash: hash ?? viewHash(after) }
}

describe('online game state', () => {
  it('applies a patch with the correct hash', () => {
    const result = applyPatch(state, patchFor(1))
    expect(result.resync).toBeNull()
    expect(result.state.view?.players[0].zones.hand).toHaveLength(8)
    expect(result.state.log).toEqual(['s1 drew 1 card'])
  })

  it('finds a view that is not in sync', () => {
    const result = applyPatch(state, patchFor(1, '0000000000000000'))
    expect(result.resync).toBe('hash')
    expect(result.state.desyncs).toBe(1)
  })

  it('finds a patch that cannot apply', () => {
    const result = applyPatch(state, { ...patchFor(1), changes: [{ seat: 's9', field: 'life', value: 1 }] })
    expect(result.resync).toBe('patch')
    expect(result.state.view).toBe(state.view)
  })

  it('accepts the empty patches of the lobby, and asks for a full view at the start with the reason "view"', () => {
    const lobbyState = { ...state, view: null }
    expect(applyPatch(lobbyState, { seq: 2, actorSeat: '0', log: '', changes: [], hash: '' }).resync).toBeNull()
    const start = applyPatch(lobbyState, { seq: 3, actorSeat: '0', log: '', changes: [], hash: '0123456789abcdef' })
    // The page has no view yet: this is not a desynchronization, so the metric of the desynchronizations does not count it.
    expect(start.resync).toBe('view')
    expect(start.state.desyncs).toBe(0)
  })
})
