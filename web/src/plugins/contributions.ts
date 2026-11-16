import { pluginActionSlots, pluginIcons, type PluginActionSlot, type PluginIcon, type PluginText, type SlotRegistry } from '../slots/registry'
import { createCellLoader, maxColumnsPerPlugin } from './columns'
import type { HostPeer } from './connection'
import { errorCodes } from './protocol'
import { RpcError } from './rpc'

/** The data of a plugin that the contributions need. */
export interface ContributingPlugin {
  id: string
  name: string
  /** The slots of the plugin manifest. A plugin can contribute only to these slots. */
  slots: string[]
  development: boolean
}

/** The maximum number of buttons or menu items of one plugin in one slot. */
export const maxActionsPerSlot = 3

/** The maximum length of a label of a plugin. */
export const maxLabelLength = 40

/** The plugin contributions come after the contributions of the core. */
const pluginOrderBase = 1000

const itemIdPattern = /^[a-z][a-z0-9-]{0,31}$/
const languagePattern = /^[a-z]{2}(-[A-Z]{2})?$/

/**
 * Gives the context of a slot to a plugin. The plugin gets IDs, and it reads more data with the public API.
 * The contexts follow the type SlotContexts of the plugin SDK.
 */
export function pluginContext(slot: PluginActionSlot, context: unknown): Record<string, unknown> {
  if (slot === 'deck.actions') {
    return { deckId: (context as { deckId: string }).deckId }
  }
  return {}
}

function invalid(message: string): RpcError {
  return new RpcError(errorCodes.invalidParams, message)
}

function readLabel(value: unknown, field: 'label' | 'title' = 'label'): PluginText {
  if (typeof value === 'string') {
    value = { en: value }
  }
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw invalid(`The ${field} must be a text, or an object with a text for each language.`)
  }
  const entries = Object.entries(value)
  if (typeof (value as { en?: unknown }).en !== 'string') {
    throw invalid(`The ${field} must have an English text in the key "en".`)
  }
  for (const [language, text] of entries) {
    if (!languagePattern.test(language) || typeof text !== 'string' || text.trim().length === 0 || text.length > maxLabelLength) {
      throw invalid(`Each text of the ${field} must have 1 to ${maxLabelLength} characters, with a language code such as "en" or "fr-CA".`)
    }
  }
  return Object.fromEntries(entries) as PluginText
}

/**
 * Adds the methods `ui.contribute` and `ui.withdraw` to the peer of a plugin.
 * The core checks the data, then adds a contribution of the kind "plugin" to the slot registry.
 * @returns A function that removes all the contributions of the plugin.
 */
export function registerContributionMethods(peer: HostPeer, plugin: ContributingPlugin, registry: SlotRegistry): () => void {
  const removers = new Map<string, () => void>()
  const key = (slot: string, id: string) => `${slot} ${id}`

  peer.handle('ui.contribute', (params) => {
    const { slot, contribution } = (params ?? {}) as { slot?: unknown; contribution?: Record<string, unknown> }
    if (slot === 'table.columns') {
      return contributeColumn(contribution)
    }
    if (typeof slot !== 'string' || !pluginActionSlots.includes(slot as PluginActionSlot)) {
      throw invalid(`The slot "${String(slot)}" does not accept buttons or menu items from plugins.`)
    }
    if (!plugin.slots.includes(slot)) {
      throw new RpcError(errorCodes.permissionDenied, `The plugin manifest does not declare the slot "${slot}". Add the slot to the field "slots".`)
    }
    if (typeof contribution !== 'object' || contribution === null) {
      throw invalid('The contribution must be an object.')
    }
    const { id, order, icon } = contribution
    if (typeof id !== 'string' || !itemIdPattern.test(id)) {
      throw invalid('The ID must have 1 to 32 lowercase letters, digits and hyphens, and start with a letter.')
    }
    const label = readLabel(contribution.label)
    if (icon !== undefined && !pluginIcons.includes(icon as PluginIcon)) {
      throw invalid(`The icon must be one of: ${pluginIcons.join(', ')}.`)
    }
    if (order !== undefined && (typeof order !== 'number' || !Number.isFinite(order))) {
      throw invalid('The order must be a number.')
    }
    const actionSlot = slot as PluginActionSlot
    const itemKey = key(actionSlot, id)
    const inSlot = [...removers.keys()].filter((existing) => existing.startsWith(`${actionSlot} `) && existing !== itemKey)
    if (inSlot.length >= maxActionsPerSlot) {
      throw invalid(`A plugin can add ${maxActionsPerSlot} items at most to the slot "${actionSlot}".`)
    }

    removers.get(itemKey)?.()
    const remove = registry.register(actionSlot, {
      kind: 'plugin',
      id: `plugin.${plugin.id}.${id}`,
      order: pluginOrderBase + Math.min(Math.max(Math.round((order as number | undefined) ?? 0), 0), 999),
      plugin: { id: plugin.id, name: plugin.name, development: plugin.development },
      label,
      icon: icon as PluginIcon | undefined,
      run: async (context) => {
        await peer.call('ui.action', { slot: actionSlot, id, context: pluginContext(actionSlot, context) })
      },
    })
    removers.set(itemKey, remove)
    return null
  })

  /** Adds a column of the table mode. The core asks the cells later, in batches, with `ui.cells`. */
  function contributeColumn(contribution: Record<string, unknown> | undefined): null {
    if (!plugin.slots.includes('table.columns')) {
      throw new RpcError(errorCodes.permissionDenied, 'The plugin manifest does not declare the slot "table.columns". Add the slot to the field "slots".')
    }
    if (typeof contribution !== 'object' || contribution === null) {
      throw invalid('The contribution must be an object.')
    }
    const { id, order } = contribution
    if (typeof id !== 'string' || !itemIdPattern.test(id)) {
      throw invalid('The ID must have 1 to 32 lowercase letters, digits and hyphens, and start with a letter.')
    }
    const title = readLabel(contribution.title, 'title')
    if (order !== undefined && (typeof order !== 'number' || !Number.isFinite(order))) {
      throw invalid('The order must be a number.')
    }
    const itemKey = key('table.columns', id)
    const columns = [...removers.keys()].filter((existing) => existing.startsWith('table.columns ') && existing !== itemKey)
    if (columns.length >= maxColumnsPerPlugin) {
      throw invalid(`A plugin can add ${maxColumnsPerPlugin} columns at most.`)
    }
    removers.get(itemKey)?.()
    removers.set(
      itemKey,
      registry.register('table.columns', {
        kind: 'plugin-column',
        id: `plugin.${plugin.id}.${id}`,
        order: pluginOrderBase + Math.min(Math.max(Math.round((order as number | undefined) ?? 0), 0), 999),
        plugin: { id: plugin.id, name: plugin.name, development: plugin.development },
        title,
        cell: createCellLoader(peer, id),
      }),
    )
    return null
  }

  peer.handle('ui.withdraw', (params) => {
    const { slot, id } = (params ?? {}) as { slot?: unknown; id?: unknown }
    const itemKey = key(String(slot), String(id))
    removers.get(itemKey)?.()
    removers.delete(itemKey)
    return null
  })

  return () => {
    for (const remove of removers.values()) {
      remove()
    }
    removers.clear()
  }
}
