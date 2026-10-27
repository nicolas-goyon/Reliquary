import { createContext, useContext, useSyncExternalStore, type ReactNode } from 'react'
import type { DeckAnalysis } from '../api/analysis'
import type { Deck } from '../api/decks'
import type { Account } from '../api/types'

/**
 * The named places of the interface where the core, and later the extensions, add interface elements.
 * See docs/architecture/extensions.md.
 */
export interface SlotContexts {
  /** The main navigation of the application. */
  navigation: { account: Account | null }
  /** The menu of the user in the header. */
  'app.menu': { account: Account | null }
  /** The action bar of a deck. */
  'deck.actions': { deckId: string }
  /** The tabs of the deck analysis. Each contribution is one tab, and its title is the name of the tab. */
  'analysis.tabs': { deckId: string; deck: Deck; analysis: DeckAnalysis }
  /** The columns of the table mode. */
  'table.columns': { deckId: string }
  /** The page of a card. */
  'card.page': { oracleId: string }
  /** The account page. */
  'account.page': { account: Account }
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

type AnyContribution = SlotContribution<SlotName>

/** The contributions of each slot. The core registers its own panels here, and the extensions use the same registry later. */
export class SlotRegistry {
  private readonly contributions = new Map<SlotName, AnyContribution[]>()
  private readonly listeners = new Set<() => void>()
  private version = 0

  /**
   * Adds a contribution to a slot.
   * @returns A function that removes the contribution.
   */
  register<Name extends SlotName>(slot: Name, contribution: SlotContribution<Name>): () => void {
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
  list<Name extends SlotName>(slot: Name): SlotContribution<Name>[] {
    return [...(this.contributions.get(slot) ?? [])]
      .sort((left, right) => (left.order ?? 100) - (right.order ?? 100) || left.id.localeCompare(right.id))
      .map((contribution) => contribution as unknown as SlotContribution<Name>)
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
export function useSlotContributions<Name extends SlotName>(slot: Name): SlotContribution<Name>[] {
  const registry = useContext(SlotRegistryContext)
  if (!registry) {
    throw new Error('A slot needs a SlotRegistryContext above the component.')
  }
  useSyncExternalStore(registry.subscribe, registry.getVersion)
  return registry.list(slot)
}
