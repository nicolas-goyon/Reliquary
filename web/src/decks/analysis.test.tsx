import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { DeckAnalysis } from '../api/analysis'
import type { Deck } from '../api/decks'
import i18n from '../i18n'
import { registerCoreContributions } from '../slots/coreContributions'
import { SlotRegistry } from '../slots/registry'
import { mockApi, session } from '../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d1'
const solRing = '0190a000-0000-7000-8000-0000000000c1'
const atraxa = '0190a000-0000-7000-8000-0000000000c2'

function deck(overrides: Partial<Deck> = {}): Deck {
  return {
    id: deckId,
    ownerId: 'owner',
    name: 'Atraxa superfriends',
    format: 'commander',
    visibility: 'public',
    folderId: null,
    description: '',
    sourceDeckId: null,
    pinned: false,
    archived: false,
    version: 3,
    createdAt: '2026-10-27T10:00:00Z',
    updatedAt: '2026-10-27T10:00:00Z',
    isOwner: true,
    counts: { commander: 1, companion: 0, main: 1, sideboard: 0, considering: 0, tokens: 0 },
    lines: [
      { id: 'l1', oracleId: atraxa, printingId: null, finish: null, quantity: 1, zone: 'commander', note: '', tagIds: [], categories: [] },
      { id: 'l2', oracleId: solRing, printingId: null, finish: null, quantity: 1, zone: 'main', note: '', tagIds: [], categories: ['ramp'] },
    ],
    cards: {
      [atraxa]: { name: "Atraxa, Praetors' Voice", layout: 'normal', manaValue: 4, curveManaValue: 4, manaCost: '{G}{W}{U}{B}', typeLine: 'Legendary Creature', colorIdentity: ['W', 'U', 'B', 'G'], cardTypes: ['Creature'], isLand: false, defaultPrintingId: null, faces: [] },
      [solRing]: { name: 'Sol Ring', layout: 'normal', manaValue: 1, curveManaValue: 1, manaCost: '{1}', typeLine: 'Artifact', colorIdentity: [], cardTypes: ['Artifact'], isLand: false, defaultPrintingId: null, faces: [] },
    },
    printings: {},
    tags: {},
    ...overrides,
  }
}

const atLeast = (values: number[]) => values.map((probability, index) => ({ lands: index + 1, probability }))

function analysis(overrides: Partial<DeckAnalysis> = {}): DeckAnalysis {
  return {
    format: 'commander',
    cardCount: 100,
    landCount: 37,
    modalLandCount: 1,
    spellCount: 64,
    averageManaValue: 3.25,
    manaCurve: [0, 6, 12, 15, 14, 9, 5, 3].map((count, manaValue) => ({ manaValue, count })),
    types: [{ key: 'artifact', count: 10 }, { key: 'creature', count: 30 }, { key: 'land', count: 37 }],
    colors: [{ key: 'W', count: 20 }, { key: 'U', count: 18 }, { key: 'B', count: 15 }, { key: 'R', count: 0 }, { key: 'G', count: 22 }, { key: 'C', count: 8 }],
    categories: [{ key: 'ramp', count: 6 }, { key: 'land', count: 37 }, { key: 'uncategorized', count: 20 }],
    mana: {
      tableDeckSize: 99,
      colors: [
        { color: 'W', symbols: 25, landSources: 16, otherSources: 3, sources: 17.5, required: 23, oracleId: atraxa, pips: 1, manaValue: 4, status: 'low' },
        { color: 'R', symbols: 0, landSources: 2, otherSources: 0, sources: 2, required: null, oracleId: null, pips: null, manaValue: null, status: 'no_requirement' },
      ],
    },
    draws: {
      librarySize: 99,
      lands: 37,
      checkpoints: [
        { code: 'opening_hand', turn: 1, onThePlay: true, cards: 7, atLeast: atLeast([0.9632, 0.8312, 0.5938, 0.3166, 0.1213, 0.0316, 0.0045]) },
        { code: 'turn_2_play', turn: 2, onThePlay: true, cards: 8, atLeast: atLeast([0.9775, 0.8812, 0.6741, 0.4027, 0.1781, 0.0558, 0.0118]) },
      ],
    },
    needs: [
      { category: 'land', count: 37, target: 38, min: 36, max: 40, status: 'ok', difference: -1 },
      { category: 'ramp', count: 6, target: 10, min: 8, max: 15, status: 'low', difference: -4 },
      { category: 'synergy', count: 0, target: null, min: null, max: null, status: 'no_target', difference: null },
    ],
    bracket: { bracket: 3, reasons: [{ code: 'game_changers', bracket: 3, oracleIds: [solRing] }], unchecked: ['two_card_combos'] },
    ...overrides,
  }
}

function deckHandlers(analysisResponse: () => Response) {
  return {
    'POST /auth/refresh': () => session(),
    [`GET /decks/${deckId}`]: () => Response.json(deck()),
    [`GET /decks/${deckId}/legality`]: () => Response.json({ format: 'commander', legal: true, problems: [] }),
    [`GET /decks/${deckId}/analysis`]: analysisResponse,
  }
}

