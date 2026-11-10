import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PluginHost, PluginLimitError, type PluginHostEvent, type PluginHostOptions } from './host'
import type { HostMethods, PluginMethods } from './methods'
import { connectMessageType, readyMessageType } from './protocol'
import { RpcPeer, type PortLike } from './rpc'

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** The core side of the tests: a host with a container, a fake window of the core and the list of the events. */
function createHost(options: Partial<PluginHostOptions> = {}) {
  const container = document.createElement('div')
  document.body.append(container)
  const hostWindow = new EventTarget() as unknown as Window
  const events: PluginHostEvent[] = []
  const host = new PluginHost({
    container,
    settings: { maxActive: 2, frameOrigin: null, developmentPlugins: false },
    coreApi: '1.1.0',
    language: () => 'fr',
    hostWindow,
    onEvent: (event) => events.push(event),
    ...options,
  })
  return { container, hostWindow, host, events }
}

type Host = ReturnType<typeof createHost>

function frameOf(context: Host, id: string): HTMLIFrameElement | null {
  return context.container.querySelector(`iframe[data-plugin="${id}"]`)
}

function sendReady(context: Host, source: unknown): void {
  context.hostWindow.dispatchEvent(Object.assign(new Event('message'), { source, data: { type: readyMessageType, coreApi: '1.0.0' } }))
}

/**
 * Starts a plugin with a fake frame: the test plays the code of the plugin on the port that the core sends.
 * The fake plugin answers the heartbeats when `answerPings` is true.
 */
async function startFakePlugin(context: Host, id: string, answerPings = true) {
  const started = context.host.start({ id, version: '1.0.0' })
  const frameWindow = frameOf(context, id)!.contentWindow!
  const posted: { message: unknown; port: MessagePort }[] = []
  vi.spyOn(frameWindow, 'postMessage').mockImplementation(((message: unknown, _origin: string, transfer: Transferable[]) => {
    posted.push({ message, port: transfer[0] as MessagePort })
  }) as typeof frameWindow.postMessage)
  sendReady(context, frameWindow)
  await started
  const plugin = new RpcPeer<PluginMethods, HostMethods>(posted[0].port as unknown as PortLike)
  const deactivated = vi.fn<() => null>(() => null)
  if (answerPings) {
    plugin.handle('plugin.ping', () => null)
  }
  plugin.handle('plugin.deactivate', deactivated)
  return { plugin, posted, frameWindow, deactivated }
}

