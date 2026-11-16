import { useContext, useEffect, useRef, useState } from 'react'
import { activePlugins, type ExtensionList } from '../api/extensions'
import { useAuth } from '../auth/session'
import { SlotRegistryContext } from '../slots/registry'
import { developmentPluginUrls } from './development'
import { pluginCheckIntervalMs, pluginsChangedEvent, runningPluginsKey } from './events'

/**
 * Starts the plugin runtime when the user has an active plugin or a development plugin. See docs/decisions/0049-plugin-sandbox.md.
 * This component is in the main bundle. It loads the runtime module only when a plugin must run.
 * After `notifyPluginsChanged()`, it gets the extensions again and starts the runtime again.
 * While a plugin runs, it also gets the extensions each `pluginCheckIntervalMs` and when the page becomes visible.
 * When the active plugins or their versions changed, for example after the revocation of a version, it starts the runtime again.
 */
export function PluginRuntime() {
  const { state, request } = useAuth()
  const registry = useContext(SlotRegistryContext) ?? undefined
  const accountId = state.status === 'authenticated' ? state.account.id : null
  const [revision, setRevision] = useState(0)
  const running = useRef<string | null>(null)

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
        running.current = runningPluginsKey(list)
        const developmentUrls = list.plugins?.developmentPlugins ? developmentPluginUrls() : []
        if (activePlugins(list).length === 0 && developmentUrls.length === 0) {
          return
        }
        const { startPluginRuntime } = await import('./runtime')
        if (!cancelled) {
          const host = startPluginRuntime(list, { developmentUrls, registry })
          stop = () => host.stopAll()
        }
      })
      .catch((error: unknown) => console.warn('The core did not load the plugins.', error))
    return () => {
      cancelled = true
      running.current = null
      void stop?.()
    }
  }, [accountId, request, revision, registry])

  useEffect(() => {
    if (!accountId) {
      return
    }
    let cancelled = false
    const check = () => {
      const current = running.current
      if (!current) {
        return
      }
      request<ExtensionList>('/extensions')
        .then((list) => {
          if (!cancelled && list && running.current === current && runningPluginsKey(list) !== current) {
            setRevision((value) => value + 1)
          }
        })
        .catch((error: unknown) => console.warn('The core did not check the plugins.', error))
    }
    const timer = window.setInterval(check, pluginCheckIntervalMs)
    const visible = () => {
      if (document.visibilityState === 'visible') {
        check()
      }
    }
    document.addEventListener('visibilitychange', visible)
    return () => {
      cancelled = true
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', visible)
    }
  }, [accountId, request])

  return null
}
