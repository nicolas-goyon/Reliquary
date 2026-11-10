import { createContext, useContext, useSyncExternalStore, type ReactNode } from 'react'
import type { DeckAnalysis } from '../api/analysis'
import type { Deck, DeckCard, DeckLine } from '../api/decks'
import type { Account } from '../api/types'

/**
 * The context of a column of the table mode. The table renders each contribution once in the header,
 * with the part "header", and once in each row, with the part "cell" and the line of the row.
 */
export type TableColumnContext =
  | { deckId: string; part: 'header' }
  | { deckId: string; part: 'cell'; line: DeckLine; card: DeckCard | null }

/**
 * The named places of the interface where the core, and later the extensions, add interface elements.
 * See docs/architecture/extensions.md.
 */
export interface SlotContexts {
  /** The main navigation of the application. */
  navigation: { account: Account | null }
  /** The menu of the user in the header. */
  'app.menu': { account: Account | null }
  /** The action bar of a deck. The context gives the deck as the user sees it. */
  'deck.actions': { deckId: string; deck: Deck }
  /** The tabs of the deck analysis. Each contribution is one tab, and its title is the name of the tab. */
  'analysis.tabs': { deckId: string; deck: Deck; analysis: DeckAnalysis }
  /** The columns of the table mode: one column for each contribution. */
  'table.columns': TableColumnContext
  /** The page of a card. */
  'card.page': { oracleId: string }
  /** The account page. */
  'account.page': { account: Account }
  /** A page of a plugin at /plugins/<plugin ID>/<page>. Only plugin panels use this slot. */
  'plugin.page': { pluginId: string; page: string }
  /** The open dialogs of the plugins. Only plugin panels use this slot. */
  'plugin.dialog': Record<string, never>
}

export type SlotName = keyof SlotContexts

export const slotNames = [
  'navigation',
  'app.menu',
  'deck.actions',
  'analysis.tabs',
  'table.columns',
  'card.page',
  'account.page',
  'plugin.page',
  'plugin.dialog',
] as const satisfies readonly SlotName[]

/** One interface element in a slot. */
export interface SlotContribution<Name extends SlotName> {
  /** A unique ID in the slot, for example "core.apiKeys". */
  id: string
  /** The position in the slot. A small number comes first. The default is 100. */
  order?: number
  /**
   * Gives the title of the element, for a slot that shows titles. For example, the slot "analysis.tabs" shows it as the name of the tab.
   * Return null to hide the element for this context.
   */
  title?(context: SlotContexts[Name]): string | null
  /** Renders the element. Return null to show nothing for this context. */
  render(context: SlotContexts[Name]): ReactNode
}

/** The slots where a plugin adds a button or a menu item as data. See docs/architecture/extensions.md. */
export const pluginActionSlots = ['deck.actions', 'app.menu', 'navigation'] as const
export type PluginActionSlot = (typeof pluginActionSlots)[number]

/** The icons that a plugin can give to a button or a menu item. */
export const pluginIcons = ['sync', 'download', 'upload', 'link', 'external', 'chart', 'star', 'tag', 'search', 'settings', 'info', 'camera'] as const
export type PluginIcon = (typeof pluginIcons)[number]

/** A text of a plugin in English, with optional translations, for example `{ en: "Sync", fr: "Synchroniser" }`. */
export type PluginText = { en: string } & Record<string, string>

/**
 * A button or a menu item of a plugin. The plugin sends data, and the core renders it with its own components.
 * This contribution has no render function, so the plugin cannot add elements to the page of the core.
 */
export interface PluginActionContribution {
  kind: 'plugin'
  /** A unique ID in the slot: "plugin.<plugin ID>.<item ID>". */
  id: string
  order?: number
  plugin: { id: string; name: string; development: boolean }
  label: PluginText
  icon?: PluginIcon
  /** Sends the action to the plugin. The promise rejects when the plugin fails or does not answer. */
  run(context: unknown): Promise<void>
}

/** The slots where a plugin shows a panel: a sandboxed iframe that the core creates when the user opens the panel. */
export const pluginPanelSlots = ['analysis.tabs', 'card.page', 'plugin.page', 'plugin.dialog'] as const
export type PluginPanelSlot = (typeof pluginPanelSlots)[number]

