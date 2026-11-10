import type { ExtensionPanel } from '../api/extensions'
import { navigate } from '../router/navigation'
import { pluginPanelSlots, type PanelState, type PluginPanelSlot, type PluginText, type SlotRegistry } from '../slots/registry'
import type { HostPeer } from './connection'
import type { ContributingPlugin } from './contributions'
import { mountPanel, panelMaxHeights, type MountPanelOptions } from './panels'
import { errorCodes } from './protocol'
import { RpcError } from './rpc'

/** The data of a plugin that its panels need. */
export interface PanelPlugin extends ContributingPlugin {
  version: string
  /** The address of the frame of the plugin. A panel uses the same frame. */
  src: string
  panels: ExtensionPanel[]
  i18n: Record<string, Record<string, string>> | null
}

/** The options of the runtime that each panel frame uses. */
export type PanelFrameOptions = Pick<MountPanelOptions, 'coreApi' | 'language' | 'hostWindow' | 'connectTimeoutMs' | 'intervalMs' | 'maxMissed' | 'timeoutMs' | 'maxMessageBytes'>

/** The plugin panels come after the panels of the core. */
const pluginOrderBase = 1000

/** Gets the title of a panel in English, with the translations of the field `i18n` of the plugin manifest. */
export function panelTitle(plugin: PanelPlugin, panel: ExtensionPanel): PluginText {
  const title: PluginText = { en: panel.title }
  for (const [language, texts] of Object.entries(plugin.i18n ?? {})) {
    const text = texts?.[`panels.${panel.id}.title`]
    if (language !== 'en' && typeof text === 'string' && text.trim()) {
      title[language] = text
    }
  }
  return title
}

/**
 * Adds the panels of a plugin to the slots, and the methods `ui.openDialog`, `ui.closeDialog` and `ui.openPage` to its main frame.
 * A panel of the slot "plugin.dialog" goes into the slot only when the plugin opens the dialog.
 * @returns A function that removes the panels and the dialog of the plugin.
 */
export function registerPluginUi(peer: HostPeer, plugin: PanelPlugin, registry: SlotRegistry, frameOptions: PanelFrameOptions): () => void {
  const removers: (() => void)[] = []
  let closeDialog: (() => void) | undefined
  const declared = plugin.panels.filter(
    (panel) => pluginPanelSlots.includes(panel.slot as PluginPanelSlot) && plugin.slots.includes(panel.slot),
  ) as (ExtensionPanel & { slot: PluginPanelSlot })[]

  const contribution = (panel: ExtensionPanel & { slot: PluginPanelSlot }, index: number, fixedContext?: unknown) => ({
    kind: 'plugin-panel' as const,
    id: `plugin.${plugin.id}.${panel.id}`,
    order: pluginOrderBase + index,
    plugin: { id: plugin.id, name: plugin.name, development: plugin.development },
    panelId: panel.id,
    title: panelTitle(plugin, panel),
    maxHeight: panelMaxHeights[panel.slot],
    mount: (element: HTMLElement, context: unknown, onState: (state: PanelState) => void) =>
      mountPanel({
        ...frameOptions,
        element,
        plugin: { id: plugin.id, version: plugin.version },
        src: plugin.src,
        development: plugin.development,
        panel: { id: panel.id, slot: panel.slot, title: panelTitle(plugin, panel).en },
        context: fixedContext ?? context,
        onState,
        registerMethods: (panelPeer) => registerNavigation(panelPeer),
      }),
  })

  const findPanel = (id: unknown, slot: PluginPanelSlot) => {
    const panel = declared.find((item) => item.id === id && item.slot === slot)
    if (!panel) {
      throw new RpcError(errorCodes.invalidParams, `The plugin manifest has no panel "${String(id)}" in the slot "${slot}".`)
    }
    return panel
  }

  /** The methods that the main frame and each panel frame of the plugin answer. */
  function registerNavigation(target: HostPeer): void {
    target.handle('ui.openDialog', (params) => {
      const { panel: id, data } = (params ?? {}) as { panel?: unknown; data?: unknown }
      const panel = findPanel(id, 'plugin.dialog')
      closeDialog?.()
      // Each open dialog has its own close function, so a late close cannot remove a newer dialog.
      let removed = false
      let remove = () => {}
      const closeThis = (): void => {
        if (removed) {
          return
        }
        removed = true
        remove()
        if (closeDialog === closeThis) {
          closeDialog = undefined
        }
      }
      remove = registry.register('plugin.dialog', { ...contribution(panel, 0, { data: data ?? null }), close: closeThis })
      closeDialog = closeThis
      return null
    })
    target.handle('ui.closeDialog', () => {
      closeDialog?.()
      return null
    })
    target.handle('ui.openPage', (params) => {
      const panel = findPanel((params as { page?: unknown } | null)?.page, 'plugin.page')
      navigate(`/plugins/${plugin.id}/${panel.id}`)
      return null
    })
  }

  registerNavigation(peer)
  declared.forEach((panel, index) => {
    if (panel.slot !== 'plugin.dialog') {
      removers.push(registry.register(panel.slot, contribution(panel, index)))
    }
  })

  return () => {
    closeDialog?.()
    for (const remove of removers) {
      remove()
    }
  }
}
