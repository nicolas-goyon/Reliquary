import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import i18n from '../i18n'
import { atraxa, deckId, testDeck } from '../test/fixtures'
import { mockApi, problem, session } from '../test/mockApi'
import { seriousAccessibilityProblems } from '../test/axe'
import { artCrop, fit } from './deckImage'
import { gridCard, gridColumns, gridLines, imageFileName, imageWidth, margin, imageGroups, imageHeight, maxGridCards, placeGroups, typeGroup } from './deckImageLayout'
import { embedSnippet } from './embed'

const authorId = '0190a000-0000-7000-8000-0000000000a1'
const bolt = '0190a000-0000-7000-8000-0000000000c3'
const forest = '0190a000-0000-7000-8000-0000000000c4'

const summary = {
  id: deckId,
  name: 'Atraxa superfriends',
  format: 'commander',
  colorIdentity: 'WUBG',
  commanders: [{ oracleId: atraxa, name: "Atraxa, Praetors' Voice", imageUri: null }],
  author: { id: authorId, displayName: 'Ana' },
  cardCount: 2,
  copyCount: 0,
  favoriteCount: 0,
  updatedAt: '2026-10-27T10:00:00Z',
}

/** A deck with a commander, an artifact, an instant, 10 lands, a sideboard card and a considering card. */
function richDeck() {
  const base = testDeck({ visibility: 'public', isOwner: false })
  return testDeck({
    visibility: 'public',
    isOwner: false,
    counts: { commander: 1, companion: 0, main: 12, sideboard: 1, considering: 1, tokens: 0 },
    lines: [
      ...base.lines,
      { id: 'l3', oracleId: bolt, printingId: null, finish: null, quantity: 1, zone: 'main', note: '', tagIds: [], categories: [] },
      { id: 'l4', oracleId: forest, printingId: null, finish: null, quantity: 10, zone: 'main', note: '', tagIds: [], categories: [] },
      { id: 'l5', oracleId: bolt, printingId: null, finish: null, quantity: 1, zone: 'sideboard', note: '', tagIds: [], categories: [] },
      { id: 'l6', oracleId: forest, printingId: null, finish: null, quantity: 1, zone: 'considering', note: '', tagIds: [], categories: [] },
    ],
    cards: {
      ...base.cards,
      [bolt]: { name: 'Lightning Bolt', layout: 'normal', manaValue: 1, curveManaValue: 1, manaCost: '{R}', typeLine: 'Instant', colorIdentity: ['R'], cardTypes: ['Instant'], isLand: false, defaultPrintingId: null, faces: [] },
      [forest]: { name: 'Forest', layout: 'normal', manaValue: 0, curveManaValue: 0, manaCost: null, typeLine: 'Basic Land — Forest', colorIdentity: ['G'], cardTypes: ['Land'], isLand: true, defaultPrintingId: null, faces: [] },
    },
  })
}

describe('deck image layout', () => {
  it('groups the main deck by card type, between the commander and the sideboard', () => {
    const groups = imageGroups(richDeck())

    expect(groups.map((group) => [group.key, group.count])).toEqual([
      ['commander', 1],
      ['instant', 1],
      ['artifact', 1],
      ['land', 10],
      ['sideboard', 1],
    ])
    expect(groups.find((group) => group.key === 'commander')?.lines[0].name).toBe("Atraxa, Praetors' Voice")
  })

  it('puts a card in the first type group of its types, and an unknown type in "other"', () => {
    expect(typeGroup(['Artifact', 'Creature'])).toBe('creature')
    expect(typeGroup(['Enchantment', 'Land'])).toBe('enchantment')
    expect(typeGroup(['Conspiracy'])).toBe('other')
  })

  it('shows one card for each line in the grid, without the considering zone, and with a limit', () => {
    expect(gridLines(richDeck()).map((line) => [line.name, line.quantity])).toEqual([
      ["Atraxa, Praetors' Voice", 1],
      ['Forest', 10],
      ['Lightning Bolt', 1],
      ['Sol Ring', 1],
      ['Lightning Bolt', 1],
    ])
    const many = testDeck({ lines: Array.from({ length: maxGridCards + 30 }, (_, index) => ({ id: `m${index}`, oracleId: `card-${index}`, printingId: null, finish: null, quantity: 1, zone: 'main' as const, note: '', tagIds: [], categories: [] })) })
    expect(gridLines(many)).toHaveLength(maxGridCards)
  })

  it('keeps the grid inside the margins', () => {
    expect(gridColumns * gridCard.width + (gridColumns - 1) * gridCard.gap).toBeLessThanOrEqual(imageWidth - 2 * margin)
  })

  it('keeps each group in one column and balances the columns', () => {
    const groups = Array.from({ length: 6 }, (_, index) => ({ key: 'land' as const, count: 10, lines: Array.from({ length: 10 }, (_, line) => ({ oracleId: `${index}-${line}`, name: 'Forest', quantity: 1, imageUri: undefined })) }))

    const { placements, height } = placeGroups(groups, 3)

    expect(placements.map((placement) => placement.column)).toEqual([0, 0, 1, 1, 2, 2])
    expect(height).toBe(2 * (38 + 300) + 18)
  })

  it('gives a taller image for a grid of many cards', () => {
    const deck = richDeck()
    expect(imageHeight(deck, 'list')).toBeGreaterThan(400)
    expect(imageHeight(testDeck({ lines: Array.from({ length: 40 }, (_, index) => ({ id: `g${index}`, oracleId: `c${index}`, printingId: null, finish: null, quantity: 1, zone: 'main' as const, note: '', tagIds: [], categories: [] })) }), 'grid')).toBeGreaterThan(imageHeight(deck, 'grid'))
  })

  it('makes a safe file name and uses the art crop of Scryfall', () => {
    expect(imageFileName('Atraxa: "superfriends"/v2')).toBe('Atraxa superfriends v2.png')
    expect(imageFileName('   ')).toBe('deck.png')
    expect(artCrop('https://cards.scryfall.io/normal/front/a/b/ab.jpg?1')).toBe('https://cards.scryfall.io/art_crop/front/a/b/ab.jpg?1')
    expect(artCrop('https://example.org/card.jpg')).toBe('https://example.org/card.jpg')
  })

  it('cuts a long text with an ellipsis', () => {
    const context = { measureText: (text: string) => ({ width: text.length * 10 }) as TextMetrics }
    expect(fit(context, 'Short', 100)).toBe('Short')
    expect(fit(context, 'A very long deck name', 100)).toBe('A very lo…')
  })
})

