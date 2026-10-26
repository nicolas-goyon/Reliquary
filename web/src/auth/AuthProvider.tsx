import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { ApiError, apiRequest, type RequestOptions } from '../api/client'
import type { Account, SessionResponse } from '../api/types'
import i18n, { isSupportedLanguage } from '../i18n'
import { AuthContext, type AuthContextValue, type AuthState } from './session'

// The provider gets a new access token this time before the current token expires.
const refreshMargin = 60_000

/**
 * Keeps the web session: the access token stays in memory, and the refresh token stays in an HttpOnly cookie.
 * When the page opens, the provider gets an access token from the cookie.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading' })
  const accessToken = useRef<string | null>(null)
  const refreshTimer = useRef<number | undefined>(undefined)
  const refreshing = useRef<Promise<string | null> | null>(null)

  const clearSession = useCallback(() => {
    accessToken.current = null
    window.clearTimeout(refreshTimer.current)
    setState({ status: 'anonymous' })
  }, [])

  const refresh = useRef<() => Promise<string | null>>(() => Promise.resolve(null))

  const acceptSession = useCallback((token: string, expiresIn: number, account: Account) => {
    accessToken.current = token
    setState({ status: 'authenticated', account })
    if (isSupportedLanguage(account.language) && i18n.resolvedLanguage !== account.language) {
      void i18n.changeLanguage(account.language)
    }
    window.clearTimeout(refreshTimer.current)
    refreshTimer.current = window.setTimeout(() => void refresh.current(), Math.max(10_000, expiresIn * 1000 - refreshMargin))
  }, [])

  const doRefresh = useCallback((): Promise<string | null> => {
    // Only one refresh at a time: the API replaces the cookie at each refresh.
    refreshing.current ??= apiRequest<SessionResponse>('/auth/refresh', { method: 'POST' })
      .then((session) => {
        acceptSession(session.accessToken, session.expiresIn, session.account)
        return session.accessToken
      })
      .catch((error: unknown) => {
        if ((error instanceof ApiError && error.status === 401) || !accessToken.current) {
          clearSession()
        }
        return null
      })
      .finally(() => {
        refreshing.current = null
      })
    return refreshing.current
  }, [acceptSession, clearSession])

  useEffect(() => {
    refresh.current = doRefresh
  }, [doRefresh])

  useEffect(() => {
    void doRefresh()
    return () => window.clearTimeout(refreshTimer.current)
  }, [doRefresh])

  const signIn = useCallback(
    async (email: string, password: string) => {
      const session = await apiRequest<SessionResponse>('/auth/sign-in', { method: 'POST', body: { email, password } })
      acceptSession(session.accessToken, session.expiresIn, session.account)
      return session.account
    },
    [acceptSession],
  )

  const signOut = useCallback(async () => {
    try {
      await apiRequest('/auth/sign-out', { method: 'POST' })
    } finally {
      clearSession()
    }
  }, [clearSession])

  const request = useCallback(async <T,>(path: string, options: Omit<RequestOptions, 'accessToken'> = {}) => {
    const token = accessToken.current ?? (refreshing.current ? await refreshing.current : null)
    try {
      return await apiRequest<T>(path, { ...options, accessToken: token ?? undefined })
    } catch (error) {
      const expired = error instanceof ApiError && error.status === 401 && token !== null
      if (!expired) {
        throw error
      }
      const newToken = await doRefresh()
      if (!newToken) {
        throw error
      }
      return apiRequest<T>(path, { ...options, accessToken: newToken })
    }
  }, [doRefresh])

  const updateAccount = useCallback((account: Account) => setState({ status: 'authenticated', account }), [])

  const value = useMemo<AuthContextValue>(
    () => ({ state, signIn, signOut, request, acceptSession, updateAccount }),
    [state, signIn, signOut, request, acceptSession, updateAccount],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
