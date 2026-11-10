import { useEffect, useState } from 'react'
import { activePlugins, type ExtensionList } from '../api/extensions'
import { useAuth } from '../auth/session'
import { developmentPluginUrls } from './development'
import { pluginsChangedEvent } from './events'

/**
 * Starts the plugin runtime when the user has an active plugin or a development plugin. See docs/decisions/0049-plugin-sandbox.md.
 * This component is in the main bundle. It loads the runtime module only when a plugin must run.
 * After `notifyPluginsChanged()`, it gets the extensions again and starts the runtime again.
 */
export function PluginRuntime() {
  const { state, request } = useAuth()
  const accountId = state.status === 'authenticated' ? state.account.id : null
  const [revision, setRevision] = useState(0)

  useEffect(() => {
    const changed = () => setRevision((value) => value + 1)
    window.addEventListener(pluginsChangedEvent, changed)
    return () => window.removeEventListener(pluginsChangedEvent, changed)
  }, [])

  useEffect(() => {
    if (!accountId) {
      return
    }
    let cancelled = false
    let stop: (() => Promise<void>) | undefined
    request<ExtensionList>('/extensions')
      .then(async (list) => {
        if (cancelled || !list) {
          return
        }
        const developmentUrls = list.plugins?.developmentPlugins ? developmentPluginUrls() : []
        if (activePlugins(list).length === 0 && developmentUrls.length === 0) {
          return
        }
        const { startPluginRuntime } = await import('./runtime')
        if (!cancelled) {
          const host = startPluginRuntime(list, { developmentUrls })
          stop = () => host.stopAll()
        }
      })
      .catch((error: unknown) => console.warn('The core did not load the plugins.', error))
    return () => {
      cancelled = true
      void stop?.()
    }
  }, [accountId, request, revision])

  return null
}
