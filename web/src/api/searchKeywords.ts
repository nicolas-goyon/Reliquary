/** The main keywords of the card search, for the autocomplete of the search field. See the Scryfall syntax guide. */
export const searchKeywords = [
  'name:', 'c:', 'id:', 't:', 'o:', 'fo:', 'kw:', 'm:', 'mv', 'pow', 'tou', 'loy', 'devotion:', 'produces:',
  'f:', 'banned:', 'restricted:', 'is:', 'not:', 'has:', 'otag:', 'function:', 'art:', 'set:', 'e:', 'b:', 'st:', 'in:',
  'r:', 'a:', 'ft:', 'wm:', 'game:', 'year', 'date', 'lang:', 'new:', 'frame:', 'border:', 'usd', 'eur', 'tix',
  'prints', 'sets', 'edhrec', 'unique:', 'order:', 'dir:', 'prefer:', 'include:extras',
] as const

/** Gets the word under the cursor: the text from the last space, parenthesis or minus sign to the cursor. */
export function wordAt(text: string, cursor: number): { start: number; word: string } {
  let start = cursor
  while (start > 0 && !/[\s()]/.test(text[start - 1])) {
    start--
  }
  if (text[start] === '-') {
    start++
  }
  return { start, word: text.slice(start, cursor) }
}

/** Gets the keywords that start with a word. A word with an operator or a colon gets no suggestion. */
export function keywordsFor(word: string): string[] {
  if (word.length === 0 || /[:<>=!"/]/.test(word)) {
    return []
  }
  const lower = word.toLowerCase()
  return searchKeywords.filter((keyword) => keyword.startsWith(lower) && keyword !== lower)
}
