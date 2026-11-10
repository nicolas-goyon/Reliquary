import { useEffect } from 'react'
import { activePlugins, type ExtensionList } from '../api/extensions'
import { useAuth } from '../auth/session'

/**
 * Starts the plugin runtime when the user has an active plugin. See docs/decisions/0049-plugin-sandbox.md.
 * This component is in the main bundle. It loads the runtime module only when a plugin is active.
 */
export function PluginRuntime() {
  const { state, request } = useAuth()
  const accountId = state.status === 'authenticated' ? state.account.id : null

  useEffect(() => {
    if (!accountId) {
      return
    }
    let cancelled = false
    let stop: (() => Promise<void>) | undefined
    request<ExtensionList>('/extensions')
      .then(async (list) => {
        if (cancelled || !list || activePlugins(list).length === 0) {
          return
        }
        const { startPluginRuntime } = await import('./runtime')
        if (!cancelled) {
          const host = startPluginRuntime(list)
          stop = () => host.stopAll()
        }
      })
      .catch((error: unknown) => console.warn('The core did not load the plugins.', error))
    return () => {
      cancelled = true
      void stop?.()
    }
  }, [accountId, request])

  return null
}
