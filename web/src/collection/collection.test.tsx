import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { CollectionEntry, CollectionLocation } from '../api/collection'
import { cardViewQuery } from '../api/collection'
import i18n from '../i18n'
import { mockApi, problem, session } from '../test/mockApi'

const rackId = '0190a000-0000-7000-8000-0000000000a1'
const boxId = '0190a000-0000-7000-8000-0000000000a2'
const solRing = '0190a000-0000-7000-8000-0000000000c1'

function location(overrides: Partial<CollectionLocation> = {}): CollectionLocation {
  return {
    id: rackId,
    parentId: null,
    name: 'Rack 1',
    kind: 'rack',
    locked: false,
    pickable: true,
    deckId: null,
    position: 0,
    cardCount: 2,
    totalCardCount: 3,
    createdAt: '2026-10-28T10:00:00Z',
    updatedAt: '2026-10-28T10:00:00Z',
    sections: [],
    ...overrides,
  }
}

function entry(overrides: Partial<CollectionEntry> = {}): CollectionEntry {
  return {
    id: 'e1',
    locationId: rackId,
    oracleId: solRing,
    name: 'Sol Ring',
    printingId: null,
    setCode: 'cmr',
    collectorNumber: '472',
    imageUri: null,
    language: 'en',
    quantity: 2,
    ...overrides,
  }
}

const rackWithBox = location({
  sections: [location({ id: boxId, parentId: rackId, name: 'Box A', cardCount: 1, totalCardCount: 1 })],
})

