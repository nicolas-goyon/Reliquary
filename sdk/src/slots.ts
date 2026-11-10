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
  /** A tab of the deck analysis: a panel. */
  'analysis.tabs': { deckId: string }
  /** The columns of the table mode. The core asks a value for each row. */
  'table.columns': { deckId: string; lineId: string; oracleId: string; quantity: number; zone: string }
  /** A part of the page of a card: a panel. */
  'card.page': { oracleId: string }
  /** A page of the plugin at /plugins/<plugin ID>/<panel ID>: a panel. */
  'plugin.page': { page: string }
  /** A dialog of the plugin: a panel. `data` is the data of `sdk.ui.openDialog`. */
  'plugin.dialog': { data: unknown }
  /** The account page. */
  'account.page': Record<string, never>
  /** The linked accounts panel of the account page. */
  'account.linked': Record<string, never>
}

export type SlotName = keyof SlotContexts

/** A text in the language of the interface. Give an object to give a text for each language, for example `{ en: "Sync", fr: "Synchroniser" }`. */
export type LocalizedText = string | { en: string; [language: string]: string }

/** The icons that a button or a menu item can have. The core draws them. */
export const pluginIcons = ['sync', 'download', 'upload', 'link', 'external', 'chart', 'star', 'tag', 'search', 'settings', 'info', 'camera'] as const
export type PluginIcon = (typeof pluginIcons)[number]

/**
 * A button or a menu item. The core renders it with its own components, after the items of the core.
 * A plugin can add 3 items at most to each slot.
 */
export interface ActionContribution<Slot extends SlotName> {
  /** 1 to 32 lowercase letters, digits and hyphens. The first character is a letter. */
  id: string
  /** 1 to 40 characters in each language. The English text is necessary. */
  label: LocalizedText
  icon?: PluginIcon
  /** The position in the slot. A small number comes first. */
  order?: number
  /** Runs when the user selects the button or the menu item. */
  onAction?(context: SlotContexts[Slot]): void | Promise<void>
}

/** The tones of a cell. The core gives each tone a color of its theme. */
export const cellTones = ['neutral', 'positive', 'warning', 'negative'] as const
export type CellTone = (typeof cellTones)[number]

/** The value of a cell of a plugin column. The core renders the text: a cell has no iframe. */
export interface CellValue {
  /** 100 characters at most. */
  text: string
  tone?: CellTone
  /** A longer text that the core shows on hover and gives to screen readers. 200 characters at most. */
  tooltip?: string
}

/**
 * A column of the table mode. A plugin can add 2 columns at most.
 * The core asks the cells of many rows in one call, and keeps the result until the deck changes.
 */
export interface ColumnContribution {
  /** 1 to 32 lowercase letters, digits and hyphens. The first character is a letter. */
  id: string
  /** The header of the column: 1 to 40 characters in each language. */
  title: LocalizedText
  order?: number
  /** Gives the cells of the rows, in the same order. Return null for an empty cell. */
  cells(rows: SlotContexts['table.columns'][]): (CellValue | null)[] | Promise<(CellValue | null)[]>
}

/** The contributions that a plugin sends as data to a slot. */
export interface SlotContributions {
  navigation: ActionContribution<'navigation'>
  'app.menu': ActionContribution<'app.menu'>
  'deck.actions': ActionContribution<'deck.actions'>
  'table.columns': ColumnContribution
}

/** The data of a contribution that the plugin sends to the core: the contribution without its functions. */
export type ContributionData = { id: string; order?: number; label?: LocalizedText; icon?: PluginIcon; title?: LocalizedText }
