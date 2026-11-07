import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../../App'
import type { Deck, DeckCard, DeckLine } from '../../api/decks'
import type { DeckHistory, DeckVariant, DeckVersion, ListComparison, TransitionCopy, TransitionPlan } from '../../api/versions'
import i18n from '../../i18n'
import { mockApi, problem, session, type Handler } from '../../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d1'
const variantId = '0190a000-0000-7000-8000-0000000000d2'
const version1 = '0190a000-0000-7000-8000-0000000000a1'
const version2 = '0190a000-0000-7000-8000-0000000000a2'
const solRing = '0190a000-0000-7000-8000-0000000000c1'
const cultivate = '0190a000-0000-7000-8000-0000000000c3'
const swords = '0190a000-0000-7000-8000-0000000000c4'
const printingA = '0190a000-0000-7000-8000-0000000000e1'
const printingB = '0190a000-0000-7000-8000-0000000000e2'

function card(name: string): DeckCard {
  return { name, layout: 'normal', manaValue: 1, curveManaValue: 1, manaCost: null, typeLine: null, colorIdentity: [], cardTypes: [], isLand: false, defaultPrintingId: null, faces: [] }
}

const cards = { [solRing]: card('Sol Ring'), [cultivate]: card('Cultivate'), [swords]: card('Swords to Plowshares') }
const solRingLine: DeckLine = { id: 'l1', oracleId: solRing, printingId: null, finish: null, quantity: 1, zone: 'main', note: '', tagIds: [], categories: [] }

function deck(overrides: Partial<Deck> = {}): Deck {
  return {
    id: deckId,
    ownerId: 'owner',
    name: 'Atraxa superfriends',
    format: 'commander',
    visibility: 'private',
    folderId: null,
    description: '',
    sourceDeckId: null,
    pinned: false,
    archived: false,
    version: 3,
    createdAt: '2026-10-27T10:00:00Z',
    updatedAt: '2026-10-27T10:00:00Z',
    isOwner: true,
    counts: { commander: 0, companion: 0, main: 1, sideboard: 0, considering: 0, tokens: 0 },
    lines: [solRingLine],
    cards,
    printings: {},
    tags: {},
    variantOf: null,
    baseVersionId: null,
    headVersionId: version2,
    assembledVersionId: version1,
    ...overrides,
  }
}

const variantDeck = deck({ id: variantId, name: 'Budget', variantOf: deckId, baseVersionId: version2, headVersionId: null, assembledVersionId: null })

function version(id: string, number: number, overrides: Partial<DeckVersion> = {}): DeckVersion {
  return { id, number, kind: 'commit', message: `Message ${number}`, parentIds: [], createdAt: '2026-10-27T10:00:00Z', cardCount: 99, assembled: false, ...overrides }
}

const history: DeckHistory = {
  versions: [version(version2, 2, { message: 'Add ramp', parentIds: [version1] }), version(version1, 1, { message: 'First list', assembled: true })],
  workingListChanged: true,
  headVersionId: version2,
  assembledVersionId: version1,
}

const variant: DeckVariant = { id: variantId, name: 'Budget', baseVersionId: version2, headVersionId: null, updatedAt: '2026-10-27T10:00:00Z' }

/** The handlers of a deck page: the deck, its legality, its analysis, its history and its variants. */
function deckPage(shown: Deck, variants: DeckVariant[] = []): Record<string, Handler> {
  return {
    [`GET /decks/${shown.id}`]: () => Response.json(shown),
    [`GET /decks/${shown.id}/legality`]: () => Response.json({ format: 'commander', legal: true, problems: [] }),
    [`GET /decks/${shown.id}/analysis`]: () => problem(404, 'not_found'),
    [`GET /decks/${shown.id}/recommendations`]: () => problem(404, 'recommendations_disabled'),
    [`GET /decks/${shown.id}/versions`]: () => Response.json(history),
    [`GET /decks/${shown.id}/variants`]: () => Response.json(variants),
  }
}

