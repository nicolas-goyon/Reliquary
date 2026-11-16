import { afterEach, describe, expect, it } from 'vitest'
import type { DeckLine } from '../api/decks'
import { isPluginColumn, SlotRegistry, type PluginColumnContribution } from '../slots/registry'
import type { HostPeer } from './connection'
import { maxColumnsPerPlugin, readCell } from './columns'
import { registerContributionMethods } from './contributions'
import type { HostMethods, PluginMethods } from './methods'
import { errorCodes } from './protocol'
import { RpcPeer, type PortLike } from './rpc'

const peers: { close(): void }[] = []

function line(id: string, quantity = 1): DeckLine {
  return { id, oracleId: `oracle-${id}`, printingId: null, finish: null, quantity, zone: 'main', note: 'private note', tagIds: [], categories: [] }
}

function context(lineId: string, deckVersion = 1) {
  return { deckId: 'deck-1', deckVersion, part: 'cell' as const, line: line(lineId), card: null }
}

/** A plugin with one column. It records each call of `ui.cells`. */
async function setup(slots = ['table.columns']) {
  const channel = new MessageChannel()
  const host: HostPeer = new RpcPeer<HostMethods, PluginMethods>(channel.port1 as unknown as PortLike)
  const plugin = new RpcPeer<PluginMethods, HostMethods>(channel.port2 as unknown as PortLike)
  peers.push(host, plugin)
  const registry = new SlotRegistry()
  registerContributionMethods(host, { id: 'prices', name: 'Prices', slots, development: false }, registry)
  const calls: { id: string; rows: Record<string, unknown>[] }[] = []
  let answer: (rows: Record<string, unknown>[]) => unknown = (rows) => ({ cells: rows.map((row) => ({ text: `${String(row.lineId)} €` })) })
  plugin.handle('ui.cells', (params) => {
    calls.push(params)
    return answer(params.rows)
  })
  return {
    plugin,
    registry,
    calls,
    answer: (value: typeof answer) => (answer = value),
    column: () => registry.list('table.columns').find(isPluginColumn) as PluginColumnContribution,
  }
}

afterEach(() => {
  for (const peer of peers.splice(0)) {
    peer.close()
  }
})

describe('plugin columns', () => {
  it('adds a column as data, and asks the cells of one render in one call with the data of the rows only', async () => {
    const { plugin, calls, column } = await setup()
    await plugin.call('ui.contribute', { slot: 'table.columns', contribution: { id: 'price', title: { en: 'Price', fr: 'Prix' } } })

    const cells = await Promise.all(['a', 'b', 'c'].map((id) => column().cell(context(id))))

    expect(column()).toMatchObject({ kind: 'plugin-column', id: 'plugin.prices.price', order: 1000, title: { en: 'Price', fr: 'Prix' } })
    expect(cells).toEqual([{ text: 'a €' }, { text: 'b €' }, { text: 'c €' }])
    expect(calls).toHaveLength(1)
    expect(calls[0]).toEqual({
      id: 'price',
      rows: ['a', 'b', 'c'].map((id) => ({ deckId: 'deck-1', lineId: id, oracleId: `oracle-${id}`, quantity: 1, zone: 'main' })),
    })
  })

  it('keeps the cells of a deck version, and asks again after a change of the deck', async () => {
    const { plugin, calls, column } = await setup()
    await plugin.call('ui.contribute', { slot: 'table.columns', contribution: { id: 'price', title: 'Price' } })

    await column().cell(context('a', 1))
    await column().cell(context('a', 1))
    expect(calls).toHaveLength(1)

    await column().cell(context('a', 2))
    expect(calls).toHaveLength(2)
  })

  it('asks the plugin in parts of 200 rows', async () => {
    const { plugin, calls, column } = await setup()
    await plugin.call('ui.contribute', { slot: 'table.columns', contribution: { id: 'price', title: 'Price' } })

    await Promise.all(Array.from({ length: 250 }, (_, index) => column().cell(context(`line-${index}`))))

    expect(calls.map((call) => call.rows.length)).toEqual([200, 50])
  })

  it('rejects the cells when the plugin gives a wrong number of cells, and asks again later', async () => {
    const { plugin, calls, column, answer } = await setup()
    await plugin.call('ui.contribute', { slot: 'table.columns', contribution: { id: 'price', title: 'Price' } })
    answer(() => ({ cells: [] }))

    await expect(column().cell(context('a'))).rejects.toMatchObject({ code: errorCodes.internalError })
    answer((rows) => ({ cells: rows.map(() => null) }))

    expect(await column().cell(context('a'))).toBeNull()
    expect(calls).toHaveLength(2)
  })

  it('keeps only the correct parts of a cell', () => {
    expect(readCell({ text: 'x'.repeat(150), tone: 'negative', tooltip: 'Price on 2026-11-10' })).toEqual({ text: 'x'.repeat(100), tone: 'negative', tooltip: 'Price on 2026-11-10' })
    expect(readCell({ text: 'Cheap', tone: 'red', tooltip: 3 })).toEqual({ text: 'Cheap' })
    expect(readCell({ html: '<b>1 €</b>' })).toBeNull()
    expect(readCell('1 €')).toBeNull()
  })

  it(`refuses a column of an undeclared slot, and more than ${maxColumnsPerPlugin} columns`, async () => {
    const declared = await setup()
    for (const id of ['a', 'b']) {
      await declared.plugin.call('ui.contribute', { slot: 'table.columns', contribution: { id, title: id } })
    }
    await expect(declared.plugin.call('ui.contribute', { slot: 'table.columns', contribution: { id: 'c', title: 'C' } })).rejects.toMatchObject({ code: errorCodes.invalidParams })
    await expect(declared.plugin.call('ui.contribute', { slot: 'table.columns', contribution: { id: 'd', title: { fr: 'Sans anglais' } } })).rejects.toMatchObject({ code: errorCodes.invalidParams })

    const undeclared = await setup(['deck.actions'])
    await expect(undeclared.plugin.call('ui.contribute', { slot: 'table.columns', contribution: { id: 'a', title: 'A' } })).rejects.toMatchObject({ code: errorCodes.permissionDenied })
  })
})
