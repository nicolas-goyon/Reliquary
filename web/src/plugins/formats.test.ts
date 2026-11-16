import { afterEach, describe, expect, it } from 'vitest'
import { isPluginFormat, SlotRegistry, type PluginFormatContribution, type PluginFormatSlot } from '../slots/registry'
import { acceptWithPluginFormats, findPluginFormat, pluginFormatForFile, requirePluginFormat } from '../slots/pluginFormats'
import type { HostPeer } from './connection'
import { registerPluginFormats, safeFileName, type FormatPlugin } from './formats'
import type { HostMethods, PluginMethods } from './methods'
import { RpcPeer, type PortLike } from './rpc'

const peers: { close(): void }[] = []

const plugin: FormatPlugin = {
  id: 'scanner',
  name: 'Scanner',
  development: false,
  formats: [
    { id: 'scan', direction: 'import', target: 'deck', title: 'Scanner list', extensions: ['.scan', 'bad'] },
    { id: 'sheet', direction: 'export', target: 'collection', title: 'Sheet', extensions: [] },
  ],
  i18n: { fr: { 'formats.scan.title': 'Liste du scanner' } },
}

function setup() {
  const channel = new MessageChannel()
  const host: HostPeer = new RpcPeer<HostMethods, PluginMethods>(channel.port1 as unknown as PortLike)
  const frame = new RpcPeer<PluginMethods, HostMethods>(channel.port2 as unknown as PortLike)
  peers.push(host, frame)
  const registry = new SlotRegistry()
  const cleanup = registerPluginFormats(host, plugin, registry)
  const formats = (slot: PluginFormatSlot) => registry.list(slot).filter(isPluginFormat) as PluginFormatContribution[]
  return { frame, formats, cleanup }
}

afterEach(() => {
  for (const peer of peers.splice(0)) {
    peer.close()
  }
})

describe('plugin formats', () => {
  it('adds each format of the manifest to the slot of its target and direction', () => {
    const { formats } = setup()

    expect(formats('deck.import-formats')).toMatchObject([{ kind: 'plugin-format', id: 'plugin.scanner.scan', title: { en: 'Scanner list', fr: 'Liste du scanner' }, extensions: ['.scan'] }])
    expect(formats('collection.export-formats').map((format) => format.id)).toEqual(['plugin.scanner.sheet'])
    expect(formats('deck.export-formats')).toEqual([])
    expect(formats('collection.import-formats')).toEqual([])
  })

  it('sends an imported file to the plugin and gets the text to import', async () => {
    const { frame, formats } = setup()
    frame.handle('format.import', ({ id, content, fileName }) => ({ content: `${id}: ${content} from ${fileName}` }))

    const result = await formats('deck.import-formats')[0].convert('Sol Ring', 'deck.scan')

    expect(result.content).toBe('scan: Sol Ring from deck.scan')
  })

  it('checks the file of an export format', async () => {
    const { frame, formats } = setup()
    frame.handle('format.export', () => ({ fileName: '../../etc/passwd', contentType: 'text/html', content: 'a\tb' }))

    const file = await formats('collection.export-formats')[0].convert('a,b', null)

    expect(file).toEqual({ fileName: '.._.._etc_passwd', contentType: 'text/plain', content: 'a\tb' })
  })

  it('rejects an answer with no text', async () => {
    const { frame, formats } = setup()
    frame.handle('format.import', () => ({ lines: [] }))

    await expect(formats('deck.import-formats')[0].convert('x', null)).rejects.toMatchObject({ code: -32603 })
  })

  it('removes the formats of the plugin', () => {
    const { formats, cleanup } = setup()

    cleanup()

    expect(formats('deck.import-formats')).toEqual([])
    expect(formats('collection.export-formats')).toEqual([])
  })

  it('finds a plugin format from a select value or a file name, and adds its extensions to the file input', () => {
    const { formats } = setup()
    const imports = formats('deck.import-formats')

    expect(findPluginFormat(imports, 'plugin:plugin.scanner.scan')).toBe(imports[0])
    expect(findPluginFormat(imports, 'moxfield')).toBeUndefined()
    expect(pluginFormatForFile(imports, 'My Deck.SCAN')).toBe(imports[0])
    expect(acceptWithPluginFormats('.txt,.csv', imports)).toBe('.txt,.csv,.scan')
    expect(() => requirePluginFormat(imports, 'plugin:plugin.gone.scan')).toThrow(expect.objectContaining({ code: 'plugin_format_failed' }))
    expect(requirePluginFormat(imports, 'moxfield')).toBeUndefined()
  })

  it('makes safe file names', () => {
    expect(safeFileName('deck.dek', 'x.txt')).toBe('deck.dek')
    expect(safeFileName('a\u0000b:c', 'x.txt')).toBe('a_b_c')
    expect(safeFileName('..', 'x.txt')).toBe('x.txt')
    expect(safeFileName(42, 'x.txt')).toBe('x.txt')
    expect(safeFileName('n'.repeat(150), 'x.txt')).toHaveLength(100)
  })
})