function open(path: string) {
  window.history.replaceState(null, '', path)
  render(<App />)
}

async function historyPanel() {
  const panel = await screen.findByRole('region', { name: 'History' })
  await within(panel).findByText('Add ramp')
  return panel
}

describe('deck history', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the versions, the assembled version and the state of the working list', async () => {
    mockApi({ 'POST /auth/refresh': () => session(), ...deckPage(deck()) })
    open(`/decks/${deckId}`)

    const panel = await historyPanel()
    expect(within(panel).getByText('The working list changed after the last version.')).toBeInTheDocument()
    const items = within(panel).getAllByRole('listitem')
    expect(within(items[1]).getByText('Assembled version')).toBeInTheDocument()
    expect(within(items[0]).queryByText('Assembled version')).not.toBeInTheDocument()
    expect(within(items[0]).getByRole('link', { name: 'Compare with the parent of version 2' })).toHaveAttribute('href', `/compare?left=${deckId}:${version1}&right=${deckId}:${version2}`)
    expect(within(items[0]).getByRole('link', { name: 'Compare version 2 with the working list' })).toHaveAttribute('href', `/compare?left=${deckId}:${version2}&right=${deckId}`)
  })

  it('commits the working list with a message', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      ...deckPage(deck()),
      [`POST /decks/${deckId}/versions`]: () => Response.json(version('0190a000-0000-7000-8000-0000000000a3', 3), { status: 201 }),
    })
    const user = userEvent.setup()
    open(`/decks/${deckId}`)

    const panel = await historyPanel()
    await user.click(within(panel).getByRole('button', { name: 'Commit' }))
    const dialog = screen.getByRole('dialog', { name: 'Commit the working list' })
    await user.type(within(dialog).getByLabelText('Message'), 'Cut the slow cards')
    await user.click(within(dialog).getByRole('button', { name: 'Commit' }))

    expect(await within(panel).findByText('The application created version 3.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(calls.find((call) => call.key === `POST /decks/${deckId}/versions`)?.body).toEqual({ message: 'Cut the slow cards' })
  })

  it('tells the user when the working list has no changes', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      ...deckPage(deck()),
      [`POST /decks/${deckId}/versions`]: () => problem(409, 'deck_no_changes'),
    })
    const user = userEvent.setup()
    open(`/decks/${deckId}`)

    const panel = await historyPanel()
    await user.click(within(panel).getByRole('button', { name: 'Commit' }))
    const dialog = screen.getByRole('dialog', { name: 'Commit the working list' })
    await user.type(within(dialog).getByLabelText('Message'), 'Nothing')
    await user.click(within(dialog).getByRole('button', { name: 'Commit' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('The working list did not change after the last version. Change the deck, then commit.')
    expect(screen.getByRole('dialog', { name: 'Commit the working list' })).toBeInTheDocument()
  })

  it('restores a version only after the confirmation', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      ...deckPage(deck()),
      [`POST /decks/${deckId}/versions/${version1}/restore`]: () => Response.json(version('0190a000-0000-7000-8000-0000000000a3', 3, { kind: 'restore' }), { status: 201 }),
    })
    const user = userEvent.setup()
    open(`/decks/${deckId}`)

    const panel = await historyPanel()
    await user.click(within(panel).getByRole('button', { name: 'Restore version 1' }))
    const dialog = screen.getByRole('dialog', { name: 'Restore version 1?' })
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(calls.some((call) => call.key.endsWith('/restore'))).toBe(false)

    await user.click(within(panel).getByRole('button', { name: 'Restore version 1' }))
    await user.click(within(screen.getByRole('dialog', { name: 'Restore version 1?' })).getByRole('button', { name: 'Restore' }))

    expect(await within(panel).findByText('The application created version 3.')).toBeInTheDocument()
    expect(calls.filter((call) => call.key === `POST /decks/${deckId}/versions/${version1}/restore`)).toHaveLength(1)
  })

  it('shows the cards of a version and its changes against its parent', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      ...deckPage(deck()),
      [`GET /decks/${deckId}/versions/${version2}`]: () =>
        Response.json({
          version: history.versions[0],
          lines: [{ oracleId: cultivate, printingId: null, finish: 'foil', quantity: 2, zone: 'main', note: 'Ramp', categories: [], tags: [], globalTags: [] }],
          cards,
          printings: {},
        }),
      [`GET /decks/${deckId}/versions/${version2}/changes`]: () =>
        Response.json({ quantities: [{ oracleId: cultivate, zone: 'main', before: 0, after: 2 }], printings: [], notes: [], cards, printingData: {} }),
    })
    const user = userEvent.setup()
    open(`/decks/${deckId}`)

    const panel = await historyPanel()
    await user.click(within(panel).getByRole('button', { name: 'Cards of version 2' }))
    const lines = await screen.findByRole('region', { name: 'Cards of version 2' })
    expect(await within(lines).findByRole('link', { name: 'Cultivate' })).toBeInTheDocument()
    expect(within(lines).getByRole('heading', { name: 'Main deck (2)' })).toBeInTheDocument()

    await user.click(within(panel).getByRole('button', { name: 'Changes of version 2' }))
    const changes = await screen.findByRole('region', { name: 'Changes of version 2' })
    const added = await within(changes).findByRole('table', { name: 'Added cards: 2' })
    expect(within(added).getByRole('cell', { name: '+2' })).toBeInTheDocument()
  })

  it('works with the keyboard in a dialog', async () => {
    mockApi({ 'POST /auth/refresh': () => session(), ...deckPage(deck()) })
    const user = userEvent.setup()
    open(`/decks/${deckId}`)

    const panel = await historyPanel()
    const commit = within(panel).getByRole('button', { name: 'Commit' })
    commit.focus()
    await user.keyboard('{Enter}')

    const dialog = screen.getByRole('dialog', { name: 'Commit the working list' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(within(dialog).getByLabelText('Message')).toHaveFocus()
    await user.keyboard('First')
    await user.tab()
    expect(within(dialog).getByRole('button', { name: 'Commit' })).toHaveFocus()
    await user.tab()
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus()
    await user.tab()
    expect(within(dialog).getByLabelText('Message')).toHaveFocus()
    await user.tab({ shift: true })
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(commit).toHaveFocus()
  })

  it('hides the operations of the owner from another user', async () => {
    mockApi({ 'POST /auth/refresh': () => session(), ...deckPage(deck({ isOwner: false, visibility: 'unlisted' })), [`GET /public-decks/${deckId}/social`]: () => problem(404, 'not_found') })
    open(`/decks/${deckId}`)

    const panel = await historyPanel()
    expect(within(panel).queryByRole('button', { name: 'Commit' })).not.toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: /Restore/ })).not.toBeInTheDocument()
    expect(within(panel).getByRole('button', { name: 'Cards of version 1' })).toBeInTheDocument()
  })
})

