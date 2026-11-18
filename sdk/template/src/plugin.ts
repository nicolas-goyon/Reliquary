import type { PluginDefinition, PluginSdk } from 'reliquary-plugin-sdk'

/** The lines of a deck, as `GET /decks/{id}` gives them. The plugin reads only the quantities. */
interface DeckResponse {
  name?: string
  lines?: { quantity?: number }[]
}

/** Counts the cards of a deck with the public API. The plugin manifest asks for the scope "decks:read". */
export async function countCards(sdk: PluginSdk, deckId: string): Promise<{ name: string; cards: number }> {
  const response = await sdk.api.fetch(`/decks/${encodeURIComponent(deckId)}`)
  if (response.status !== 200) {
    throw new Error(`The public API returned the status ${response.status}.`)
  }
  const deck = response.body as DeckResponse
  const cards = (deck.lines ?? []).reduce((sum, line) => sum + (line.quantity ?? 0), 0)
  return { name: deck.name ?? '', cards }
}

const texts = {
  en: { action: 'Count the cards', result: (name: string, cards: number) => `${name}: ${cards} cards.`, failed: 'The plugin did not read the deck.' },
  fr: { action: 'Compter les cartes', result: (name: string, cards: number) => `${name} : ${cards} cartes.`, failed: 'Le plugin n’a pas lu le deck.' },
}

function text(sdk: PluginSdk) {
  return sdk.language === 'fr' ? texts.fr : texts.en
}

/** The plugin: one button in the action bar of a deck, and one tab of the deck analysis. */
export const plugin: PluginDefinition = {
  async activate(sdk) {
    await sdk.ui.contribute('deck.actions', {
      id: 'count',
      label: { en: texts.en.action, fr: texts.fr.action },
      icon: 'chart',
      async onAction({ deckId }) {
        try {
          const { name, cards } = await countCards(sdk, deckId)
          await sdk.ui.notify(text(sdk).result(name, cards), 'success')
        } catch {
          await sdk.ui.notify(text(sdk).failed, 'error')
        }
      },
    })
  },
  panels: {
    size: {
      async mount(element, context, sdk) {
        const { deckId } = context as { deckId: string }
        const paragraph = element.ownerDocument.createElement('p')
        element.append(paragraph)
        try {
          const { name, cards } = await countCards(sdk, deckId)
          // Use textContent for the data of the user. Do not put data in HTML text.
          paragraph.textContent = text(sdk).result(name, cards)
        } catch {
          paragraph.textContent = text(sdk).failed
        }
      },
    },
  },
}