describe('deck analysis', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', `/decks/${deckId}`)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the mana curve and a low ramp need for a Commander deck', async () => {
    mockApi(deckHandlers(() => Response.json(analysis())))
    const user = userEvent.setup()
    render(<App />)

    const section = await screen.findByRole('region', { name: 'Deck analysis' })
    expect(await within(section).findByRole('tab', { name: 'Mana curve' })).toHaveAttribute('aria-selected', 'true')
    const curve = within(section).getByRole('table', { name: 'Mana curve' })
    expect(within(curve).getByRole('row', { name: '2 12' })).toBeInTheDocument()
    expect(within(curve).getByRole('row', { name: '7+ 3' })).toBeInTheDocument()
    expect(within(section).getByText('3.25')).toBeInTheDocument()

    await user.click(within(section).getByRole('tab', { name: 'Commander needs' }))

    const panel = within(section).getByRole('tabpanel', { name: 'Commander needs' })
    const ramp = within(panel).getByRole('row', { name: /^Ramp/ })
    expect(ramp).toHaveTextContent('Too few: add 4 cards')
    expect(ramp).toHaveTextContent('10 (from 8 to 15)')
    expect(within(panel).getByRole('row', { name: /^Synergy/ })).toHaveTextContent('No target')
    expect(within(panel).getByText('Bracket 3')).toBeInTheDocument()
    expect(within(panel).getByText(/Sol Ring/)).toBeInTheDocument()
    expect(within(panel).getByText('The analysis does not check two-card combos yet. A combo can put the deck in a higher bracket.')).toBeInTheDocument()
  })

  it('moves between the tabs with the keyboard', async () => {
    mockApi(deckHandlers(() => Response.json(analysis())))
    const user = userEvent.setup()
    render(<App />)

    const first = await screen.findByRole('tab', { name: 'Mana curve' })
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Mana curve', 'Distribution', 'Mana sources', 'Draw probabilities', 'Commander needs'])
    expect(first).toHaveAttribute('tabindex', '0')
    expect(screen.getByRole('tab', { name: 'Distribution' })).toHaveAttribute('tabindex', '-1')

    first.focus()
    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('tab', { name: 'Distribution' })).toHaveFocus()
    expect(screen.getByRole('tab', { name: 'Distribution' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('table', { name: 'Card types' })).toBeInTheDocument()

    await user.keyboard('{End}')
    expect(screen.getByRole('tab', { name: 'Commander needs' })).toHaveFocus()
    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('tab', { name: 'Mana curve' })).toHaveFocus()
    await user.keyboard('{ArrowLeft}')
    expect(screen.getByRole('tab', { name: 'Commander needs' })).toHaveFocus()
    await user.keyboard('{Home}')
    expect(screen.getByRole('tab', { name: 'Mana curve' })).toHaveAttribute('aria-selected', 'true')

    await user.click(screen.getByRole('tab', { name: 'Mana sources' }))
    const white = screen.getByRole('row', { name: /^White/ })
    expect(white).toHaveTextContent('Too few sources')
    expect(white).toHaveTextContent("Atraxa, Praetors' Voice (1 symbol)")

    await user.click(screen.getByRole('tab', { name: 'Draw probabilities' }))
    expect(screen.getByRole('row', { name: /^Opening hand/ })).toHaveTextContent('96.3%')
  })

  it('shows the tabs of the extensions and hides the Commander tab for another format', async () => {
    mockApi(deckHandlers(() => Response.json(analysis({ format: 'modern', needs: null, bracket: null }))))
    const registry = new SlotRegistry()
    registerCoreContributions(registry)
    registry.register('analysis.tabs', {
      id: 'plugin.prices',
      order: 60,
      title: () => 'Prices',
      render: ({ deckId: id, analysis: result }) => <p>Prices of {id}: {result.cardCount} cards</p>,
    })
    const user = userEvent.setup()
    render(<App registry={registry} />)

    const tab = await screen.findByRole('tab', { name: 'Prices' })
    expect(screen.queryByRole('tab', { name: 'Commander needs' })).not.toBeInTheDocument()
    await user.click(tab)

    expect(screen.getByRole('tabpanel', { name: 'Prices' })).toHaveTextContent(`Prices of ${deckId}: 100 cards`)
  })

  it('gets the analysis again for a new version of the deck', async () => {
    const calls = mockApi({
      ...deckHandlers(() => Response.json(analysis())),
      [`PATCH /decks/${deckId}/lines/l2`]: () => Response.json(deck({ version: 4 })),
    })
    const user = userEvent.setup()
    render(<App />)

    await screen.findByRole('tab', { name: 'Mana curve' })
    await user.click(screen.getByRole('button', { name: 'Add one Sol Ring' }))

    await waitFor(() => expect(calls.filter((call) => call.key === `GET /decks/${deckId}/analysis`)).toHaveLength(2))
  })

  it('keeps the deck page when the analysis fails', async () => {
    mockApi(deckHandlers(() => Response.json({ status: 500, code: 'internal_error', detail: 'x' }, { status: 500 })))
    render(<App />)

    expect(await screen.findByText('The application cannot get the analysis. Reload the page to try again.')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: /Main deck/ })).toBeInTheDocument()
  })
})
