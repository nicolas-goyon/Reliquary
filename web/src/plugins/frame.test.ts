import { describe, expect, it } from 'vitest'
import { createPluginFrame, frameAllow, frameUrl } from './frame'

describe('plugin frame', () => {
  it('gives no device to a frame with no accepted device', () => {
    const frame = createPluginFrame(document, { id: 'deck-stats', version: '1.0.0' }, frameUrl({ id: 'deck-stats', version: '1.0.0' }, null))

    expect(frame.getAttribute('allow')).toBe('')
    expect(frame.getAttribute('sandbox')).toBe('allow-scripts')
    expect(frame.getAttribute('referrerpolicy')).toBe('no-referrer')
  })

  it('gives only the accepted devices to the frame', () => {
    const plugin = { id: 'card-scan', version: '1.0.0' }

    const camera = createPluginFrame(document, plugin, frameUrl(plugin, null, { network: ['http://localhost:7000'], devices: ['camera'] }))
    const both = createPluginFrame(document, plugin, frameUrl(plugin, 'https://plugins.example.org', { network: [], devices: ['camera', 'loopback-network'] }), 'Scan')

    expect(camera.getAttribute('allow')).toBe('camera')
    expect(both.getAttribute('allow')).toBe('camera; loopback-network; local-network-access')
  })

  it('ignores an unknown device and an address that is not correct', () => {
    expect(frameAllow('/api/v1/plugins/x/1.0.0/frame?devices=microphone&devices=camera&devices=camera')).toBe('camera')
    expect(frameAllow('/api/v1/plugins/development/frame?entry=http%3A%2F%2Flocalhost%3A5173%2Fmain.ts&devices=loopback-network')).toBe('loopback-network; local-network-access')
    expect(frameAllow('http://[')).toBe('')
  })
})
