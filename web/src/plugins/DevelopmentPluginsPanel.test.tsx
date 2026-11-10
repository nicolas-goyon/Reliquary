import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ExtensionList } from '../api/extensions'
import { AuthContext, type AuthContextValue } from '../auth/session'
import '../i18n'
import { testAccount } from '../test/mockApi'
import { developmentPluginUrls } from './development'
import { DevelopmentPluginsPanel } from './DevelopmentPluginsPanel'

function extensions(developmentPlugins: boolean): ExtensionList {
  return { coreApi: '1.1.0', items: [], plugins: { maxActive: 8, frameOrigin: null, developmentPlugins } }
}

function renderPanel(developmentPlugins: boolean) {
  const request = vi.fn<(path: string) => Promise<ExtensionList>>(async () => extensions(developmentPlugins))
  const value = { state: { status: 'authenticated', account: testAccount }, request } as unknown as AuthContextValue
  render(
    <AuthContext.Provider value={value}>
      <DevelopmentPluginsPanel />
    </AuthContext.Provider>,
  )
  return { user: userEvent.setup() }
}

describe('DevelopmentPluginsPanel', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows nothing on an instance that does not allow development plugins', async () => {
    renderPanel(false)

    await new Promise((resolve) => setTimeout(resolve, 20))

    expect(screen.queryByRole('heading', { name: 'Development plugins' })).not.toBeInTheDocument()
  })

  it('loads a plugin from the URL of its manifest and shows the "Not checked" banner', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(async () => Response.json({ id: 'dev-plugin', name: 'Dev plugin', version: '0.0.1', entry: 'src/main.ts' })),
    )
    const { user } = renderPanel(true)
    const panel = (await screen.findByRole('heading', { name: 'Development plugins' })).closest('section')!

    await user.type(within(panel).getByLabelText('URL of the plugin manifest'), 'http://localhost:5173/manifest.json')
    await user.click(within(panel).getByRole('button', { name: 'Load the plugin' }))

    expect(await within(panel).findByRole('status')).toHaveTextContent('The plugin "Dev plugin" runs now in this browser.')
    expect(within(panel).getByRole('note')).toHaveTextContent('Not checked. The plugin http://localhost:5173/manifest.json comes from a development URL.')
    expect(developmentPluginUrls()).toEqual(['http://localhost:5173/manifest.json'])

    await user.click(within(panel).getByRole('button', { name: 'Remove http://localhost:5173/manifest.json' }))

    expect(within(panel).getByText('You have no development plugin.')).toBeInTheDocument()
    expect(developmentPluginUrls()).toEqual([])
  })

  it('shows an error for an incorrect URL and for a manifest that does not load', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response('', { status: 404 })))
    const { user } = renderPanel(true)
    const panel = (await screen.findByRole('heading', { name: 'Development plugins' })).closest('section')!
    const input = within(panel).getByLabelText('URL of the plugin manifest')

    await user.type(input, 'manifest.json')
    await user.click(within(panel).getByRole('button', { name: 'Load the plugin' }))
    expect(within(panel).getByText('The URL is not correct. Enter an absolute http or https URL.')).toBeInTheDocument()

    await user.clear(input)
    await user.type(input, 'http://localhost:5173/manifest.json')
    await user.click(within(panel).getByRole('button', { name: 'Load the plugin' }))
    expect(await within(panel).findByText(/The plugin manifest did not load/)).toBeInTheDocument()
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(developmentPluginUrls()).toEqual([])
  })
})
