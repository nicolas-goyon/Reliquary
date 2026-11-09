/** The default size of the iframe of an embed. The oEmbed endpoint of the API uses the same size. */
export const embedSize = { width: 600, height: 700 } as const

/** Gets the address of the embed page of a deck. */
export function embedUrl(origin: string, deckId: string): string {
  return `${origin}/embed/decks/${deckId}`
}

/** Encodes the five special characters of HTML for an attribute value. */
function attribute(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;')
}

/**
 * Makes the HTML code that shows a public deck on another site: an iframe on the embed page.
 * The oEmbed endpoint of the API gives the same code. See docs/decisions/0046-deck-image-and-embed.md.
 */
export function embedSnippet(origin: string, deckId: string, deckName: string): string {
  return `<iframe src="${attribute(embedUrl(origin, deckId))}" title="${attribute(`${deckName} · Reliquary`)}" width="${embedSize.width}" height="${embedSize.height}" style="border:0" loading="lazy"></iframe>`
}
