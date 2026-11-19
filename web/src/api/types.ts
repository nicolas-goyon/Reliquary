/** An account, as the owner sees it. */
export interface Account {
  id: string
  email: string
  displayName: string
  language: 'en' | 'fr'
  isAdministrator: boolean
  createdAt: string
}

/** A new access token for a web session. */
export interface SessionResponse {
  accessToken: string
  tokenType: 'Bearer'
  expiresIn: number
  expiresAt: string
  account: Account
}

export interface MeResponse {
  account: Account
  externalLogins: string[]
}

export interface ApiKey {
  id: string
  name: string
  prefix: string
  createdAt: string
  lastUsedAt: string | null
}

export interface CreatedApiKey {
  id: string
  name: string
  prefix: string
  key: string
  createdAt: string
}

export interface ProvidersResponse {
  providers: string[]
  /** Whether the instance has the development sign-in, with no OAuth provider. */
  developmentSignIn: boolean
}
