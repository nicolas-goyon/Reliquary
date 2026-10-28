// The service worker of Reliquary. See docs/development/web-app.md.
//
// The build (vite.service-worker.ts) writes this file to dist/sw.js. It adds two values before the code:
// - self.__buildVersion: a hash of the build. Each new build gets a new cache.
// - self.__buildFiles: the paths of the application shell, for example /index.html and the files in /assets/.
//
// Rules:
// - The worker never answers a request to /api/. The API data always comes from the network.
// - The worker answers only GET requests to the same origin.
// - A page navigation goes to the network first. When the network is not available, the worker gives the cached index.html.
// - The files of the application shell come from the cache first.

const cachePrefix = 'reliquary-shell-'
const cacheName = cachePrefix + (self.__buildVersion ?? 'development')
const shellFiles = self.__buildFiles ?? ['/index.html']
const shellPaths = new Set(shellFiles)
const indexPath = '/index.html'

/**
 * Gives the strategy for a request.
 * @returns "network" when the worker must not answer, "navigation" for a page, or "cache" for a file of the application shell.
 */
function strategyFor(request) {
  if (request.method !== 'GET') {
    return 'network'
  }
  const url = new URL(request.url)
  if (url.origin !== self.location.origin) {
    return 'network'
  }
  if (url.pathname === '/api' || url.pathname.startsWith('/api/')) {
    return 'network'
  }
  if (request.mode === 'navigate') {
    return 'navigation'
  }
  if (shellPaths.has(url.pathname) || url.pathname.startsWith('/assets/')) {
    return 'cache'
  }
  return 'network'
}

async function fromNetworkThenShell(request) {
  try {
    return await fetch(request)
  } catch (error) {
    const cache = await caches.open(cacheName)
    const shell = await cache.match(indexPath)
    if (shell) {
      return shell
    }
    throw error
  }
}

async function fromCacheThenNetwork(request) {
  const cache = await caches.open(cacheName)
  const cached = await cache.match(request)
  if (cached) {
    return cached
  }
  const response = await fetch(request)
  if (response.ok) {
    await cache.put(request, response.clone())
  }
  return response
}

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(cacheName).then((cache) => cache.addAll(shellFiles)))
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((names) => Promise.all(names.filter((name) => name.startsWith(cachePrefix) && name !== cacheName).map((name) => caches.delete(name))))
      .then(() => self.clients.claim()),
  )
})

// The web app sends this message when the user accepts the update.
self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'skipWaiting') {
    self.skipWaiting()
  }
})

self.addEventListener('fetch', (event) => {
  const strategy = strategyFor(event.request)
  if (strategy === 'navigation') {
    event.respondWith(fromNetworkThenShell(event.request))
  } else if (strategy === 'cache') {
    event.respondWith(fromCacheThenNetwork(event.request))
  }
})
