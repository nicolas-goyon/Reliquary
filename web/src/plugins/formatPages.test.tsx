import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import type { CollectionLocation } from '../api/collection'
import { AuthContext, type AuthContextValue } from '../auth/session'
import { DeckExportDialog } from '../components/DeckExportDialog'
import i18n from '../i18n'
import { registerCoreContributions } from '../slots/coreContributions'
import { SlotRegistry, SlotRegistryContext, type ConvertedFile, type PluginFormatContribution, type PluginFormatSlot } from '../slots/registry'
import { mockApi, session, testAccount } from '../test/mockApi'

const deckId = '0190a000-0000-7000-8000-0000000000d1'
const rackId = '0190a000-0000-7000-8000-0000000000a1'

const rack: CollectionLocation = {
  id: rackId,
  parentId: null,
  name: 'Rack 1',
  kind: 'rack',
  locked: false,
  pickable: true,
  deckId: null,
  position: 0,
  cardCount: 0,
  totalCardCount: 0,
  createdAt: '2026-10-28T10:00:00Z',
  updatedAt: '2026-10-28T10:00:00Z',
  sections: [],
}

/** A registry with the core contributions and one plugin format. The format converts with a fake function. */
function registryWithFormat(slot: PluginFormatSlot, convert: (content: string, fileName: string | null) => Promise<ConvertedFile>, development = false) {
  const registry = new SlotRegistry()
  registerCoreContributions(registry)
  const format: PluginFormatContribution = {
    kind: 'plugin-format',
    id: 'plugin.scanner.scan',
    order: 1000,
    plugin: { id: 'scanner', name: 'Scanner', development },
    title: { en: 'Scanner list', fr: 'Liste du scanner' },
    extensions: ['.scan'],
    convert: vi.fn<PluginFormatContribution['convert']>(convert),
  }
  registry.register(slot, format)
  return { registry, format }
}

function downloads() {
  const files: { name: string; blob: Blob }[] = []
  let last: Blob | null = null
  vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: (blob: Blob) => ((last = blob), 'blob:file'), revokeObjectURL: () => undefined }))
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    files.push({ name: this.download, blob: last! })
  })
  return files
}

