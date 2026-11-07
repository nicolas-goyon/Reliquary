import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../../App'
import type { Deck, DeckLine } from '../../api/decks'
import i18n from '../../i18n'
import { downloadFile, makeProxyPdf } from '../../proxies/pdf'
import { atraxa, deckId, solRing, testDeck } from '../../test/fixtures'
import { mockApi, session } from '../../test/mockApi'

// The test replaces the PDF function. The tests of src/proxies check the PDF itself.
vi.mock('../../proxies/pdf', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../proxies/pdf')>()),
  makeProxyPdf: vi.fn<typeof makeProxyPdf>(),
  downloadFile: vi.fn<typeof downloadFile>(),
}))

const delver = '0190a000-0000-7000-8000-0000000000c5'
const treasure = '0190a000-0000-7000-8000-0000000000c6'

function line(id: string, oracleId: string, zone: DeckLine['zone'], quantity: number, printingId: string | null = null): DeckLine {
  return { id, oracleId, printingId, finish: null, quantity, zone, note: '', tagIds: [], categories: [] }
}

function deck(): Deck {
  const base = testDeck()
  return testDeck({
    isOwner: false,
    visibility: 'public',
    counts: { commander: 1, companion: 0, main: 5, sideboard: 0, considering: 0, tokens: 2 },
    lines: [line('l1', atraxa, 'commander', 1), line('l2', solRing, 'main', 4), line('l3', delver, 'main', 1, 'p-delver'), line('l4', treasure, 'tokens', 2)],
    cards: {
      ...base.cards,
      [delver]: { ...base.cards[solRing], name: 'Delver of Secrets', layout: 'transform', faces: [{ name: 'Delver of Secrets', manaCost: null, typeLine: null }, { name: 'Insectile Aberration', manaCost: null, typeLine: null }] },
      [treasure]: { ...base.cards[solRing], name: 'Treasure' },
    },
    printings: { 'p-delver': { setCode: 'isd', collectorNumber: '51', lang: 'en', imageUri: 'https://cards.scryfall.io/normal/front/d/e/delver.jpg' } },
  })
}

function handlers() {
  return {
    'POST /auth/refresh': () => session(),
    [`GET /decks/${deckId}`]: () => Response.json(deck()),
    [`GET /decks/${deckId}/legality`]: () => Response.json({ format: 'commander', legal: true, problems: [] }),
    [`GET /decks/${deckId}/analysis`]: () => Response.json({ status: 500, code: 'internal_error', detail: 'x' }, { status: 500 }),
    'GET /printings/p-delver': () =>
      Response.json({
        imageUris: {},
        faces: [
          { name: 'Delver of Secrets', printedName: null, imageUris: { large: 'https://cards.scryfall.io/large/front/d/e/delver.jpg' } },
          { name: 'Insectile Aberration', printedName: null, imageUris: { large: 'https://cards.scryfall.io/large/back/d/e/delver.jpg' } },
        ],
      }),
  }
}

const pdfResult = { blob: new Blob(['%PDF'], { type: 'application/pdf' }), pages: 1, failed: [] as string[] }

async function openDialog() {
  const user = userEvent.setup()
  render(<App />)
  await user.click(await screen.findByRole('button', { name: 'Print proxies' }))
  return { user, dialog: screen.getByRole('dialog', { name: 'Print proxies' }) }
}

describe('proxy dialog', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    window.history.replaceState(null, '', `/decks/${deckId}`)
    vi.mocked(makeProxyPdf).mockReset()
    vi.mocked(downloadFile).mockReset()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('prints all the cards of the deck for a user who is not the owner, with the quantities and the tokens zone', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    mockApi(handlers())
    vi.mocked(makeProxyPdf).mockImplementation(async (_items, options) => {
      options.onProgress?.(1, 3)
      return pdfResult
    })
    const { user, dialog } = await openDialog()

    expect(within(dialog).getByLabelText('Paper size')).toHaveValue('letter')
    expect(within(dialog).getByText('6 cards · 1 page')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('checkbox', { name: 'Add the zone Tokens' }))
    expect(within(dialog).getByText('8 cards · 1 page')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Make the PDF' }))

    expect(await within(dialog).findByText('The PDF is ready. The browser downloads the file.')).toBeInTheDocument()
    const [items, options] = vi.mocked(makeProxyPdf).mock.calls[0]
    expect(options.paper).toBe('letter')
    expect(items.map((item) => [item.name, item.quantity])).toEqual([
      ["Atraxa, Praetors' Voice", 1],
      ['Delver of Secrets', 1],
      ['Sol Ring', 4],
      ['Treasure', 2],
    ])
    expect(items[1].imageUri).toBe('https://cards.scryfall.io/large/front/d/e/delver.jpg')
    expect(downloadFile).toHaveBeenCalledWith(pdfResult.blob, 'Atraxa superfriends - proxies.pdf')
  })

  it('prints the selected lines, with the back faces, on A4 paper', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('fr-FR')
    const calls = mockApi(handlers())
    vi.mocked(makeProxyPdf).mockResolvedValue(pdfResult)
    const { user, dialog } = await openDialog()

    expect(within(dialog).getByLabelText('Paper size')).toHaveValue('a4')
    await user.click(within(dialog).getByRole('radio', { name: 'Selected cards' }))
    expect(within(dialog).getByText('Select one card or more.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Make the PDF' })).toBeDisabled()
    await user.click(within(dialog).getByRole('checkbox', { name: /1 × Delver of Secrets/ }))
    await user.click(within(dialog).getByRole('checkbox', { name: 'Add the back face of each double-faced card' }))
    await user.click(within(dialog).getByRole('button', { name: 'Make the PDF' }))

    await waitFor(() => expect(makeProxyPdf).toHaveBeenCalled())
    expect(calls.some((call) => call.key === 'GET /printings/p-delver')).toBe(true)
    const [items, options] = vi.mocked(makeProxyPdf).mock.calls[0]
    expect(options.paper).toBe('a4')
    expect(items.map((item) => item.name)).toEqual(['Delver of Secrets', 'Insectile Aberration'])
  })

  it('shows the progress, then the cards whose image did not load', async () => {
    mockApi(handlers())
    let finish: (value: typeof pdfResult) => void = () => undefined
    vi.mocked(makeProxyPdf).mockImplementation((_items, options) => {
      options.onProgress?.(2, 5)
      return new Promise((resolve) => {
        finish = resolve
      })
    })
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole('button', { name: 'Make the PDF' }))

    expect(await within(dialog).findByText('The application loads the card images: 2 of 5.')).toBeInTheDocument()
    expect(within(dialog).getByRole('progressbar', { name: 'Card images' })).toHaveAttribute('value', '2')
    finish({ ...pdfResult, failed: ['Sol Ring'] })

    const alert = await within(dialog).findByRole('alert')
    expect(alert).toHaveTextContent('1 card image did not load. The PDF shows a frame with the card name in its place.')
    expect(alert).toHaveTextContent('Sol Ring')
    expect(downloadFile).toHaveBeenCalled()
  })

  it('shows an error when the PDF fails, and closes with Escape', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
    mockApi(handlers())
    vi.mocked(makeProxyPdf).mockRejectedValue(new Error('chunk'))
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole('button', { name: 'Make the PDF' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('The application cannot make the PDF.')
    expect(downloadFile).not.toHaveBeenCalled()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Print proxies' })).not.toBeInTheDocument()
  })
})
