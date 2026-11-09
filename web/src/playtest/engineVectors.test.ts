import { describe, expect, it } from 'vitest'
import { applyAction } from './engine'
import { generateVector, stateChanges, vectorSpecs, vectorText, type StateChange } from './engineVectors'

/** Applies the changes of a step to a JSON state, as the C# test does. */
function applyChanges(state: unknown, changes: readonly StateChange[]): unknown {
  const copy = structuredClone(state) as Record<string | number, unknown>
  for (const [path, value] of changes) {
    if (path.length === 0) {
      return structuredClone(value)
    }
    let target = copy
    for (const key of path.slice(0, -1)) {
      target = target[key] as Record<string | number, unknown>
    }
    target[path[path.length - 1]] = structuredClone(value)
  }
  return copy
}

// The files of the vectors. The C# tests of Reliquary.Playtest replay them.
const folder = '../../../tests/vectors/table-engine'

describe('table engine conformance vectors', () => {
  const vectors = vectorSpecs.map(generateVector)

  // A failure here means that engine.ts or the generator changed. Run "npm run vectors", then change the C# engine until its tests pass.
  it.each(vectors.map((vector) => [vector.name, vector] as const))('vector %s is up to date', async (name, vector) => {
    await expect(vectorText(vector)).toMatchFileSnapshot(`${folder}/${name}.json`)
  })

  it('covers each action type, 2 to 4 players, and the games with and with no commander damage', () => {
    const types = new Set(vectors.flatMap((vector) => vector.steps.map((step) => step.action.type)))
    expect([...types].sort()).toEqual(
      ['bottom', 'commanderDamage', 'counter', 'draw', 'flip', 'life', 'move', 'mulligan', 'nextTurn', 'playerCounter', 'shuffle', 'tap', 'token', 'untapAll'].sort(),
    )
    expect(new Set(vectors.map((vector) => vector.setups.length))).toEqual(new Set([2, 3, 4]))
    expect(new Set(vectors.map((vector) => vector.commander))).toEqual(new Set([true, false]))
  })

  it('has changes of the state for each action type, not only actions that the engine ignores', () => {
    const changing = new Set(vectors.flatMap((vector) => vector.steps.filter((step) => step.changes.length > 0).map((step) => step.action.type)))
    expect(changing.size).toBe(14)
  })

  it('moves tapped, face-down and counted cards and tokens out of the battlefield', () => {
    const left = { tapped: 0, faceDown: 0, counters: 0, token: 0 }
    for (const vector of vectors) {
      let state = vector.initial
      for (const { action } of vector.steps) {
        if (action.type === 'move' && action.to !== 'battlefield') {
          const card = state.players.find((player) => player.id === action.playerId)?.zones.battlefield.find((item) => item.id === action.cardId)
          left.tapped += card?.tapped ? 1 : 0
          left.faceDown += card?.faceDown ? 1 : 0
          left.counters += card && card.counters > 0 ? 1 : 0
          left.token += card?.isToken ? 1 : 0
        }
        state = applyAction(state, action)
      }
    }
    expect(Math.min(...Object.values(left))).toBeGreaterThanOrEqual(2)
  })

  it('has actions that the engine ignores', () => {
    expect(vectors.flatMap((vector) => vector.steps.filter((step) => step.changes.length === 0)).length).toBeGreaterThan(20)
  })

  it('gives changes that make the full state after each action', () => {
    for (const vector of vectors) {
      let state: unknown = JSON.parse(JSON.stringify(vector.initial))
      for (const step of vector.steps) {
        state = applyChanges(state, step.changes)
      }
      expect(state).toEqual(JSON.parse(JSON.stringify(vector.final)))
    }
  })

  it('gives the changes of a state as paths and values', () => {
    expect(stateChanges({ a: 1, b: [1, 2], c: [1] }, { a: 1, b: [1, 3], c: [1, 2] })).toEqual([
      [['b', 1], 3],
      [['c'], [1, 2]],
    ])
    expect(stateChanges(1, 2)).toEqual([[[], 2]])
  })

  it('gives the same vector as the engine, action by action', () => {
    const vector = vectors[0]
    const replayed = vector.steps.reduce((state, step) => applyAction(state, step.action), vector.initial)
    expect(replayed).toEqual(vector.final)
  })

  it('keeps the vector files small', () => {
    const size = vectors.reduce((sum, vector) => sum + vectorText(vector).length, 0)
    expect(size).toBeLessThan(400_000)
  })
})