describe('collection', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('makes the query of the global view with the filters only', () => {
    expect(cardViewQuery({ q: ' sol ', locationId: '', language: 'fr', set: '' }, 'next')).toBe('q=sol&language=fr&cursor=next')
  })

  it('shows the copies of each card with their locations', async () => {
    window.history.replaceState(null, '', '/')
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /health': () => Response.json({ status: 'ok', version: '0.1.0' }),
      'GET /collection/locations': () => Response.json([rackWithBox]),
      'GET /collection/settings': () => Response.json({ advancedFields: [] }),
      'GET /collection/cards': () =>
        Response.json({
          items: [
            {
              oracleId: solRing,
              name: 'Sol Ring',
              quantity: 4,
              imageUri: null,
              locations: [
                { locationId: rackId, locationName: 'Rack 1', sectionName: null, kind: 'rack', quantity: 2 },
                { locationId: boxId, locationName: 'Rack 1', sectionName: 'Box A', kind: 'rack', quantity: 1 },
                { locationId: 'deck', locationName: 'Atraxa', sectionName: null, kind: 'deck', quantity: 1 },
              ],
            },
          ],
          nextCursor: null,
        }),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('link', { name: 'Collection' }))
    const row = (await screen.findByRole('link', { name: 'Sol Ring' })).closest('tr')!

    expect(within(row).getByText('4')).toBeInTheDocument()
    expect(within(row).getByRole('link', { name: 'Rack 1 / Box A' })).toBeInTheDocument()
    expect(within(row).getByRole('link', { name: 'Atraxa' })).toBeInTheDocument()
    expect(within(row).getByText('(2 copies)')).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('Language', { selector: '#collection-language' }), 'fr')
    await user.click(screen.getByRole('button', { name: 'Apply the filters' }))
    await waitFor(() => expect(calls.filter((call) => call.key === 'GET /collection/cards')).toHaveLength(2))
  })

  it('creates a section in a location', async () => {
    window.history.replaceState(null, '', '/collection/locations')
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([location()]),
      'GET /collection/storage-rules': () => Response.json([]),
      'POST /collection/locations': () => Response.json(location({ id: boxId }), { status: 201 }),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText('Name'), 'Box A')
    await user.selectOptions(screen.getByLabelText('Parent location'), rackId)
    expect(screen.queryByLabelText('Kind')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Create' }))

    await waitFor(() => expect(calls.find((call) => call.key === 'POST /collection/locations')?.body).toEqual({ name: 'Box A', parentId: rackId }))
  })

  it('locks a location and shows the error of a locked location', async () => {
    window.history.replaceState(null, '', '/collection/locations')
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([location()]),
      'GET /collection/storage-rules': () => Response.json([]),
      [`POST /collection/locations/${rackId}/lock`]: () => Response.json(location({ locked: true })),
      [`DELETE /collection/locations/${rackId}`]: () => problem(409, 'location_locked'),
    })
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Lock' }))
    await waitFor(() => expect(calls.some((call) => call.key === `POST /collection/locations/${rackId}/lock`)).toBe(true))
    await user.click(screen.getByRole('button', { name: 'Delete' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The location is locked. Unlock the location, then try again.')
  })

  it('adds a card to a location and hides the disabled advanced fields', async () => {
    window.history.replaceState(null, '', `/collection/locations/${rackId}`)
    let fields: string[] = []
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([location()]),
      'GET /collection/settings': () => Response.json({ advancedFields: fields }),
      [`GET /collection/locations/${rackId}/entries`]: () => Response.json({ items: [entry()], nextCursor: null }),
      'GET /cards/autocomplete': () => Response.json([{ oracleId: solRing, name: 'Sol Ring', matchedName: 'Sol Ring', lang: 'en' }]),
      'GET /collection/storage-rules/suggestion': () =>
        Response.json({ id: 'r1', locationId: boxId, locationName: 'Rack 2', sectionName: 'Artifacts', query: 't:artifact', priority: 0 }),
      [`POST /collection/locations/${rackId}/entries`]: () => Response.json({ batchId: 'b1', entries: [entry({ quantity: 3 })] }),
      'PATCH /collection/entries/e1': () => Response.json({ batchId: 'b2', entries: [entry({ quantity: 3 })] }),
    })
    const user = userEvent.setup()
    const { unmount } = render(<App />)

    expect(await screen.findByRole('link', { name: 'Sol Ring' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Finish')).not.toBeInTheDocument()
    await user.type(screen.getByRole('combobox', { name: 'Card' }), 'sol')
    await user.click(await screen.findByRole('option', { name: /Sol Ring/ }))
    expect(await screen.findByRole('link', { name: 'A storage rule suggests this location for the card: Rack 2 / Artifacts.' })).toHaveAttribute('href', `/collection/locations/${boxId}`)
    await user.clear(screen.getByLabelText('Quantity'))
    await user.type(screen.getByLabelText('Quantity'), '2')
    await user.selectOptions(screen.getByLabelText('Origin'), 'purchase')
    await user.click(screen.getByRole('button', { name: 'Add' }))
    await waitFor(() =>
      expect(calls.find((call) => call.key === `POST /collection/locations/${rackId}/entries`)?.body).toEqual({ entries: [{ oracleId: solRing, quantity: 2 }], origin: 'purchase' }),
    )

    await user.click(screen.getByRole('button', { name: 'Add one copy of Sol Ring' }))
    await waitFor(() => expect(calls.find((call) => call.key === 'PATCH /collection/entries/e1')?.body).toEqual({ quantity: 3 }))
    unmount()

    fields = ['finish', 'note']
    render(<App />)
    expect(await screen.findByLabelText('Finish')).toBeInTheDocument()
    expect(screen.getByLabelText('Note')).toBeInTheDocument()
    expect(screen.queryByLabelText('Condition')).not.toBeInTheDocument()
  })

  it('shows no form in a locked location', async () => {
    window.history.replaceState(null, '', `/collection/locations/${rackId}`)
    mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([location({ locked: true })]),
      'GET /collection/settings': () => Response.json({ advancedFields: [] }),
      [`GET /collection/locations/${rackId}/entries`]: () => Response.json({ items: [entry()], nextCursor: null }),
    })
    render(<App />)

    expect(await screen.findByText('This location is locked. Unlock the location to change its content.')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Card' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add one copy of Sol Ring' })).not.toBeInTheDocument()
  })

  it('adds a storage rule', async () => {
    window.history.replaceState(null, '', '/collection/locations')
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([location()]),
      'GET /collection/storage-rules': () => Response.json([]),
      'POST /collection/storage-rules': () => problem(400, 'validation_failed', [{ field: 'query', code: 'invalid_format', message: 'Syntax' }]),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByText('Storage rules'))
    await user.type(screen.getByLabelText('Card search'), 't:(')
    await user.selectOptions(screen.getByLabelText('Location', { selector: '#rule-location' }), rackId)
    await user.click(screen.getByRole('button', { name: 'Add the rule' }))

    expect(await screen.findByText('This value does not have the correct format.')).toBeInTheDocument()
    expect(calls.find((call) => call.key === 'POST /collection/storage-rules')?.body).toEqual({ locationId: rackId, query: 't:(', priority: 0 })
  })

  it('marks a token of an assembled deck as owned', async () => {
    window.history.replaceState(null, '', `/collection/locations/${rackId}`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([location({ kind: 'deck', deckId: 'd1', pickable: false })]),
      'GET /collection/settings': () => Response.json({ advancedFields: [] }),
      [`GET /collection/locations/${rackId}/entries`]: () => Response.json({ items: [], nextCursor: null }),
      [`GET /collection/locations/${rackId}/tokens`]: () => Response.json([{ oracleId: 'goblin', name: 'Goblin', owned: false }]),
      [`PUT /collection/locations/${rackId}/tokens/goblin`]: () => Response.json([{ oracleId: 'goblin', name: 'Goblin', owned: true }]),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('checkbox', { name: /Goblin/ }))

    await waitFor(() => expect(screen.getByRole('checkbox', { name: /Goblin/ })).toBeChecked())
    expect(calls.find((call) => call.key === `PUT /collection/locations/${rackId}/tokens/goblin`)?.body).toEqual({ owned: true })
    expect(screen.getByText('This location is an assembled deck. Change the deck to change its cards.')).toBeInTheDocument()
  })

  it('moves the selected cards to another location in one request', async () => {
    window.history.replaceState(null, '', `/collection/locations/${rackId}`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([location(), location({ id: boxId, name: 'Binder', kind: 'binder' })]),
      'GET /collection/settings': () => Response.json({ advancedFields: [] }),
      [`GET /collection/locations/${rackId}/entries`]: () => Response.json({ items: [entry(), entry({ id: 'e2', name: 'Forest', quantity: 8 })], nextCursor: null }),
      'POST /collection/movements': () => Response.json({ batchId: 'b1', movements: [] }),
    })
    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('checkbox', { name: 'Select all the cards' }))
    expect(screen.getByText('2 entries selected. The movement takes all their copies.')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Destination'), `location:${boxId}`)
    await user.click(screen.getByRole('button', { name: 'Move the cards' }))

    await waitFor(() =>
      expect(calls.find((call) => call.key === 'POST /collection/movements')?.body).toEqual({
        origin: { kind: 'location', locationId: rackId },
        destination: { kind: 'location', locationId: boxId },
        cards: [{ entryId: 'e1' }, { entryId: 'e2' }],
      }),
    )
  })

  it('shows the history of the movements', async () => {
    window.history.replaceState(null, '', `/collection/movements?locationId=${rackId}`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([location()]),
      'GET /collection/movements': () =>
        Response.json({
          items: [
            {
              id: 'm1',
              batchId: 'b1',
              kind: 'move',
              origin: { kind: 'location', locationId: rackId, locationName: 'Rack 1' },
              destination: { kind: 'location', locationId: 'gone', locationName: null },
              oracleId: solRing,
              name: 'Sol Ring',
              printingId: null,
              language: 'en',
              quantity: 2,
              inventoryId: null,
              createdAt: '2026-10-28T10:00:00Z',
            },
            {
              id: 'm2',
              batchId: 'b0',
              kind: 'add',
              origin: { kind: 'purchase', locationId: null, locationName: null },
              destination: { kind: 'location', locationId: rackId, locationName: 'Rack 1' },
              oracleId: solRing,
              name: 'Sol Ring',
              printingId: null,
              language: 'en',
              quantity: 4,
              inventoryId: null,
              createdAt: '2026-10-27T10:00:00Z',
            },
          ],
          nextCursor: null,
        }),
    })
    render(<App />)

    const moved = (await screen.findByText('Moved')).closest('tr')!
    expect(within(moved).getByText('Deleted location')).toBeInTheDocument()
    const added = screen.getByText('Added').closest('tr')!
    expect(within(added).getByText('Purchase')).toBeInTheDocument()
    expect(calls.find((call) => call.key === 'GET /collection/movements')).toBeDefined()
  })

  it('makes an inventory with a group answer', async () => {
    window.history.replaceState(null, '', `/collection/locations/${rackId}/inventory`)
    const difference = (key: string, name: string, quantity: number, expected: number, counted: number) => ({
      key,
      card: { oracleId: key, name, printingId: null, setCode: null, language: 'en', quantity },
      expected,
      counted,
    })
    const open = {
      id: 'i1',
      locationId: rackId,
      status: 'open',
      createdAt: '2026-10-28T10:00:00Z',
      appliedAt: null,
      cardCount: 3,
      entries: [],
      differences: [difference('k1', 'Sol Ring', 1, 2, 3), difference('k2', 'Forest', -2, 2, 0)],
    }
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([location(), location({ id: boxId, name: 'Binder', kind: 'binder' })]),
      [`GET /collection/locations/${rackId}/entries`]: () =>
        Response.json({ items: [entry(), entry({ id: 'e2', oracleId: 'forest', name: 'Forest', setCode: null, quantity: 2 })], nextCursor: null }),
      [`GET /collection/locations/${rackId}/inventories`]: () => Response.json([]),
      [`POST /collection/locations/${rackId}/inventories`]: () => Response.json(open, { status: 201 }),
      'POST /collection/inventories/i1/apply': () => Response.json({ ...open, status: 'applied', differences: [] }),
    })
    const user = userEvent.setup()
    render(<App />)

    const solRingCount = await screen.findByLabelText('Counted copies of Sol Ring')
    await user.clear(solRingCount)
    await user.type(solRingCount, '3')
    await user.clear(screen.getByLabelText('Counted copies of Forest'))
    await user.click(screen.getByRole('button', { name: 'Compare with the location' }))
    await waitFor(() =>
      expect(calls.find((call) => call.key === `POST /collection/locations/${rackId}/inventories`)?.body).toEqual({
        entries: [{ oracleId: solRing, language: 'en', quantity: 3 }],
      }),
    )

    await user.selectOptions(await screen.findByLabelText('Origin or destination of Sol Ring'), `location:${boxId}`)
    await user.selectOptions(screen.getByLabelText('Destination of all the removed cards'), 'sale')
    await user.click(screen.getByRole('button', { name: 'Apply the inventory' }))

    expect(await screen.findByText('Reliquary applied the inventory. The location has the counted content.')).toBeInTheDocument()
    expect(calls.find((call) => call.key === 'POST /collection/inventories/i1/apply')?.body).toEqual({
      answers: [{ key: 'k1', kind: 'location', locationId: boxId }],
      destination: { kind: 'sale' },
    })
  })
})
