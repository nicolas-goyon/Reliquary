import { describe, expect, it } from 'vitest'
import { applyAction, createTable, tableZones, type PlayerSetup, type TableAction, type TableState } from './engine'
import { projectPublicState, publicViewerId } from './projection'
import { applyViewChanges, canonicalJson, diffViews, fnv1a64, playerView, viewHash, type JsonValue } from './views'

/** The folder of the shared vectors. The C# tests of the API replay them. */
const vectorFolder = '../../../tests/vectors/views'

function setup(id: string, name: string, size: number): PlayerSetup {
  const card = (index: number) => ({
    id: `${id}-c${index}`,
    oracleId: `oracle-${id}-${index}`,
    name: `${name} card ${index}`,
    imageUri: `https://cards.example/${id}/${index}.jpg`,
    isLand: index % 3 === 0,
    manaValue: index % 6,
  })
  return { id, name, library: Array.from({ length: size }, (_, index) => card(index)), command: [card(99)] }
}

/**
 * Four players. The names test the escapes and the non-ASCII characters of the canonical JSON.
 * The vectors use small libraries, so that the files stay small.
 */
function table(seed: number, size = 30): TableState {
  const names = ['Ana "the fast"', 'Bruno \\ back\\slash', 'Chloé 🐉 ✓', 'Dan\ttab\u0001']
  return createTable(['s1', 's2', 's3', 's4'].map((id, index) => setup(id, names[index], size)), seed, 40, { commander: true })
}

/** A random but repeatable game. Each step gives the state after one action. */
function randomGame(seed: number, steps: number, size = 30): { action: TableAction; state: TableState }[] {
  let state = table(seed, size)
  let random = seed
  const next = (max: number) => {
    random = (Math.imul(random, 1103515245) + 12345) >>> 0
    return random % max
  }
  const result: { action: TableAction; state: TableState }[] = []
  for (let step = 0; step < steps; step++) {
    const player = state.players[next(state.players.length)]
    const other = state.players[next(state.players.length)]
    const zone = tableZones[next(tableZones.length)]
    const card = player.zones[zone][next(Math.max(1, player.zones[zone].length))]
    const choices: TableAction[] = [
      { type: 'draw', playerId: player.id, count: 1 + next(2) },
      { type: 'shuffle', playerId: player.id },
      { type: 'nextTurn' },
      { type: 'token', playerId: player.id, name: 'Treasure' },
      { type: 'life', playerId: player.id, change: next(7) - 4 },
      { type: 'commanderDamage', playerId: player.id, fromPlayerId: other.id, change: 1 + next(3) },
      { type: 'playerCounter', playerId: player.id, counter: 'poison', change: 1 },
      ...(card
        ? ([
            { type: 'move', playerId: player.id, cardId: card.id, to: tableZones[next(tableZones.length)] },
            { type: 'move', playerId: player.id, cardId: card.id, to: 'battlefield' },
            { type: 'flip', playerId: player.id, cardId: card.id },
            { type: 'tap', playerId: player.id, cardId: card.id },
            { type: 'counter', playerId: player.id, cardId: card.id, change: 1 },
          ] satisfies TableAction[])
        : []),
    ]
    const action = choices[next(choices.length)]
    state = applyAction(state, action)
    result.push({ action, state })
  }
  return result
}

const viewers = ['s1', 's2', 's3', 's4', publicViewerId]

describe('canonical JSON', () => {
  it('sorts the keys, writes no whitespace and skips the undefined values', () => {
    expect(canonicalJson({ b: 1, a: [true, null, 'x'], c: undefined, A: { z: -2, y: 0 } })).toBe('{"A":{"y":0,"z":-2},"a":[true,null,"x"],"b":1}')
  })

  it('sorts the keys by UTF-16 code units, as an ordinal comparison', () => {
    expect(canonicalJson({ é: 1, z: 2, Z: 3, '🐉': 4, '￿': 5 })).toBe('{"Z":3,"z":2,"é":1,"🐉":4,"￿":5}')
  })

  it('escapes the strings as JSON.stringify', () => {
    const texts = ['"\\/', '\b\f\n\r\t\u0000\u001f\u007f', 'é🐉  ', '\udc00 lone', 'a\ud800']
    for (const text of texts) {
      expect(canonicalJson(text)).toBe(JSON.stringify(text))
    }
  })

  it('writes the safe integers as String, and refuses the other numbers', () => {
    expect(canonicalJson([0, -0, -7, 2 ** 53 - 1])).toBe('[0,0,-7,9007199254740991]')
    expect(() => canonicalJson(1.5)).toThrow('only safe integers')
    expect(() => canonicalJson(2 ** 53)).toThrow('only safe integers')
    expect(() => canonicalJson(Number.NaN)).toThrow('only safe integers')
    expect(() => canonicalJson([undefined as unknown as JsonValue])).toThrow('undefined in an array')
  })
})