describe('embed snippet', () => {
  it('encodes the deck name in the HTML code', () => {
    expect(embedSnippet('https://reliquary.example.org', deckId, 'Tom & "Jerry" <3')).toBe(
      `<iframe src="https://reliquary.example.org/embed/decks/${deckId}" title="Tom &amp; &quot;Jerry&quot; &lt;3 · Reliquary" width="600" height="700" style="border:0" loading="lazy"></iframe>`,
    )
  })
})

describe('share dialog', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  function mockDeckPage(isPublic: boolean, visibility: 'public' | 'unlisted' = 'public') {
    return mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${deckId}`]: () => Response.json({ ...richDeck(), visibility }),
      [`GET /decks/${deckId}/legality`]: () => Response.json({ format: 'commander', legal: true, problems: [] }),
      [`GET /decks/${deckId}/analysis`]: () => problem(500, 'internal_error'),
      [`GET /public-decks/${deckId}/social`]: () => Response.json({ public: isPublic, favorite: false, favoriteCount: 0, copyCount: 0, author: isPublic ? { id: authorId, displayName: 'Ana' } : null }),
    })
  }

  it('gives the embed code of a public deck and copies it', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    mockDeckPage(true)
    const writeText = vi.fn<(text: string) => Promise<void>>(async () => undefined)
    const user = userEvent.setup()
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Share as image or embed' }))
    const dialog = screen.getByRole('dialog', { name: 'Share the deck' })
    const code = within(dialog).getByLabelText('HTML code of the embed')
    expect(code).toHaveValue(embedSnippet(window.location.origin, deckId, 'Atraxa superfriends'))
    expect(within(dialog).getByRole('radio', { name: 'Commander art and list by card type' })).toBeChecked()
    expect(await seriousAccessibilityProblems(dialog)).toEqual([])

    await user.click(within(dialog).getByRole('button', { name: 'Copy the code' }))
    expect(writeText).toHaveBeenCalledWith(embedSnippet(window.location.origin, deckId, 'Atraxa superfriends'))
    expect(await within(dialog).findByText('The application copied the code.')).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('does not offer the image and the embed for an unlisted deck', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    mockDeckPage(false, 'unlisted')
    render(<App />)

    expect(await screen.findByRole('button', { name: 'Copy the link' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Share as image or embed' })).not.toBeInTheDocument()
  })

  it('tells the user when the browser cannot make the image', async () => {
    window.history.replaceState(null, '', `/decks/${deckId}`)
    mockDeckPage(true)
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Share as image or embed' }))
    await user.click(screen.getByRole('radio', { name: 'Card images' }))
    // jsdom has no canvas (see src/test/axe.ts), so the image fails.
    await user.click(screen.getByRole('button', { name: 'Download the image' }))

    expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('The application cannot make the image.')
  })
})

describe('embed page', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows a public deck with no application shell and no session', async () => {
    window.history.replaceState(null, '', `/embed/decks/${deckId}`)
    const calls = mockApi({
      [`GET /public-decks/${deckId}`]: () => Response.json(summary),
      [`GET /decks/${deckId}`]: () => Response.json(richDeck()),
    })
    render(<App />)

    const title = await screen.findByRole('link', { name: 'Atraxa superfriends' })
    expect(title).toHaveAttribute('href', `${window.location.origin}/decks/${deckId}`)
    expect(title).toHaveAttribute('target', '_blank')
    expect(screen.getByText('Commander · 2 cards · by Ana')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Land (10)' })).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Sideboard' })).getByText('Lightning Bolt')).toBeInTheDocument()
    expect(screen.queryByText('Forest', { selector: '[aria-label="Considering"] *' })).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
    expect(calls.map((call) => call.key)).not.toContain('POST /auth/refresh')
    await waitFor(async () => expect(await seriousAccessibilityProblems()).toEqual([]))
  })

  it('shows a "not available" text for a deck that is not public', async () => {
    window.history.replaceState(null, '', `/embed/decks/${deckId}`)
    const calls = mockApi({
      [`GET /public-decks/${deckId}`]: () => problem(404, 'not_found'),
    })
    render(<App />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Deck not available' })).toBeInTheDocument()
    expect(calls.map((call) => call.key)).toEqual([`GET /public-decks/${deckId}`])
  })
})