describe('deck variants', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('creates a variant from a version and opens it', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      ...deckPage(deck()),
      ...deckPage(variantDeck, [variant]),
      [`POST /decks/${deckId}/variants`]: () => Response.json(variant, { status: 201 }),
    })
    const user = userEvent.setup()
    open(`/decks/${deckId}`)

    expect(await screen.findByText('This deck has no variant.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Create a variant' }))
    const dialog = screen.getByRole('dialog', { name: 'Create a variant' })
    expect(within(dialog).getByLabelText('Name')).toHaveFocus()
    await user.type(within(dialog).getByLabelText('Name'), 'Budget')
    const base = within(dialog).getByLabelText('Base version')
    await within(dialog).findByRole('option', { name: 'Version 1: First list' })
    await user.selectOptions(base, version1)
    await user.click(within(dialog).getByRole('button', { name: 'Create the variant' }))

    await waitFor(() => expect(window.location.pathname).toBe(`/decks/${variantId}`))
    expect(calls.find((call) => call.key === `POST /decks/${deckId}/variants`)?.body).toEqual({ name: 'Budget', baseVersionId: version1 })
    expect(await screen.findByText('This deck is a variant. Main deck:')).toBeInTheDocument()
    const notice = screen.getByText('This deck is a variant. Main deck:')
    expect(within(notice).getByRole('link', { name: 'Atraxa superfriends' })).toHaveAttribute('href', `/decks/${deckId}`)
    expect(screen.getByRole('link', { name: 'Budget' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: 'Compare with the main deck' })).toHaveAttribute('href', `/compare?left=${deckId}&right=${variantId}`)
    // The API refuses a change of the format or the visibility of a variant.
    expect(screen.queryByLabelText('Format')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Visibility')).not.toBeInTheDocument()
  })

  it('merges a variant into the main deck', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      ...deckPage(deck()),
      ...deckPage(variantDeck, [variant]),
      [`POST /decks/${variantId}/merge`]: () => Response.json(version('0190a000-0000-7000-8000-0000000000a4', 4, { kind: 'merge' }), { status: 201 }),
    })
    const user = userEvent.setup()
    open(`/decks/${variantId}`)

    await user.click(await screen.findByRole('button', { name: 'Merge into the main deck' }))
    const dialog = screen.getByRole('dialog', { name: 'Merge into the main deck' })
    expect(within(dialog).getByText(/The working list of the main deck Atraxa superfriends gets the cards of the variant Budget/)).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Message'), 'Take the budget cuts')
    await user.click(within(dialog).getByRole('button', { name: 'Merge' }))

    expect(await screen.findByText('The application merged the variant into the main deck as version 4.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(calls.find((call) => call.key === `POST /decks/${variantId}/merge`)?.body).toEqual({ message: 'Take the budget cuts' })
  })
})