describe('FNV-1a 64-bit hash', () => {
  it('gives the published test values', () => {
    expect(fnv1a64('')).toBe('cbf29ce484222325')
    expect(fnv1a64('a')).toBe('af63dc4c8601ec8c')
    expect(fnv1a64('foobar')).toBe('85944171f73967e8')
  })

  it('hashes the UTF-8 bytes', () => {
    expect(fnv1a64('é')).toBe(fnv1a64('é'))
    expect(fnv1a64('é')).not.toBe(fnv1a64('é'))
  })
})

describe('player view', () => {
  it('has the same contract as projectPublicState', () => {
    const state = randomGame(5, 50).at(-1)!.state
    for (const viewer of viewers) {
      expect(playerView(state, viewer)).toEqual(projectPublicState(state, viewer))
    }
  })

  it('gives the same hash for equal views, and a different hash after a change', () => {
    const state = table(1)
    expect(viewHash(playerView(state, 's1'))).toBe(viewHash(playerView(structuredClone(state), 's1')))
    expect(viewHash(playerView(state, 's1'))).not.toBe(viewHash(playerView(applyAction(state, { type: 'life', playerId: 's2', change: -1 }), 's1')))
    // A shuffle of a library does not change the views, because no viewer sees the library order.
    const shuffled = applyAction(state, { type: 'shuffle', playerId: 's1' })
    expect(viewHash(playerView(shuffled, 's2'))).toBe(viewHash(playerView(state, 's2')))
  })
})

describe('zone patches', () => {
  it('gives the changed zones and fields only, in a fixed order', () => {
    const state = table(2)
    const card = state.players[0].zones.hand[0]
    const after = applyAction(applyAction(state, { type: 'move', playerId: 's1', cardId: card.id, to: 'battlefield' }), { type: 'nextTurn' })
    const changes = diffViews(playerView(state, 's3'), playerView(after, 's3'))
    expect(changes.map((change) => ('zone' in change ? `${change.seat}.${change.zone}` : 'seat' in change ? `${change.seat}.${change.field}` : change.field))).toEqual([
      'activePlayerId',
      'turn',
      's1.hand',
      's1.battlefield',
      's2.library',
      's2.hand',
    ])
    expect(changes[3]).toEqual({ seat: 's1', zone: 'battlefield', cards: [card] })
  })

  it('gives no change for equal views', () => {
    const view = playerView(table(3), 's1')
    expect(diffViews(view, structuredClone(view))).toEqual([])
  })

  it('gives the next view from the patch for each viewer during 300 random actions', () => {
    for (const seed of [11]) {
      let previous = table(seed)
      for (const { state } of randomGame(seed, 300)) {
        for (const viewer of viewers) {
          const before = playerView(previous, viewer)
          const after = playerView(state, viewer)
          const patched = applyViewChanges(before, diffViews(before, after))
          expect(viewHash(patched)).toBe(viewHash(after))
          expect(patched).toEqual(after)
        }
        previous = state
      }
    }
  })

  it('does not change the view that it patches', () => {
    const state = table(4)
    const before = playerView(state, 's1')
    const copy = structuredClone(before)
    applyViewChanges(before, diffViews(before, playerView(applyAction(state, { type: 'draw', playerId: 's1' }), 's1')))
    expect(before).toEqual(copy)
  })

  it('refuses a change for a player that is not in the view, and two views with different players', () => {
    const view = playerView(table(5), 's1')
    expect(() => applyViewChanges(view, [{ seat: 's9', field: 'life', value: 1 }])).toThrow('no player "s9"')
    expect(() => diffViews(view, { ...view, players: view.players.slice(1) })).toThrow('not have the same players')
  })
})

