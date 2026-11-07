import { describe, expect, it, vi, type Mock } from 'vitest'
import workerSource from './service-worker.js?raw'

const origin = 'https://reliquary.test'
const buildFiles = ['/index.html', '/assets/index-abc.js', '/manifest.webmanifest']

interface FakeRequest {
  url: string
  method: string
  mode: string
}

interface FetchEvent {
  request: FakeRequest
  respondWith: Mock<(response: Promise<Response>) => void>
}

/** A cache in memory, with the methods that the worker uses. */
class FakeCache {
  readonly entries = new Map<string, Response>()
  private readonly fetchFile: (path: string) => Promise<Response>

  constructor(fetchFile: (path: string) => Promise<Response>) {
    this.fetchFile = fetchFile
  }

  async match(request: FakeRequest | string): Promise<Response | undefined> {
    return this.entries.get(typeof request === 'string' ? new URL(request, origin).pathname : new URL(request.url).pathname)?.clone()
  }

  async put(request: FakeRequest, response: Response): Promise<void> {
    this.entries.set(new URL(request.url).pathname, response)
  }

  async addAll(paths: string[]): Promise<void> {
    for (const path of paths) {
      this.entries.set(path, await this.fetchFile(path))
    }
  }
}

/** Runs the worker code with a fake global scope, a fake cache storage and a fake network. */
function startWorker() {
  const handlers = new Map<string, (event: unknown) => void>()
  const network = vi.fn<(request: FakeRequest | string) => Promise<Response>>(async (request) => new Response(`network ${typeof request === 'string' ? request : request.url}`))
  const caches = new Map<string, FakeCache>([['reliquary-shell-old', new FakeCache(async () => new Response('old'))]])
  const cacheStorage = {
    open: vi.fn<(name: string) => Promise<FakeCache>>(async (name) => {
      const cache = caches.get(name) ?? new FakeCache(async (path) => new Response(`cached ${path}`))
      caches.set(name, cache)
      return cache
    }),
    keys: async () => [...caches.keys()],
    delete: async (name: string) => caches.delete(name),
  }
  const scope = {
    __buildVersion: 'test',
    __buildFiles: buildFiles,
    location: new URL(`${origin}/sw.js`),
    clients: { claim: vi.fn<() => Promise<undefined>>(async () => undefined) },
    skipWaiting: vi.fn<() => void>(),
    addEventListener: (type: string, handler: (event: unknown) => void) => handlers.set(type, handler),
  }
  new Function('self', 'caches', 'fetch', workerSource)(scope, cacheStorage, network)

  async function lifecycle(type: 'install' | 'activate') {
    let work: Promise<unknown> = Promise.resolve()
    handlers.get(type)?.({ waitUntil: (promise: Promise<unknown>) => (work = promise) })
    await work
  }

  function request(path: string, init: Partial<FakeRequest> = {}): FetchEvent {
    const event = { request: { url: new URL(path, origin).href, method: 'GET', mode: 'cors', ...init }, respondWith: vi.fn<(response: Promise<Response>) => void>() }
    handlers.get('fetch')?.(event)
    return event
  }

  async function answer(event: FetchEvent): Promise<string> {
    expect(event.respondWith).toHaveBeenCalledOnce()
    const response = await event.respondWith.mock.calls[0][0]
    return response.text()
  }

  return { scope, network, caches, cacheStorage, lifecycle, request, answer, handlers }
}

describe('service worker', () => {
  it('caches the application shell at the installation', async () => {
    const worker = startWorker()
    await worker.lifecycle('install')

    expect([...(worker.caches.get('reliquary-shell-test')?.entries.keys() ?? [])]).toEqual(buildFiles)
  })

  it('deletes the caches of the old versions at the activation', async () => {
    const worker = startWorker()
    await worker.lifecycle('install')
    await worker.lifecycle('activate')

    expect([...worker.caches.keys()]).toEqual(['reliquary-shell-test'])
    expect(worker.scope.clients.claim).toHaveBeenCalled()
  })

  it.each([
    ['GET', 'cors', '/api/v1/decks'],
    ['GET', 'cors', '/api/v1/status'],
    ['POST', 'cors', '/api/v1/auth/refresh'],
    ['GET', 'navigate', '/api/v1/auth/external/github/start'],
    ['GET', 'cors', '/api'],
  ])('does not answer the API request %s %s %s', (method, mode, path) => {
    const worker = startWorker()
    const event = worker.request(path, { method, mode })

    expect(event.respondWith).not.toHaveBeenCalled()
    expect(worker.cacheStorage.open).not.toHaveBeenCalled()
    expect(worker.network).not.toHaveBeenCalled()
  })

  it('never puts an API response in the cache', async () => {
    const worker = startWorker()
    await worker.lifecycle('install')
    worker.request('/api/v1/cards/search?q=bolt')
    worker.request('/api/v1/me')

    const cachedPaths = [...worker.caches.values()].flatMap((cache) => [...cache.entries.keys()])
    expect(cachedPaths.filter((path) => path.startsWith('/api'))).toEqual([])
  })

  it('does not answer a request to another origin or a write request', () => {
    const worker = startWorker()

    expect(worker.request('https://cards.scryfall.io/large/front/a.jpg').respondWith).not.toHaveBeenCalled()
    expect(worker.request('/assets/index-abc.js', { method: 'POST' }).respondWith).not.toHaveBeenCalled()
  })

  it('gives the files of the application shell from the cache', async () => {
    const worker = startWorker()
    await worker.lifecycle('install')

    expect(await worker.answer(worker.request('/assets/index-abc.js'))).toBe('cached /assets/index-abc.js')
    expect(worker.network).not.toHaveBeenCalled()
  })

  it('gets a page from the network, and gives the cached shell when the network is not available', async () => {
    const worker = startWorker()
    await worker.lifecycle('install')

    expect(await worker.answer(worker.request('/decks', { mode: 'navigate' }))).toBe(`network ${origin}/decks`)

    worker.network.mockRejectedValueOnce(new TypeError('offline'))
    expect(await worker.answer(worker.request('/decks', { mode: 'navigate' }))).toBe('cached /index.html')
  })

  it('starts the waiting worker when the web app asks for it', () => {
    const worker = startWorker()
    worker.handlers.get('message')?.({ data: { type: 'skipWaiting' } })

    expect(worker.scope.skipWaiting).toHaveBeenCalled()
  })
})
