/**
 * The registration of the service worker and the update flow. See docs/development/web-app.md.
 *
 * 1. The browser installs a new worker after a new release. The new worker waits.
 * 2. The web app shows the update prompt.
 * 3. The user selects "Update". The web app tells the waiting worker to start.
 * 4. The new worker takes control, and the web app loads the page again.
 */

const updateCheckInterval = 60 * 60 * 1000

let waitingWorker: ServiceWorker | null = null
let updateRequested = false
const listeners = new Set<() => void>()

function setWaitingWorker(worker: ServiceWorker | null): void {
  waitingWorker = worker
  for (const listener of listeners) {
    listener()
  }
}

/** Calls the listener when an update becomes available. */
export function subscribeToUpdate(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Tells if a new version of the web app waits for the approval of the user. */
export function isUpdateAvailable(): boolean {
  return waitingWorker !== null
}

/** Starts the waiting worker. The page loads again when the new worker takes control. */
export function applyUpdate(): void {
  if (!waitingWorker) {
    return
  }
  updateRequested = true
  waitingWorker.postMessage({ type: 'skipWaiting' })
}

/**
 * Registers the service worker at /sw.js.
 * @param container The service worker container of the browser. The tests give a fake container.
 * @param reload Loads the page again. The tests give a fake function.
 */
export async function registerServiceWorker(container: ServiceWorkerContainer, reload: () => void = () => window.location.reload()): Promise<void> {
  // The first worker also takes control. Only a change that the user asked for loads the page again.
  container.addEventListener('controllerchange', () => {
    if (updateRequested) {
      updateRequested = false
      reload()
    }
  })

  const registration = await container.register('/sw.js', { scope: '/' })
  if (registration.waiting && container.controller) {
    setWaitingWorker(registration.waiting)
  }
  registration.addEventListener('updatefound', () => {
    const worker = registration.installing
    worker?.addEventListener('statechange', () => {
      // Without a controller, this is the first worker, not an update.
      if (worker.state === 'installed' && container.controller) {
        setWaitingWorker(worker)
      }
    })
  })
  window.setInterval(() => {
    registration.update().catch(() => undefined)
  }, updateCheckInterval)
}

/** Clears the state. Only for the tests. */
export function resetRegistrationState(): void {
  updateRequested = false
  setWaitingWorker(null)
}
