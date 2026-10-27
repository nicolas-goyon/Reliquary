/** The error of one field in a validation error of the API. */
export interface FieldError {
  field: string
  code: string
  message: string
}

/** An error response of the API: problem details with a stable code. See docs/architecture/api-conventions.md. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly fieldErrors: FieldError[]
  /** The other members of the problem response, for example `position` or `lineErrors`. */
  readonly details: Record<string, unknown>

  constructor(status: number, code: string, message: string, fieldErrors: FieldError[] = [], details: Record<string, unknown> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
    this.details = details
  }

  /** Gets the error code of a field, or undefined when the field has no error. */
  fieldCode(field: string): string | undefined {
    return this.fieldErrors.find((error) => error.field === field)?.code
  }
}

/** The code of an error that is not an API response, for example a network failure. */
export const networkErrorCode = 'network_error'

export interface RequestOptions {
  method?: string
  body?: unknown
  accessToken?: string
  signal?: AbortSignal
}

/**
 * Sends a request to the public API.
 * The browser sends the cookies of the site, so the refresh cookie reaches the authentication endpoints.
 * @returns The JSON body of the response, or undefined for a response with no content.
 * @throws ApiError when the API answers with an error, or when the API is not available.
 */
export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  if (options.accessToken) {
    headers.Authorization = `Bearer ${options.accessToken}`
  }

  let response: Response
  try {
    response = await fetch(`/api/v1${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      credentials: 'same-origin',
      signal: options.signal,
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error
    }
    throw new ApiError(0, networkErrorCode, 'The API is not available.')
  }

  if (!response.ok) {
    throw await readError(response)
  }

  if (response.status === 204 || response.status === 202 || response.headers.get('Content-Length') === '0') {
    return undefined as T
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

async function readError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as { code?: string; detail?: string; errors?: FieldError[] } & Record<string, unknown>
    return new ApiError(response.status, problem.code ?? 'internal_error', problem.detail ?? response.statusText, problem.errors ?? [], problem)
  } catch {
    return new ApiError(response.status, response.status >= 500 ? 'internal_error' : 'bad_request', response.statusText)
  }
}