describe('PluginHost', () => {
  beforeEach(() => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  })

  afterEach(() => {
    vi.restoreAllMocks()
    document.body.replaceChildren()
  })

  it('creates a sandboxed frame with scripts only, no device and the frame address of the plugin version', () => {
    const context = createHost()

    void context.host.start({ id: 'moxfield-sync', version: '0.1.0' })
    const frame = frameOf(context, 'moxfield-sync')!

    expect(frame.getAttribute('sandbox')).toBe('allow-scripts')
    expect(frame.getAttribute('allow')).toBe('')
    expect(frame.getAttribute('referrerpolicy')).toBe('no-referrer')
    expect(frame.getAttribute('src')).toBe('/api/v1/plugins/moxfield-sync/0.1.0/frame')
    void context.host.stopAll()
  })

  it('uses the frame origin of the instance', () => {
    const context = createHost({ settings: { maxActive: 8, frameOrigin: 'https://plugins.example.org/', developmentPlugins: false } })

    void context.host.start({ id: 'moxfield-sync', version: '0.1.0' })

    expect(frameOf(context, 'moxfield-sync')!.getAttribute('src')).toBe('https://plugins.example.org/api/v1/plugins/moxfield-sync/0.1.0/frame')
    void context.host.stopAll()
  })

  it('sends the port one time, and only after a ready message from the frame', async () => {
    const context = createHost()
    const started = context.host.start({ id: 'moxfield-sync', version: '0.1.0' })
    const frameWindow = frameOf(context, 'moxfield-sync')!.contentWindow!
    const postMessage = vi.spyOn(frameWindow, 'postMessage').mockImplementation(() => undefined)

    sendReady(context, window)
    sendReady(context, null)
    expect(postMessage).not.toHaveBeenCalled()

    sendReady(context, frameWindow)
    sendReady(context, frameWindow)
    await started

    expect(postMessage).toHaveBeenCalledTimes(1)
    const [message, origin, transfer] = postMessage.mock.calls[0] as unknown as [unknown, string, Transferable[]]
    expect(message).toEqual({ type: connectMessageType, coreApi: '1.1.0', plugin: { id: 'moxfield-sync', version: '0.1.0' }, language: 'fr', development: false })
    expect(origin).toBe('*')
    expect(transfer).toHaveLength(1)
    expect(context.events).toEqual([{ type: 'started', plugin: { id: 'moxfield-sync', version: '0.1.0' }, development: false }])
    ;(transfer[0] as MessagePort).close()
    await context.host.stopAll()
  })

  it('refuses a plugin over the limit of active plugins', async () => {
    const context = createHost({ settings: { maxActive: 2, frameOrigin: null, developmentPlugins: false } })
    const first = await startFakePlugin(context, 'first')
    const second = await startFakePlugin(context, 'second')

    await expect(context.host.start({ id: 'third', version: '1.0.0' })).rejects.toBeInstanceOf(PluginLimitError)

    expect(frameOf(context, 'third')).toBeNull()
    expect(context.host.running).toEqual(['first', 'second'])
    await context.host.stopAll()
    first.plugin.close()
    second.plugin.close()
  })

  it('does not create a second frame for a running plugin', async () => {
    const context = createHost()
    const { plugin } = await startFakePlugin(context, 'moxfield-sync')

    await context.host.start({ id: 'moxfield-sync', version: '1.0.0' })

    expect(context.container.querySelectorAll('iframe')).toHaveLength(1)
    await context.host.stopAll()
    plugin.close()
  })

  it('asks the plugin to stop, then removes its frame', async () => {
    const context = createHost()
    const { deactivated } = await startFakePlugin(context, 'moxfield-sync')

    await context.host.stop('moxfield-sync')

    expect(deactivated).toHaveBeenCalledTimes(1)
    expect(frameOf(context, 'moxfield-sync')).toBeNull()
    expect(context.events.at(-1)).toEqual({ type: 'stopped', plugin: { id: 'moxfield-sync', version: '1.0.0' }, reason: 'stopped' })
  })

  it('removes the frame of a plugin that does not answer the heartbeats', async () => {
    const context = createHost({ intervalMs: 20, maxMissed: 3 })
    const alive = await startFakePlugin(context, 'alive')
    const blocked = await startFakePlugin(context, 'blocked', false)

    await wait(200)

    expect(frameOf(context, 'blocked')).toBeNull()
    expect(frameOf(context, 'alive')).not.toBeNull()
    expect(context.events).toContainEqual({ type: 'stopped', plugin: { id: 'blocked', version: '1.0.0' }, reason: 'unresponsive' })
    expect(context.host.running).toEqual(['alive'])
    await context.host.stopAll()
    alive.plugin.close()
    blocked.plugin.close()
  })

  it('starts a development plugin with its own frame address and the development flag', async () => {
    const context = createHost()
    const started = context.host.start({ id: 'dev', version: '0.0.1' }, { src: '/api/v1/plugins/development/frame?entry=x', development: true })
    const frame = frameOf(context, 'dev')!
    const postMessage = vi.spyOn(frame.contentWindow!, 'postMessage').mockImplementation(() => undefined)

    sendReady(context, frame.contentWindow)
    await started

    expect(frame.getAttribute('src')).toBe('/api/v1/plugins/development/frame?entry=x')
    expect(frame.getAttribute('sandbox')).toBe('allow-scripts')
    expect(postMessage.mock.calls[0][0]).toMatchObject({ development: true })
    expect(context.host.isDevelopment('dev')).toBe(true)
    ;((postMessage.mock.calls[0] as unknown as [unknown, string, MessagePort[]])[2][0]).close()
    await context.host.stopAll()
  })

  it('removes the frame when the frame sends no ready message in the time limit', async () => {
    const context = createHost({ connectTimeoutMs: 20 })

    await context.host.start({ id: 'silent', version: '1.0.0' })

    expect(frameOf(context, 'silent')).toBeNull()
    expect(context.events).toEqual([{ type: 'stopped', plugin: { id: 'silent', version: '1.0.0' }, reason: 'connect-failed' }])
  })
})