describe('plugin formats in the import and export forms', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('imports a deck with a plugin format: the plugin converts the file, and the core imports the deck list', async () => {
    window.history.replaceState(null, '', '/decks/import')
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'POST /decks/import': () => Response.json({ deck: { id: deckId, name: 'Scanned' }, warnings: [] }, { status: 201 }),
      [`GET /decks/${deckId}`]: () => new Response(null, { status: 404 }),
    })
    const { registry, format } = registryWithFormat('deck.import-formats', async (content) => ({ content: `1 ${content.trim()}`, fileName: '', contentType: 'text/plain' }))
    const user = userEvent.setup()
    render(<App registry={registry} />)

    const input = await screen.findByLabelText('File')
    expect(input).toHaveAttribute('accept', '.txt,.dek,.csv,.dck,.scan')
    await user.upload(input, new File(['Sol Ring\n'], 'Scanned.scan', { type: 'text/plain' }))
    expect(screen.getByLabelText('File format')).toHaveValue('plugin:plugin.scanner.scan')
    expect(within(screen.getByRole('group', { name: 'Plugin formats' })).getByRole('option', { name: 'Scanner list' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Import the deck' }))

    await waitFor(() => expect(calls.some((call) => call.key === 'POST /decks/import')).toBe(true))
    expect(format.convert).toHaveBeenCalledWith('Sol Ring\n', 'Scanned.scan')
    expect(calls.find((call) => call.key === 'POST /decks/import')?.body).toMatchObject({ format: 'text', content: '1 Sol Ring', name: 'Scanned' })
  })

  it('shows an error and imports nothing when the plugin does not convert the file', async () => {
    window.history.replaceState(null, '', '/decks/import')
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const calls = mockApi({ 'POST /auth/refresh': () => session() })
    const { registry } = registryWithFormat('deck.import-formats', () => Promise.reject(new Error('Timeout')), true)
    const user = userEvent.setup()
    render(<App registry={registry} />)

    await user.type(await screen.findByLabelText('Content of the file'), 'Sol Ring')
    await user.selectOptions(screen.getByLabelText('File format'), 'Scanner list (not checked)')
    await user.click(screen.getByRole('button', { name: 'Import the deck' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The plugin did not convert the file.')
    expect(calls.some((call) => call.key === 'POST /decks/import')).toBe(false)
  })

  it('imports a collection file with a plugin format as a collection CSV', async () => {
    window.history.replaceState(null, '', `/collection/exchange?locationId=${rackId}`)
    const calls = mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([rack]),
      [`POST /collection/locations/${rackId}/import`]: () => Response.json({ batchId: 'b', lineCount: 1, cardCount: 1, errors: [], warnings: [] }),
    })
    const { registry } = registryWithFormat('collection.import-formats', async () => ({ content: 'Quantity,Name\n1,Sol Ring\n', fileName: '', contentType: 'text/plain' }))
    const user = userEvent.setup()
    render(<App registry={registry} />)

    const form = await screen.findByRole('form', { name: 'Import a file' })
    await user.type(within(form).getByLabelText('Content of the file'), 'scan data')
    await user.selectOptions(within(form).getByLabelText('File format'), 'Scanner list')
    await user.click(within(form).getByRole('button', { name: 'Import the cards' }))

    await waitFor(() => expect(calls.some((call) => call.key === `POST /collection/locations/${rackId}/import`)).toBe(true))
    expect(calls.find((call) => call.key === `POST /collection/locations/${rackId}/import`)?.body).toMatchObject({ format: 'csv', content: 'Quantity,Name\n1,Sol Ring\n' })
  })

  it('exports the collection with a plugin format: the core gets the CSV file, and the plugin gives its file', async () => {
    window.history.replaceState(null, '', '/collection/exchange')
    const files = downloads()
    const exportUrls: string[] = []
    mockApi({
      'POST /auth/refresh': () => session(),
      'GET /collection/locations': () => Response.json([rack]),
      'GET /collection/export': ({ url }) => {
        exportUrls.push(url.search)
        return Response.json({ fileName: 'collection.csv', contentType: 'text/csv', content: 'a,b' })
      },
    })
    const { registry, format } = registryWithFormat('collection.export-formats', async (content) => ({ content: content.replace(',', '\t'), fileName: 'collection.tsv', contentType: 'text/plain' }))
    const user = userEvent.setup()
    render(<App registry={registry} />)

    const form = await screen.findByRole('form', { name: 'Export the cards' })
    await user.selectOptions(within(form).getByLabelText('File format'), 'Scanner list')
    await user.click(within(form).getByRole('button', { name: 'Export the file' }))

    await waitFor(() => expect(files).toHaveLength(1))
    expect(exportUrls).toEqual(['?format=csv'])
    expect(format.convert).toHaveBeenCalledWith('a,b', null)
    expect(files[0].name).toBe('collection.tsv')
    expect(await files[0].blob.text()).toBe('a\tb')
  })

  it('exports a deck with a plugin format', async () => {
    const files = downloads()
    const paths: string[] = []
    const request = vi.fn<(path: string) => Promise<unknown>>(async (path: string) => {
      paths.push(path)
      return { fileName: 'deck.csv', contentType: 'text/csv', content: 'Quantity,Name' }
    })
    const { registry } = registryWithFormat('deck.export-formats', async () => ({ content: 'scan', fileName: 'deck.scan', contentType: 'text/plain' }))
    const onClose = vi.fn<() => void>()
    render(
      <SlotRegistryContext.Provider value={registry}>
        <AuthContext.Provider value={{ state: { status: 'authenticated', account: testAccount }, request } as unknown as AuthContextValue}>
          <DeckExportDialog deckId={deckId} onClose={onClose} />
        </AuthContext.Provider>
      </SlotRegistryContext.Provider>,
    )
    const user = userEvent.setup()

    await user.selectOptions(screen.getByLabelText('File format'), 'Scanner list')
    await user.click(screen.getByRole('button', { name: 'Download the file' }))

    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(paths).toEqual([`/decks/${deckId}/export?format=csv`])
    expect(files[0].name).toBe('deck.scan')
  })
})