/** The state of a panel frame. */
export type PanelState = 'loading' | 'ready' | 'failed'

/** An open panel. The component of the core calls `update` when the context changes, and `close` when the panel closes. */
export interface PanelHandle {
  update(context: unknown): void
  close(): void
}

/**
 * A panel of a plugin. The plugin manifest declares it. The core creates its frame only when the user opens the panel.
 * The contribution has no render function: the component `PluginPanel` of the core calls `mount`.
 */
export interface PluginPanelContribution {
  kind: 'plugin-panel'
  /** A unique ID in the slot: "plugin.<plugin ID>.<panel ID>". */
  id: string
  order?: number
  plugin: { id: string; name: string; development: boolean }
  /** The ID of the panel in the plugin manifest. */
  panelId: string
  title: PluginText
  /** The maximum height of the frame, in CSS pixels. */
  maxHeight: number
  /** Only in the slot "plugin.dialog": closes the dialog. */
  close?(): void
  /** Creates the frame of the panel in the element. */
  mount(element: HTMLElement, context: unknown, onState: (state: PanelState) => void): PanelHandle
}

/** The plugin contributions that each slot accepts. */
type PluginContributionOf<Name extends SlotName> =
  | (Name extends PluginActionSlot ? PluginActionContribution : never)
  | (Name extends PluginPanelSlot ? PluginPanelContribution : never)

/** A contribution of the core, or a contribution of a plugin in a slot that accepts plugin data. */
export type AnySlotContribution<Name extends SlotName> = SlotContribution<Name> | PluginContributionOf<Name>

/** Tells whether a contribution is a button or a menu item of a plugin. */
export function isPluginContribution<Name extends SlotName>(contribution: AnySlotContribution<Name>): contribution is Extract<PluginContributionOf<Name>, PluginActionContribution> {
  return 'kind' in contribution && contribution.kind === 'plugin'
}

/** Tells whether a contribution is a panel of a plugin. */
export function isPluginPanel<Name extends SlotName>(contribution: AnySlotContribution<Name>): contribution is Extract<PluginContributionOf<Name>, PluginPanelContribution> {
  return 'kind' in contribution && contribution.kind === 'plugin-panel'
}

type AnyContribution = AnySlotContribution<SlotName>

/** The contributions of each slot. The core registers its own panels here, and the extensions use the same registry later. */
export class SlotRegistry {
  private readonly contributions = new Map<SlotName, AnyContribution[]>()
  private readonly listeners = new Set<() => void>()
  private version = 0

  /**
   * Adds a contribution to a slot.
   * @returns A function that removes the contribution.
   */
  register<Name extends SlotName>(slot: Name, contribution: AnySlotContribution<Name>): () => void {
    const list = this.contributions.get(slot) ?? []
    if (list.some((existing) => existing.id === contribution.id)) {
      throw new Error(`The slot "${slot}" already has a contribution with the ID "${contribution.id}".`)
    }
    this.contributions.set(slot, [...list, contribution as unknown as AnyContribution])
    this.changed()
    return () => {
      this.contributions.set(slot, (this.contributions.get(slot) ?? []).filter((existing) => existing.id !== contribution.id))
      this.changed()
    }
  }

  /** Gets the contributions of a slot, in their order. */
  list<Name extends SlotName>(slot: Name): AnySlotContribution<Name>[] {
    return [...(this.contributions.get(slot) ?? [])]
      .sort((left, right) => (left.order ?? 100) - (right.order ?? 100) || left.id.localeCompare(right.id))
      .map((contribution) => contribution as unknown as AnySlotContribution<Name>)
  }

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  getVersion = (): number => this.version

  private changed(): void {
    this.version += 1
    for (const listener of this.listeners) {
      listener()
    }
  }
}

export const SlotRegistryContext = createContext<SlotRegistry | null>(null)

/** Gets the contributions of a slot, and renders again when they change. */
export function useSlotContributions<Name extends SlotName>(slot: Name): AnySlotContribution<Name>[] {
  const registry = useContext(SlotRegistryContext)
  if (!registry) {
    throw new Error('A slot needs a SlotRegistryContext above the component.')
  }
  useSyncExternalStore(registry.subscribe, registry.getVersion)
  return registry.list(slot)
}
