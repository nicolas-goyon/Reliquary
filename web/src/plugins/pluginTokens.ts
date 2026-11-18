/**
 * The plugin tokens that the core keeps for the running plugins. See docs/decisions/0049-plugin-sandbox.md.
 * The core never gives a plugin token to a plugin frame. It sends the requests of the plugin with the token.
 */

/** The core gets a new plugin token when the token expires in less than this time, in milliseconds. */
const tokenMarginMs = 60_000

/** The prefix of the token ID of a development plugin. The API gives this token only on an instance that allows development plugins. */
export const developmentTokenPrefix = 'dev:'

interface TokenResponse {
  token: string
  expiresAt: string
}

/** Sends a request with the web session of the user. */
export type SessionRequest = <T>(path: string, options?: { method?: string; body?: unknown }) => Promise<T>

export interface PluginTokensOptions {
  request: SessionRequest
  /** The clock, in milliseconds. */
  now?: () => number
}

/** The plugin that asks for a token. A development plugin gives the scopes of its manifest, because the instance does not have its manifest. */
export interface TokenPlugin {
  id: string
  development?: boolean
  permissions?: string[]
}

/** Gets and keeps one plugin token for each running plugin. */
export class PluginTokens {
  private readonly options: PluginTokensOptions
  private readonly tokens = new Map<string, Promise<{ token: string; expiresAt: number }>>()

  constructor(options: PluginTokensOptions) {
    this.options = options
  }

  /** Gets a plugin token for the plugin. The core keeps it until one minute before it expires. */
  async get(plugin: TokenPlugin): Promise<string> {
    const now = this.options.now ?? Date.now
    const cached = this.tokens.get(plugin.id)
    const fresh = () => {
      const id = plugin.development ? `${developmentTokenPrefix}${plugin.id}` : plugin.id
      return this.options
        .request<TokenResponse>(`/me/extensions/${encodeURIComponent(id)}/token`, {
          method: 'POST',
          body: plugin.development ? { scopes: plugin.permissions ?? [] } : undefined,
        })
        .then((response) => ({ token: response.token, expiresAt: Date.parse(response.expiresAt) }))
    }
    const next = cached ? cached.then((value) => (value.expiresAt - tokenMarginMs > now() ? value : fresh()), fresh) : fresh()
    this.tokens.set(plugin.id, next)
    next.catch(() => {
      if (this.tokens.get(plugin.id) === next) {
        this.tokens.delete(plugin.id)
      }
    })
    return (await next).token
  }

  /** Forgets the token of a plugin, for example when its frame stops or when the API refuses the token. */
  forget(pluginId: string): void {
    this.tokens.delete(pluginId)
  }
}
