import type { Page } from '@playwright/test'

/** What the test sees of the table hub and of the console of one page. */
export interface PageWatch {
  label: string
  /** The reasons of the Resync calls. Only "view" is not a desynchronization. */
  resyncs: string[]
  /** The highest sequence number that the page got, from a patch or a snapshot. */
  lastSeq: number
  patches: number
  actions: number
  /** The Act calls with no completion yet. */
  pendingActs: Set<string>
  /** The reason codes of the refused actions, with their number. */
  rejected: Map<string, number>
  /** The hub errors, the page errors and the console errors that are not known. */
  errors: string[]
  lastPatch?: string
}

/** The SignalR JSON protocol puts the record separator 0x1e after each message. */
function messages(payload: string | Buffer): Array<Record<string, unknown>> {
  return String(payload)
    .split('\u001e')
    .filter((record) => record.trim().length > 0)
    .map((record) => JSON.parse(record) as Record<string, unknown>)
}

/**
 * Tells whether a console error is a known error.
 * Before a sign-in, and for a guest, the web app asks for a new access token and gets the status 401.
 */
function isKnownConsoleError(text: string, url: string): boolean {
  return text.includes('status of 401') && url.includes('/api/v1/auth/refresh')
}

/** Records the hub frames, the page errors and the console errors of a page. */
export function watchPage(page: Page, label: string): PageWatch {
  const watch: PageWatch = { label, resyncs: [], lastSeq: 0, patches: 0, actions: 0, pendingActs: new Set(), rejected: new Map(), errors: [] }
  page.on('websocket', (socket) => {
    if (!socket.url().includes('/api/v1/playtest/hub')) return
    // A new connection has no call in progress. The page sends the actions again with the same ID.
    watch.pendingActs.clear()
    socket.on('framesent', ({ payload }) => {
      for (const message of messages(payload)) {
        if (message.type !== 1) continue
        if (message.target === 'Resync') {
          watch.resyncs.push(String((message.arguments as unknown[])[0]))
          if (process.env.E2E_DEBUG) console.log(`${label} Resync ${String((message.arguments as unknown[])[0])} after ${watch.lastPatch}`)
        }
        if (message.target === 'Act' && typeof message.invocationId === 'string') {
          watch.actions++
          watch.pendingActs.add(message.invocationId)
        }
      }
    })
    socket.on('framereceived', ({ payload }) => {
      for (const message of messages(payload)) {
        if (message.type === 1 && message.target === 'Patch') {
          watch.patches++
          const patch = (message.arguments as Array<{ seq: number }>)[0]
          watch.lastSeq = Math.max(watch.lastSeq, patch.seq)
          if (process.env.E2E_DEBUG) watch.lastPatch = JSON.stringify(patch).slice(0, 3000)
        }
        if (message.type === 3) {
          const id = String(message.invocationId)
          const wasAct = watch.pendingActs.delete(id)
          if (message.error) watch.errors.push(`${label} hub error: ${String(message.error)}`)
          const result = message.result as { seq?: number; rejected?: string } | null | undefined
          if (result && typeof result.seq === 'number') watch.lastSeq = Math.max(watch.lastSeq, result.seq)
          if (wasAct && result?.rejected) watch.rejected.set(result.rejected, (watch.rejected.get(result.rejected) ?? 0) + 1)
        }
      }
    })
  })
  page.on('pageerror', (error) => watch.errors.push(`${label} page error: ${error.message}`))
  page.on('console', (message) => {
    if (message.type() === 'error' && !isKnownConsoleError(message.text(), message.location().url)) {
      watch.errors.push(`${label} console error: ${message.text()}`)
    }
  })
  return watch
}

/** The Resync reasons that show a desynchronization. */
export function desyncs(watch: PageWatch): string[] {
  return watch.resyncs.filter((reason) => reason !== 'view')
}
