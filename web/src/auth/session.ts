import { createContext, useContext } from 'react'
import type { RequestOptions } from '../api/client'
import type { Account } from '../api/types'

export type AuthState =
  | { status: 'loading' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; account: Account }

export interface AuthContextValue {
  state: AuthState
  /** Starts a web session with a password. */
  signIn(email: string, password: string): Promise<Account>
  /** Stops the web session. */
  signOut(): Promise<void>
  /** Sends a request to the API with the access token. When the token expired, it gets a new token and sends the request again. */
  request<T>(path: string, options?: Omit<RequestOptions, 'accessToken'>): Promise<T>
  /** Keeps a new access token, for example after a change of the password. */
  acceptSession(accessToken: string, expiresIn: number, account: Account): void
  /** Replaces the account, for example after a change of the profile. */
  updateAccount(account: Account): void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

/** Gets the web session of the user. */
export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)
  if (!value) {
    throw new Error('useAuth needs an AuthProvider above the component.')
  }
  return value
}