describe('comparison page', () => {
  const comparison: ListComparison = {
    changes: {
      quantities: [
        { oracleId: cultivate, zone: 'main', before: 1, after: 3 },
        { oracleId: swords, zone: 'main', before: 1, after: 0 },
      ],
      printings: [{ oracleId: solRing, zone: 'main', before: [{ printingId: printingA, finish: null, quantity: 1 }], after: [{ printingId: printingB, finish: 'foil', quantity: 1 }] }],
      notes: [],
      cards,
      printingData: {
        [printingA]: { setCode: 'c21', collectorNumber: '263', lang: 'en', imageUri: null },
        [printingB]: { setCode: 'ltc', collectorNumber: '3', lang: 'en', imageUri: null },
      },
    },
    left: { cardCount: 99, landCount: 37, averageManaValue: 3.1, manaCurve: [{ manaValue: 2, count: 10 }, { manaValue: 3, count: 8 }], types: [{ key: 'instant', count: 9 }], categories: [{ key: 'ramp', count: 9 }] },
    right: { cardCount: 100, landCount: 37, averageManaValue: 3.2, manaCurve: [{ manaValue: 2, count: 9 }, { manaValue: 3, count: 10 }], types: [{ key: 'instant', count: 8 }], categories: [{ key: 'ramp', count: 11 }] },
  }

  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the added and removed cards, the effect on the deck, and hides the printing changes', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /decks': () => Response.json({ items: [{ id: deckId, name: 'Atraxa superfriends' }], nextCursor: null }),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${deckId}/versions`]: () => Response.json(history),
      [`GET /decks/${deckId}/variants`]: () => Response.json([]),
      'POST /lists/compare': () => Response.json(comparison),
    })
    const user = userEvent.setup()
    open(`/compare?left=${deckId}:${version1}&right=${deckId}`)

    const added = await screen.findByRole('table', { name: 'Added cards: 2' })
    const cultivateRow = within(added).getByRole('row', { name: /Cultivate/ })
    expect(within(cultivateRow).getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['Main deck', '1', '3', '+2'])
    const removed = screen.getByRole('table', { name: 'Removed cards: 1' })
    expect(within(within(removed).getByRole('row', { name: /Swords to Plowshares/ })).getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['Main deck', '1', '0', '−1'])
    expect(calls.find((call) => call.key === 'POST /lists/compare')?.body).toEqual({ left: { deckId, versionId: version1 }, right: { deckId, versionId: null } })

    const curve = screen.getByRole('table', { name: 'Mana curve' })
    expect(within(within(curve).getByRole('row', { name: /^3/ })).getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['8', '10', '+2'])
    const categories = screen.getByRole('table', { name: 'Categories' })
    expect(within(categories).getByRole('row', { name: /Ramp/ })).toHaveTextContent('+2')

    // The printing changes are in a closed panel.
    const printingTable = screen.getByRole('table', { name: 'Changes of printing', hidden: true })
    expect(printingTable).not.toBeVisible()
    await user.click(screen.getByText('Changes of printing: 1'))
    expect(printingTable).toBeVisible()
    expect(within(printingTable).getByText('1 × C21 #263')).toBeInTheDocument()
    expect(within(printingTable).getByText('1 × LTC #3, Foil')).toBeInTheDocument()

    // The two lists come from the query.
    expect(await screen.findByRole('option', { name: 'Version 1: First list', selected: true })).toBeInTheDocument()
  })

  it('compares the lists that the user selects', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /decks': () => Response.json({ items: [{ id: deckId, name: 'Atraxa superfriends' }], nextCursor: null }),
      [`GET /decks/${deckId}/versions`]: () => Response.json(history),
      [`GET /decks/${deckId}/variants`]: () => Response.json([variant]),
      [`GET /decks/${deckId}`]: () => Response.json(deck()),
      [`GET /decks/${variantId}`]: () => Response.json(variantDeck),
      [`GET /decks/${variantId}/versions`]: () => Response.json({ ...history, versions: [] }),
      'POST /lists/compare': () => Response.json(comparison),
    })
    const user = userEvent.setup()
    open('/compare')

    const first = screen.getByRole('group', { name: 'First list' })
    const second = screen.getByRole('group', { name: 'Second list' })
    await within(first).findByRole('option', { name: 'Atraxa superfriends' })
    await user.selectOptions(within(first).getByLabelText('Deck'), deckId)
    await within(first).findByRole('option', { name: 'Version 2: Add ramp' })
    await user.selectOptions(within(first).getByLabelText('Version'), version2)
    await user.selectOptions(within(second).getByLabelText('Deck'), deckId)
    await within(second).findByRole('option', { name: 'Budget' })
    await user.selectOptions(within(second).getByLabelText('Variant'), variantId)
    await user.click(screen.getByRole('button', { name: 'Compare' }))

    expect(await screen.findByRole('table', { name: 'Added cards: 2' })).toBeInTheDocument()
    expect(window.location.search).toBe(`?left=${deckId}:${version2}&right=${variantId}`)
    expect(calls.find((call) => call.key === 'POST /lists/compare')?.body).toEqual({ left: { deckId, versionId: version2 }, right: { deckId: variantId, versionId: null } })
  })
})

describe('assembled version and transition plan', () => {
  const rack = '0190a000-0000-7000-8000-0000000000f1'
  const binder = '0190a000-0000-7000-8000-0000000000f2'
  const copy = (overrides: Partial<TransitionCopy>): TransitionCopy => ({
    locationId: rack,
    locationName: 'Rack 2',
    sectionName: null,
    locationKind: 'rack',
    locked: false,
    printingId: null,
    language: 'en',
    finish: null,
    condition: null,
    quantity: 1,
    reasons: [],
    ...overrides,
  })
  const plan: TransitionPlan = {
    deckId: variantId,
    assembledDeckId: deckId,
    assembledVersionId: version1,
    remove: [{ oracleId: solRing, quantity: 1 }],
    add: [
      { oracleId: cultivate, quantity: 2, take: [copy({ quantity: 1 }), copy({ locationId: binder, locationName: 'Binder', locked: true, language: 'fr' })], toBuy: 0, closest: [] },
      { oracleId: swords, quantity: 1, take: [], toBuy: 1, closest: [copy({ reasons: ['language'] })] },
    ],
    unlockFirst: [{ locationId: binder, locationName: 'Binder', sectionName: null }],
    totalToBuy: 1,
    cards,
    printings: {},
  }

  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.localStorage.clear()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('marks a version as the assembled version after the confirmation', async () => {
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      ...deckPage(deck()),
      [`PUT /decks/${deckId}/assembled`]: () => Response.json({ deckId, assembledVersionId: version2 }),
    })
    const user = userEvent.setup()
    open(`/decks/${deckId}`)

    const panel = await historyPanel()
    expect(within(panel).queryByRole('button', { name: 'Mark version 1 as the assembled version' })).not.toBeInTheDocument()
    await user.click(within(panel).getByRole('button', { name: 'Mark version 2 as the assembled version' }))
    await user.click(within(screen.getByRole('dialog', { name: 'Mark version 2 as the assembled version?' })).getByRole('button', { name: 'Mark as assembled' }))

    expect(await within(panel).findByText('Version 2 is now the assembled version.')).toBeInTheDocument()
    expect(calls.find((call) => call.key === `PUT /decks/${deckId}/assembled`)?.body).toEqual({ versionId: version2 })
    expect(within(panel).getByRole('link', { name: 'Transition plan' })).toHaveAttribute('href', `/decks/${deckId}/transition-plan`)
  })

  it('shows the steps of the transition plan with a check box for each step', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${variantId}`]: () => Response.json(variantDeck),
      [`GET /decks/${variantId}/transition-plan`]: () => Response.json(plan),
    })
    const user = userEvent.setup()
    open(`/decks/${variantId}/transition-plan`)

    expect(await screen.findByRole('heading', { name: 'Transition plan: Budget' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Binder' })).toHaveAttribute('href', `/collection/locations/${binder}`)
    const remove = screen.getByRole('region', { name: 'Cards to remove from the deck (1)' })
    expect(within(remove).getByLabelText('Remove 1 Sol Ring')).not.toBeChecked()
    const take = screen.getByRole('region', { name: 'Cards to take from the collection (2)' })
    expect(within(take).getAllByRole('checkbox')).toHaveLength(2)
    expect(within(take).getByText('Locked')).toBeInTheDocument()
    const buy = screen.getByRole('region', { name: 'Cards to buy (1)' })
    expect(within(buy).getByText('Closest copy: 1 copy in EN in Rack 2 (other language)')).toBeInTheDocument()

    expect(screen.getByText('Completed steps: 0 of 4')).toBeInTheDocument()
    await user.click(within(remove).getByLabelText('Remove 1 Sol Ring'))
    expect(screen.getByText('Completed steps: 1 of 4')).toBeInTheDocument()
    expect(window.localStorage.getItem(`reliquary.transition.${variantId}.${version1}`)).toBe(JSON.stringify([`remove:${solRing}`]))
  })

  it('tells the user when the main deck is not an assembled deck', async () => {
    mockApi({
      'POST /auth/refresh': () => session(),
      [`GET /decks/${variantId}`]: () => Response.json(variantDeck),
      [`GET /decks/${variantId}/transition-plan`]: () => problem(409, 'deck_not_assembled'),
    })
    open(`/decks/${variantId}/transition-plan`)

    expect(await screen.findByText('The main deck is not an assembled deck. Mark a version of the main deck as the assembled version first.')).toBeInTheDocument()
  })
})
