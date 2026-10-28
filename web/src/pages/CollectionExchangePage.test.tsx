import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import { detectCollectionFormat, exportPath, type CollectionLocation } from '../api/collection'
import i18n from '../i18n'
import { mockApi, session, testAccount } from '../test/mockApi'

const rackId = '0190a000-0000-7000-8000-0000000000a1'
const boxId = '0190a000-0000-7000-8000-0000000000a2'
const deckLocationId = '0190a000-0000-7000-8000-0000000000a3'

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

const locations = [
  location({ sections: [location({ id: boxId, parentId: rackId, name: 'Box A' })] }),
  location({ id: deckLocationId, name: 'Atraxa', kind: 'deck', deckId: 'deck-1' }),
]

describe('collection import and export', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('finds the format of a collection file and the path of an export', () => {
    expect(detectCollectionFormat('cards.dek', '<Deck />')).toBe('dek')
    expect(detectCollectionFormat('a.csv', 'Name,Set code,Collector number,Foil,Quantity,ManaBox ID\n')).toBe('manabox')
    expect(detectCollectionFormat('a.csv', '"sep=,"\nFolder Name,Quantity,Card Name\n')).toBe('dragonshield')
    expect(detectCollectionFormat('a.csv', 'Count,Tradelist Count,Name,Edition,Edition Code,Card Number\n')).toBe('deckbox')
    expect(detectCollectionFormat('a.csv', '"Count","Tradelist Count","Name","Edition","Condition"\n')).toBe('moxfield')
    expect(detectCollectionFormat('a.csv', 'Quantity,Name\n')).toBe('csv')
    expect(detectCollectionFormat('a.txt', '1x Sol Ring (slz) 114 [Ramp]\n')).toBe('archidekt')
    expect(detectCollectionFormat('a.txt', 'Deck\n1 Sol Ring (SLZ) 114\n')).toBe('mtga')
    expect(detectCollectionFormat('a.txt', '4 Sol Ring\n')).toBe('text')
    expect(exportPath('csv', '', true)).toBe('/collection/export?format=csv')
    expect(exportPath('dek', rackId, true)).toBe(`/collection/locations/${rackId}/export?format=dek&sections=true`)
    expect(exportPath('text', boxId, false)).toBe(`/collection/locations/${boxId}/export?format=text`)
  })

  it('imports a file into the selected location and shows the lines that it ignored', async () => {
    window.history.replaceState(null, '', `/collection/exchange?locationId=${boxId}`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json(locations),
      [`POST /collection/locations/${boxId}/import`]: () =>
        Response.json({
          batchId: 'batch-1',
          lineCount: 2,
          cardCount: 5,
          errors: [{ line: 4, code: 'card_not_found', text: '1 No Such Card' }],
          warnings: [{ line: 2, code: 'printing_not_found', text: '1 Sol Ring (XYZ) 1' }],
        }),
    })
    const user = userEvent.setup()
    render(<App />)

    const form = await screen.findByRole('form', { name: 'Import a file' })
    const target = within(form).getByLabelText('Location that gets the cards')
    expect(target).toHaveValue(boxId)
    expect(within(target).queryByRole('option', { name: 'Atraxa' })).not.toBeInTheDocument()
    const file = new File(['Name,Set code,Collector number,Foil,Quantity,ManaBox ID\nSol Ring,SLZ,114,normal,4,1\n'], 'ManaBox.csv', { type: 'text/csv' })
    await user.upload(within(form).getByLabelText('File'), file)
    expect(within(form).getByLabelText('File format')).toHaveValue('manabox')
    await user.selectOptions(within(form).getByLabelText('Origin'), 'purchase')
    await user.clear(within(form).getByLabelText('Currency'))
    await user.type(within(form).getByLabelText('Currency'), 'eur')
    await user.click(within(form).getByRole('button', { name: 'Import the cards' }))

    expect(await within(form).findByText('Reliquary added 5 cards to Rack 1 / Box A.')).toBeInTheDocument()
    expect(within(form).getByText('Line 4: no card has this name. "1 No Such Card"')).toBeInTheDocument()
    expect(within(form).getByText('Line 2: Reliquary found no printing for this set, number and language. The card has no printing. "1 Sol Ring (XYZ) 1"')).toBeInTheDocument()
    expect(calls.find((call) => call.key === `POST /collection/locations/${boxId}/import`)?.body).toEqual({
      format: 'manabox',
      content: 'Name,Set code,Collector number,Foil,Quantity,ManaBox ID\nSol Ring,SLZ,114,normal,4,1\n',
      origin: 'purchase',
      currency: 'EUR',
    })
  })

  it('downloads the export of a location with its sections', async () => {
    window.history.replaceState(null, '', '/collection/exchange')
    const createObjectURL = vi.fn<(blob: Blob) => string>(() => 'blob:collection')
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn<(url: string) => void>() }))
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    const exportUrls: string[] = []
    mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json(locations),
      [`GET /collection/locations/${rackId}/export`]: ({ url }) => {
        exportUrls.push(url.search)
        return Response.json({ fileName: 'Rack 1.dek', contentType: 'application/xml', content: '<Deck />' })
      },
    })
    const user = userEvent.setup()
    render(<App />)

    const form = await screen.findByRole('form', { name: 'Export the cards' })
    expect(within(form).getByLabelText('Cards to export')).toHaveValue('')
    expect(within(form).queryByLabelText('Include the cards of the sections')).not.toBeInTheDocument()
    await user.selectOptions(within(form).getByLabelText('Cards to export'), rackId)
    expect(within(form).getByLabelText('Include the cards of the sections')).toBeChecked()
    await user.selectOptions(within(form).getByLabelText('File format'), 'dek')
    expect(within(form).getByText('The file contains the quantity and the name of each card.')).toBeInTheDocument()
    await user.click(within(form).getByRole('button', { name: 'Export the file' }))

    await waitFor(() => expect(click).toHaveBeenCalled())
    expect(exportUrls).toEqual(['?format=dek&sections=true'])
    expect(createObjectURL).toHaveBeenCalled()
    click.mockRestore()
  })

  it('shows the page in French', async () => {
    await i18n.changeLanguage('fr')
    window.history.replaceState(null, '', '/collection/exchange')
    mockApi({
      'POST /auth/refresh': () => session('token-1', { ...testAccount, language: 'fr' }),
      'GET /collection/locations': () => Response.json(locations),
    })
    render(<App />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Import et export' })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Importer les cartes' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Toute la collection' })).toBeInTheDocument()
  })
})