/** Writes a vector file with one item on each line, so a diff shows only the items that changed. */
function vectorFile(description: string, items: unknown[]): string {
  return `{\n"description": ${JSON.stringify(description)},\n"items": [\n${items.map((item) => JSON.stringify(item)).join(',\n')}\n]\n}\n`
}

describe('shared vectors for the C# side', () => {
  // In CI, Vitest does not write the files: a difference makes the test fail. Run "npx vitest run -u" to write the files again after a change of the contract.
  it('canonical JSON and hash of values', async () => {
    const values: JsonValue[] = [
      null,
      true,
      false,
      0,
      -0,
      -42,
      9007199254740991,
      -9007199254740991,
      '',
      'plain',
      '"\\/',
      '\b\f\n\r\t\u0000\u001f\u007f',
      'é🐉  ',
      '\udc00 lone',
      [],
      {},
      { b: [1, { d: 'x', c: null }], a: 'y', é: 1, Z: 2, '🐉': 3 },
    ]
    const items = values.map((value) => ({ value, canonicalJson: canonicalJson(value), hash: fnv1a64(canonicalJson(value)) }))
    await expect(vectorFile('Canonical JSON and 64-bit FNV-1a hash of single values. See web/src/playtest/views.ts.', items)).toMatchFileSnapshot(`${vectorFolder}/canonical-json.json`)
  })

  it('views, canonical JSON and hashes', async () => {
    const states = [table(21, 12), ...randomGame(21, 80, 12).filter((_, index) => index % 40 === 39).map((step) => step.state)]
    const items = states.flatMap((state, index) =>
      viewers.map((viewerId) => {
        const view = playerView(state, viewerId)
        const json = canonicalJson(view as unknown as JsonValue)
        return { name: `state ${index}, viewer ${viewerId}`, viewerId, view, canonicalJson: json, hash: fnv1a64(json) }
      }),
    )
    await expect(vectorFile('The view of each viewer, its canonical JSON and its hash. See web/src/playtest/views.ts.', items)).toMatchFileSnapshot(`${vectorFolder}/views.json`)
  })

  it('full states and the view hash of each viewer', async () => {
    // The C# tests make the view of each viewer from the full state, so these vectors check the C# projection (the hidden cards).
    // The states have face-down cards on the battlefield, in the graveyard and in the exile.
    const steps = randomGame(41, 160, 8)
    const states = [table(41, 8), ...steps.filter((_, index) => index % 40 === 39).map((step) => step.state)]
    const items = states.map((state, index) => ({
      name: `state ${index}`,
      state,
      hashes: Object.fromEntries(viewers.map((viewerId) => [viewerId, viewHash(playerView(state, viewerId))])),
    }))
    await expect(vectorFile('Full states and the view hash of each viewer. Make the view of each viewer from state: it has the hash of hashes. See web/src/playtest/projection.ts.', items)).toMatchFileSnapshot(`${vectorFolder}/projections.json`)
  })

  it('patches', async () => {
    const steps = randomGame(31, 24, 12)
    const items = steps.flatMap(({ action, state }, index) => {
      const previous = index === 0 ? table(31, 12) : steps[index - 1].state
      return ['s1', publicViewerId].map((viewerId) => {
        const before = playerView(previous, viewerId)
        const after = playerView(state, viewerId)
        // Each item has the view before, the patch and the hash of the view after. Every 6th item also has the full view after.
        const full = index % 6 === 0 ? { after } : {}
        return { name: `step ${index} (${action.type}), viewer ${viewerId}`, viewerId, before, patch: diffViews(before, after), ...full, afterHash: viewHash(after) }
      })
    })
    await expect(vectorFile('The patch from one view to the next view of the same viewer. Apply the patch to before: the result has the hash afterHash, and it is equal to after when the item has it. See web/src/playtest/views.ts.', items)).toMatchFileSnapshot(`${vectorFolder}/patches.json`)
  })
})
