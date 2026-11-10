import { pluginCellTones, type PluginCell, type PluginCellTone, type TableColumnContext } from '../slots/registry'
import type { HostPeer } from './connection'
import { errorCodes } from './protocol'
import { RpcError } from './rpc'

type CellContext = Extract<TableColumnContext, { part: 'cell' }>

/** The maximum number of columns of one plugin. */
export const maxColumnsPerPlugin = 2

/** The maximum number of rows in one call of `ui.cells`. */
export const maxRowsPerCall = 200

export const maxCellText = 100
export const maxCellTooltip = 200

/** Gives the row of a cell to a plugin: the IDs and the data of the deck line, not the objects of the page. */
export function cellRow(context: CellContext): Record<string, unknown> {
  return { deckId: context.deckId, lineId: context.line.id, oracleId: context.line.oracleId, quantity: context.line.quantity, zone: context.line.zone }
}

/** Checks a cell from the plugin. An incorrect cell is empty. */
export function readCell(value: unknown): PluginCell | null {
  if (typeof value !== 'object' || value === null) {
    return null
  }
  const { text, tone, tooltip } = value as Record<string, unknown>
  if (typeof text !== 'string') {
    return null
  }
  const cell: PluginCell = { text: text.slice(0, maxCellText) }
  if (typeof tone === 'string' && pluginCellTones.includes(tone as PluginCellTone)) {
    cell.tone = tone as PluginCellTone
  }
  if (typeof tooltip === 'string' && tooltip.trim()) {
    cell.tooltip = tooltip.slice(0, maxCellTooltip)
  }
  return cell
}

interface Waiting {
  key: string
  context: CellContext
  resolve(cell: PluginCell | null): void
  reject(error: unknown): void
}

/**
 * Collects the cells that the table asks in one render, and asks the plugin for all of them with one call of `ui.cells`
 * (in parts of 200 rows). The loader keeps the cells of the last version of each deck.
 */
export function createCellLoader(peer: HostPeer, columnId: string): (context: CellContext) => Promise<PluginCell | null> {
  const cache = new Map<string, Promise<PluginCell | null>>()
  const versions = new Map<string, number>()
  let waiting: Waiting[] = []
  let scheduled = false

  async function send(batch: Waiting[]): Promise<void> {
    try {
      const result = await peer.call('ui.cells', { id: columnId, rows: batch.map((item) => cellRow(item.context)) })
      const cells = (result as { cells?: unknown } | null)?.cells
      if (!Array.isArray(cells) || cells.length !== batch.length) {
        throw new RpcError(errorCodes.internalError, `The column "${columnId}" did not give one cell for each row.`)
      }
      batch.forEach((item, index) => item.resolve(readCell(cells[index])))
    } catch (error) {
      for (const item of batch) {
        // A failed cell can load again at the next render.
        cache.delete(item.key)
        item.reject(error)
      }
    }
  }

  function flush(): void {
    scheduled = false
    const batch = waiting
    waiting = []
    for (let start = 0; start < batch.length; start += maxRowsPerCall) {
      void send(batch.slice(start, start + maxRowsPerCall))
    }
  }

  return (context) => {
    // Forget the cells of an older version of the deck.
    if (versions.get(context.deckId) !== context.deckVersion) {
      versions.set(context.deckId, context.deckVersion)
      for (const key of cache.keys()) {
        if (key.startsWith(`${context.deckId} `)) {
          cache.delete(key)
        }
      }
    }
    const key = `${context.deckId} ${context.deckVersion} ${context.line.id}`
    const cached = cache.get(key)
    if (cached) {
      return cached
    }
    const promise = new Promise<PluginCell | null>((resolve, reject) => {
      waiting.push({ key, context, resolve, reject })
    })
    cache.set(key, promise)
    if (!scheduled) {
      scheduled = true
      setTimeout(flush, 0)
    }
    return promise
  }
}
