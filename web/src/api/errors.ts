import { ApiError, networkErrorCode } from './client'

/** The error codes that the interface translates. Other codes show a general message. */
export const translatedErrorCodes = [
  networkErrorCode,
  'internal_error',
  'service_unavailable',
  'validation_failed',
  'authentication_required',
  'invalid_authentication',
  'forbidden',
  'not_found',
  'rate_limited',
  'quota_exceeded',
  'invalid_credentials',
  'email_not_checked',
  'invalid_token',
  'session_expired',
  'api_key_limit',
  'provider_not_enabled',
  'external_sign_in_failed',
  'external_email_missing',
  'external_email_not_verified',
  'search_syntax_error',
  'search_query_empty',
  'search_timeout',
] as const

export type TranslatedErrorCode = (typeof translatedErrorCodes)[number]

/** The field error codes that the interface translates. */
export const translatedFieldCodes = ['required', 'too_short', 'too_long', 'invalid_format', 'not_allowed'] as const

export type TranslatedFieldCode = (typeof translatedFieldCodes)[number]

function isOneOf<T extends string>(values: readonly T[], value: string): value is T {
  return values.some((candidate) => candidate === value)
}

/** Gets the translation key of an error code. */
export function errorKey(code: string): `errors.${TranslatedErrorCode}` | 'errors.unknown' {
  return isOneOf(translatedErrorCodes, code) ? `errors.${code}` : 'errors.unknown'
}

/** Gets the translation key of a field error code. */
export function fieldErrorKey(code: string): `fieldErrors.${TranslatedFieldCode}` | 'fieldErrors.unknown' {
  return isOneOf(translatedFieldCodes, code) ? `fieldErrors.${code}` : 'fieldErrors.unknown'
}

/** Gets the error code of any error. */
export function errorCode(error: unknown): string {
  return error instanceof ApiError ? error.code : 'internal_error'
}
