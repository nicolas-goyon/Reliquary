/**
 * The slots that a plugin can use, and the context that the core gives for each slot.
 * The context is data: the plugin gets IDs, and it reads more data with `sdk.api.fetch`.
 * See docs/architecture/extensions.md.
 */

/** The context of each slot. */
export interface SlotContexts {
  /** The main navigation of the application. */
  navigation: Record<string, never>
  /** The menu of the user in the header. */
  'app.menu': Record<string, never>
  /** The action bar of a deck. */
  'deck.actions': { deckId: string }
  /** The tabs of the deck analysis. */
  'analysis.tabs': { deckId: string }
  /** The columns of the table mode. The core asks a value for each row. */
  'table.columns': { deckId: string; lineId: string; oracleId: string | null }
  /** The page of a card. */
  'card.page': { oracleId: string }
  /** The account page. */
  'account.page': Record<string, never>
  /** The linked accounts panel of the account page. */
  'account.linked': Record<string, never>
}

export type SlotName = keyof SlotContexts

/** A text in the language of the interface. Give an object to give a text for each language, for example `{ en: "Sync", fr: "Synchroniser" }`. */
export type LocalizedText = string | { en: string; [language: string]: string }

/** A button or a menu item. The core renders it with its own components. */
export interface ActionContribution<Slot extends SlotName> {
  id: string
  label: LocalizedText
  /** The position in the slot. A small number comes first. */
  order?: number
  /** Runs when the user selects the button or the menu item. */
  onAction?(context: SlotContexts[Slot]): void | Promise<void>
}

/** A column of the table mode. The core calls `cell` for each row that it shows. */
export interface ColumnContribution {
  id: string
  title: LocalizedText
  order?: number
  /** Gives the text of a cell. Return null for an empty cell. */
  cell(context: SlotContexts['table.columns']): string | null | Promise<string | null>
}

/** A panel: an analysis tab or a part of the card page or of the account page. It renders in its own sandboxed iframe. */
export interface PanelContribution {
  id: string
  title: LocalizedText
  order?: number
  /** The path of the HTML file of the panel in the plugin files. */
  panel: string
}

/** The contribution of a plugin to each slot. */
export interface SlotContributions {
  navigation: ActionContribution<'navigation'>
  'app.menu': ActionContribution<'app.menu'>
  'deck.actions': ActionContribution<'deck.actions'>
  'analysis.tabs': PanelContribution
  'table.columns': ColumnContribution
  'card.page': PanelContribution
  'account.page': PanelContribution
  'account.linked': PanelContribution
}

/** The data of a contribution that the plugin sends to the core: the contribution without its functions. */
export type ContributionData = { id: string; order?: number; label?: LocalizedText; title?: LocalizedText; panel?: string }
