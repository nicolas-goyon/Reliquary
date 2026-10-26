import { vi } from 'vitest'

export type Handler = (request: { body: unknown; headers: Record<string, string> }) => Response | Promise<Response>

/** A problem response of the API. */
export function problem(status: number, code: string, errors: { field: string; code: string; message: string }[] = []): Response {
  return Response.json({ status, code, detail: code, errors }, { status })
}

export const testAccount = {
  id: '0190a000-0000-7000-8000-000000000001',
  email: 'ana@example.org',
  emailChecked: true,
  displayName: 'Ana',
  language: 'en',
  isAdministrator: false,
  hasPassword: true,
  createdAt: '2026-10-26T10:00:00Z',
} as const

export function session(token = 'token-1', account: Record<string, unknown> = testAccount): Response {
  return Response.json({ accessToken: token, tokenType: 'Bearer', expiresIn: 900, expiresAt: '2026-10-26T10:15:00Z', account })
}

/**
 * Replaces fetch with handlers for "METHOD /path" keys. A request with no handler fails the test.
 * @returns The list of the requests, for the assertions.
 */
export function mockApi(handlers: Record<string, Handler>) {
  const calls: { key: string; body: unknown; headers: Record<string, string> }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string, init: RequestInit = {}) => {
      const url = new URL(input, 'http://localhost')
      const key = `${init.method ?? 'GET'} ${url.pathname.replace(/^\/api\/v1/, '')}`
      const body = typeof init.body === 'string' ? JSON.parse(init.body) : undefined
      const headers = (init.headers ?? {}) as Record<string, string>
      calls.push({ key, body, headers })
      const handler = handlers[key]
      if (!handler) {
        throw new Error(`The test has no handler for ${key}.`)
      }
      return handler({ body, headers })
    }),
  )
  return calls
}
