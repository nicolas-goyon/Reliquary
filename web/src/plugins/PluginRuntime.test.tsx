import { act, render, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ExtensionList } from '../api/extensions'
import { AuthContext, type AuthContextValue, type AuthState } from '../auth/session'
import { testAccount } from '../test/mockApi'
import { notifyPluginsChanged } from './events'
import { PluginRuntime } from './PluginRuntime'

const runtime = vi.hoisted(() => ({
  startPluginRuntime: vi.fn<(list: unknown) => { stopAll(): Promise<void> }>(() => ({ stopAll: vi.fn<() => Promise<void>>(async () => undefined) })),
}))
vi.mock('./runtime', () => runtime)

function list(active: boolean): ExtensionList {
  return {
    coreApi: '1.1.0',
    plugins: { maxActive: 8, frameOrigin: null },
    items: [
      { id: 'moxfield-sync', kind: 'plugin', name: 'Moxfield', version: '0.1.0', coreApi: '^1.0', permissions: [], slots: [], enabled: true, compatible: true, active },
      { id: 'module', kind: 'module', name: 'Module', version: '1.0.0', coreApi: '^1.0', permissions: [], slots: [], enabled: true, compatible: true, active: true },
    ],
  }
}

function renderRuntime(state: AuthState, response: ExtensionList | (() => ExtensionList)) {
  const request = vi.fn<(path: string) => Promise<ExtensionList>>(async () => (typeof response === 'function' ? response() : response))
  const value = { state, request } as unknown as AuthContextValue
  const view = render(
    <AuthContext.Provider value={value}>
      <PluginRuntime />
    </AuthContext.Provider>,
  )
  return { request, view }
}

describe('PluginRuntime', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('does not ask the extensions for an anonymous user', async () => {
    const { request } = renderRuntime({ status: 'anonymous' }, list(true))

    await new Promise((resolve) => setTimeout(resolve, 10))

    expect(request).not.toHaveBeenCalled()
    expect(runtime.startPluginRuntime).not.toHaveBeenCalled()
  })

  it('does not load the runtime when the user has no active plugin', async () => {
    const { request } = renderRuntime({ status: 'authenticated', account: testAccount }, list(false))

    await waitFor(() => expect(request).toHaveBeenCalledWith('/extensions'))
    await new Promise((resolve) => setTimeout(resolve, 10))

    expect(runtime.startPluginRuntime).not.toHaveBeenCalled()
  })

  it('starts the runtime when the user has an active plugin, and stops it at sign-out', async () => {
    const response = list(true)
    const { view } = renderRuntime({ status: 'authenticated', account: testAccount }, response)

    await waitFor(() => expect(runtime.startPluginRuntime).toHaveBeenCalledWith(response))
    const host = runtime.startPluginRuntime.mock.results[0].value
    view.unmount()

    expect(host.stopAll).toHaveBeenCalledTimes(1)
  })

  it('starts the runtime after the activation of a plugin in the same session', async () => {
    let response = list(false)
    const { request } = renderRuntime({ status: 'authenticated', account: testAccount }, () => response)
    await waitFor(() => expect(request).toHaveBeenCalledTimes(1))

    response = list(true)
    act(() => notifyPluginsChanged())

    await waitFor(() => expect(runtime.startPluginRuntime).toHaveBeenCalledWith(response))
    expect(request).toHaveBeenCalledTimes(2)
  })

  it('stops the running plugins before it starts them again', async () => {
    renderRuntime({ status: 'authenticated', account: testAccount }, list(true))
    await waitFor(() => expect(runtime.startPluginRuntime).toHaveBeenCalledTimes(1))
    const first = runtime.startPluginRuntime.mock.results[0].value

    act(() => notifyPluginsChanged())

    await waitFor(() => expect(runtime.startPluginRuntime).toHaveBeenCalledTimes(2))
    expect(first.stopAll).toHaveBeenCalledTimes(1)
  })
})
